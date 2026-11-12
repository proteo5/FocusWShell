# ADR 0016 — Seguir los escritorios virtuales de Windows: un foco y unas apps por escritorio

- **Estado:** Propuesta (2026-11-11); fase 1 implementada. Se apoya en el spike de escritorios virtuales ([04 — Riesgos y spikes](../04-riesgos-y-spikes.md)); faltan las comprobaciones de S11.
- **Fecha:** 2026-11-11

## Contexto

Windows tiene escritorios virtuales: cada ventana pertenece a uno, y al cambiar de escritorio Windows oculta (*cloak*) las de los demás en **todos los monitores a la vez**. FocusWShell hoy no los conoce:

- Tiene **un solo espacio de trabajo** (`Workspace`): un foco, unos paneles y sus apps, para todos los escritorios.
- Descarta las ventanas *cloaked* como candidatas. Así que una app gestionada que el usuario manda a otro escritorio sigue en su panel del deck, pero FocusWShell no la "ve".
- En la prueba, tocar en el deck una app que estaba en el escritorio 2 **abrió una segunda instancia**, porque al no ver la ventana la dio por cerrada.
- En un escritorio vacío, el deck mostraba las apps del escritorio 1 y las tiras se dibujaban sobre la pantalla vacía.

Por otro lado, el spike mostró lo necesario para hacerlo bien con **sólo la API documentada** (`IVirtualDesktopManager`):

- El deck y las tiras son `WS_EX_TOOLWINDOW` y Windows no las asigna a ningún escritorio: se ven en todos sin hacer nada.
- `GetWindowDesktopId` dice en qué escritorio está cada ventana.
- El escritorio actual se averigua con una ventana propia recién creada, que nace en él.

Crear, cambiar, nombrar escritorios o mover ventanas de otras apps entre ellos sólo se puede con interfaces internas no documentadas, que cambian entre compilaciones de Windows (el usuario usa una Insider).

Las escenas propias ([M1](../05-hoja-de-ruta.md)) intentan lo mismo que los escritorios, cambiar de actividad, pero reacomodando ventanas con un toque y sin deshacer. Los escritorios lo hacen sin mover nada y son reversibles por naturaleza, como pide la [visión](../01-vision.md): "los cambios de escena son nombrados, visibles y reversibles".

## Decisión

**FocusWShell sigue los escritorios; no los maneja.** El usuario cambia de escritorio con lo de Windows (Ctrl+Win+← / →, cuatro dedos, Vista de tareas) y FocusWShell muestra y acomoda lo de ese escritorio.

### 1. Qué es de cada escritorio y qué es común

| Común a todos los escritorios | De cada escritorio |
|---|---|
| Los **paneles**: nombre, monitor, lado, puesto, presentación | Las **apps** de cada panel |
| La **disposición del principal** (vista y ancho) | El **foco** y su historial (Anterior) |
| El catálogo y las apps ancladas | Las libres |

Los paneles son el esqueleto del escritorio físico (monitores y zonas) y cambian poco; las apps son la actividad. Con paneles comunes, cambiar de escritorio no mueve tiras ni zonas: sólo cambia qué hay dentro.

**Alternativa descartada:** paneles propios por escritorio. Da más libertad, pero obliga a configurar cada escritorio por separado y las tiras cambiarían de sitio al cambiar de escritorio. Se puede añadir más adelante como "presentación por escritorio" si hace falta.

### 2. El modelo (Core)

Sigue habiendo un solo `Workspace`, sin dividirlo en uno por escritorio, para que los paneles sean de verdad comunes:

- `ManagedWindow` gana **`Desktop`**: el escritorio de la ventana, o `null` si está en todos (anclada con "Mostrar en todos los escritorios", si Windows la reporta así; ver S11).
- `Workspace` gana **`CurrentDesktop`**. El invariante "como máximo una ventana en el Centro" pasa a ser **por escritorio**, y el historial del Centro también.
- **El layout sólo coloca las ventanas del escritorio actual y las de todos.** Las de otros escritorios no se tocan: Windows las tiene ocultas y conservan su sitio para cuando se vuelva.
- Comandos nuevos, que vienen de la reconciliación ([ADR 0007](0007-cambios-externos-por-reconciliacion.md)):
  - `DesktopChanged(desktop)`: el usuario cambió de escritorio.
  - `WindowMovedToDesktop(handle, desktop)`: el usuario mandó una app a otro escritorio. **Conserva su panel**; si era la del foco, el foco de ese escritorio pasa a ser ella sólo si allí estaba vacío; si no, va a su panel.

Todo esto se prueba en `FocusWShell.Core.Tests` sin Win32, como el resto del dominio.

### 3. Win32: detectar el escritorio

- **Escritorio de una ventana:** `GetWindowDesktopId` en cada reconciliación, para las gestionadas.
- **Escritorio actual:** el de la ventana al frente, si pertenece a un escritorio. Si no (escritorio vacío: al frente queda `Progman`), con una ventana propia de sondeo recién creada.
- **Cuándo mirar:** no hay evento documentado de "cambió el escritorio". Se mira al cambiar la ventana al frente (`EVENT_SYSTEM_FOREGROUND`, que Windows lanza al cambiar de escritorio), al ocultarse o mostrarse ventanas (`EVENT_OBJECT_CLOAKED` / `UNCLOAKED`, ya previstos en [03 — Arquitectura](../03-arquitectura.md)) y en el sondeo periódico del deck.
- **Candidatas:** una ventana *cloaked* **porque está en otro escritorio** (`IsWindowOnCurrentVirtualDesktop` = falso) deja de descartarse sin más: si está gestionada, sigue gestionada en su escritorio; si no, se ignora como hasta ahora.
- **El registro** (`HKCU\…\Explorer\VirtualDesktops`) da nombres y orden de los escritorios, pero no está documentado. Se lee **sólo como adorno** (para mostrar "Escritorio 2" o su nombre); si falla o cambia de formato, el deck muestra un nombre genérico y todo sigue funcionando.

### 4. Nunca abrir otra copia

Antes de abrir una app anclada, FocusWShell busca **en todos los escritorios** una ventana de esa app. Si existe en otro, no abre nada: la **activa**, y Windows lleva al usuario a su escritorio (comportamiento a confirmar en S11). Esto se puede hacer antes que todo lo demás, porque arregla el fallo de la prueba.

### 5. El deck

- **Muestra sólo lo del escritorio actual**: su foco, las apps de cada panel y sus libres. Al cambiar de escritorio, el deck se actualiza solo.
- **Las ancladas abiertas en otro escritorio** se ven como un tile atenuado con la marca **"⧉ Escritorio 2"** (o su nombre). Tocarlo lleva a ese escritorio, en vez de abrir otra copia.
- **Pestañas de escritorios**, como las de monitores, para cambiar de actividad sin teclado ni ratón. Ver [Pestañas de escritorios](#pestañas-de-escritorios-en-el-deck).
- **Las escenas salen del deck.** Los escritorios las reemplazan. Las escenas siguen en la ventana principal, la bandeja y los atajos mientras se valida el uso; si los escritorios cubren la necesidad, se retiran en una versión posterior.

### Pestañas de escritorios en el deck

Sustituyen al indicador con ◀ ▶ que se pensó primero. Con ◀ ▶ había que ir de uno en uno, y no se veía cuántos escritorios había ni qué tenía cada uno. Las pestañas los muestran todos y se llega a cualquiera con un toque. Además, el deck ya usa ese patrón para los monitores.

- **Una fila encima de las pestañas de monitores.** Cambiar de escritorio cambia todos los monitores, así que el escritorio está más arriba en la jerarquía: primero el escritorio y, dentro, sus monitores. El orden lo dice sin explicarlo.
- **Con su propio aspecto**, para no confundirlas con las de monitores. Por ejemplo, icono de escritorio, nombre y número de apps («▣ Escritorio 2 · 4»). El número cuenta las apps gestionadas y libres de ese escritorio, y deja ver dónde está cada cosa antes de cambiar.
- **Tocar una pestaña cambia de escritorio de verdad.** Las de monitores sólo cambian lo que muestra el deck. Aquí no puede ser así: el deck muestra siempre lo que está en pantalla. Si enseñara el escritorio 2 mientras se ve el 1, volvería el problema que encontró la prueba.
- **Cómo se cambia:** enviando el atajo de Windows con `SendInput` (Ctrl+Win+← / →), que es documentado y estable. Para ir a un escritorio que no es vecino, el atajo se repite: del 1 al 3, dos veces. Se ve la animación de Windows en cada paso; es aceptable con pocos escritorios.
- **Una pestaña «+»** crea un escritorio (Ctrl+Win+D), y Windows pasa a él. El menú de una pestaña (clic derecho o pulsación larga) permite **cerrar** ese escritorio (Ctrl+Win+F4, tras pasar a él). Windows manda sus ventanas al escritorio vecino; FocusWShell las recibe como apps movidas de escritorio, sin cerrar nada.
- **Si no caben**, se recortan los nombres antes que las pestañas, como en las cabeceras de los paneles. Con muchos escritorios, la fila se desplaza.
- **Qué escritorios existen y cómo se llaman** sale del registro (`VirtualDesktopIDs`, nombres y orden). No está documentado, así que se lee como adorno ([punto 3](#3-win32-detectar-el-escritorio)). Si falla, se muestran sólo los escritorios que tienen ventanas (su GUID se sabe con la API documentada), con nombres genéricos, en el orden en que aparecieron, y el «+» sigue funcionando.

**Lo que no hacen, por límites de Windows:**
- **Mover una app a otro escritorio** arrastrándola a una pestaña. La API documentada (`MoveWindowToDesktop`) sólo mueve ventanas del propio proceso; para las de otras apps hace falta la interfaz interna, que cambia entre compilaciones. Se sigue haciendo desde la Vista de tareas.
- **Renombrar o reordenar** escritorios: también se hace desde la Vista de tareas.

### 6. Persistencia

- La sesión (`session.json`) guarda las apps **por escritorio** (por su GUID). Windows 11 conserva los escritorios y sus GUID al reiniciar.
- Si al volver falta un escritorio de la sesión (el usuario lo cerró), sus apps se reparten en el escritorio actual, en sus paneles, como hoy.
- `panels.json` no cambia: los paneles son comunes.

## Comprobaciones pendientes (spike S11)

Antes de programar las fases 2 y 3:

1. **Activar una ventana de otro escritorio** (`SetForegroundWindow` desde FocusWShell): ¿Windows cambia al escritorio de la ventana, o la ignora? ¿Y desde el deck, que no tiene el foco?
2. **Una ventana anclada a todos los escritorios** (Vista de tareas → "Mostrar esta ventana en todos los escritorios"): ¿qué devuelve `GetWindowDesktopId`?
3. **Eventos al cambiar de escritorio:** ¿llega siempre `EVENT_SYSTEM_FOREGROUND`, también al pasar a un escritorio vacío? ¿Cuánto tarda?
4. **`SendInput` de Ctrl+Win+→ desde un toque en el deck**: ¿cambia de escritorio sin que el deck tenga el foco? ¿Y repetido dos veces seguidas (saltar al escritorio no vecino): hay que esperar a que termine la animación?
5. **Al reiniciar**, ¿se conservan los GUID de los escritorios?
6. **La lista de escritorios del registro:** ¿se actualiza al momento al crear, cerrar o reordenar desde la Vista de tareas? ¿Un escritorio vacío aparece ahí?

## Fases

1. **Seguridad (pequeña, ya):** no abrir otra copia si la app existe en otro escritorio (punto 4), y no mostrar en el deck como "en su panel" una app que está en otro escritorio. Arregla lo que la prueba encontró, sin el resto del diseño.
2. **Un foco y unas apps por escritorio:** modelo (punto 2), detección (punto 3) y persistencia (punto 6).
3. **Deck:** pestañas de escritorios con «+» y cerrar, y escenas fuera del deck (punto 5). Los tiles de otros escritorios (apagados con «⧉») ya llegaron con la fase 1.

## Implementación de la fase 1 (2026-11-11)

- **Detección:** `WindowSnapshot.IsOnOtherDesktop`, sólo para las ventanas *cloaked* (`IsWindowOnCurrentVirtualDesktop` = falso). El filtro de elegibilidad la evalúa como si se viera y, si ninguna otra regla la excluye, da `ExclusionReason.OtherDesktop`. Una *cloaked* por otra causa (una app de la Store suspendida) sigue siendo `Cloaked`.
- **El controlador** guarda esas ventanas aparte en cada enumeración. Las gestionadas conservan su panel y su foto al día, pero **el layout no las coloca**; el foco del deck se ve vacío si la del foco está en otro escritorio, y «Anterior» no cuenta con ellas.
- **Nunca otra copia:** abrir una app (deck, lista, ventana principal) que tiene ventana en otro escritorio la activa en vez de lanzarla, y Windows debería llevar a su escritorio (S11, punto 1). Si Windows no la deja pasar al frente, se avisa. Las escenas tampoco abren las apps que están en otro escritorio.
- **Deck y lista:** la app de otro escritorio se ve apagada, como una cerrada, con **«⧉»**; no se puede mover, cerrar ni arrastrar desde aquí. Tocarla lleva a ella. Una gestionada se muestra en su panel aunque no esté anclada; una libre sólo si está anclada (ocupa el sitio de la cerrada), porque una libre no es de ningún panel. Las libres de otro escritorio no salen en LIBRES: no están aquí.
- **Limitaciones hasta la fase 2:** el modelo sigue teniendo un solo foco. Si la del foco se va a otro escritorio y se trae otra al foco, la primera vuelve a su panel en el modelo, y al regresar a su escritorio se coloca ahí. El atajo «volver a la anterior» puede elegir una de otro escritorio y dejar el foco vacío.

## Consecuencias

- **A favor:** cambiar de actividad es instantáneo y reversible, sin reacomodar ventanas; usa gestos que Windows ya enseña, y desde el deck basta un toque en la pestaña del escritorio; el deck nunca muestra apps de otro escritorio como si estuvieran aquí; se elimina el riesgo de duplicar apps.
- **En contra:** el modelo crece (un foco por escritorio) y la reconciliación consulta el escritorio de cada ventana gestionada. Los nombres y el orden de los escritorios dependen del registro no documentado, aunque sólo como adorno.
- **Límite de Windows:** cambiar de escritorio cambia todos los monitores. Una app que deba verse siempre en el monitor auxiliar (chat, monitoreo) tiene que anclarse a todos los escritorios desde la Vista de tareas; FocusWShell no puede hacerlo por el usuario sin API no documentada.
- **Las escenas pierden su papel** en el deck; su retiro total queda pendiente de la validación de uso ([06 — Validación](../06-validacion.md)).
