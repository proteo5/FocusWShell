# 02 — Modelo de dominio

Este documento es el contrato del núcleo (`FocusWShell.Core`). El núcleo no conoce Win32: trabaja con identificadores de ventana y rectángulos, y produce *planes de layout* que otra capa ejecuta. Así se puede probar entero con pruebas unitarias.

Los nombres entre paréntesis son los del código. Ver también el [glosario](glosario.md).

## Separar "hogar" de "ubicación"

El handoff mezcla en una sola tabla roles (Trabajo, Contexto) y estados (Estacionada, Pantalla completa). En el código conviene separarlos en dos ejes, porque una ventana estacionada **sigue perteneciendo** a su panel y una ventana en el Centro **sabe a dónde volver**:

| Eje | Valores | Cambia cuando… |
| --- | --- | --- |
| **Hogar** (`Home`) | `Work`, `Context` | El usuario reasigna la ventana ("Enviar a contexto", "Enviar a trabajo"). Es persistente. |
| **Ubicación** (`Placement`) | `InPanel`, `Center`, `Parked`, `Fullscreen` | El usuario promueve, estaciona, restaura, devuelve o entra/sale de pantalla completa. |

El "rol" que ve el usuario se deriva de los dos: si la ubicación es `Center` muestra "Centro"; si es `InPanel` muestra el hogar; si es `Parked` muestra "Estacionada (Trabajo)".

## Entidades

```text
MonitorSetup ──< Display
     │
     └─ (huella) ──> SceneLayout ──< Region
                          ▲            ├─ CenterRegion
Scene ────────────────────┘            └─ PanelRegion ── PanelTemplate
  │
  └──< WindowAssignment ──> ManagedWindow ──> WindowIdentity
                                     └──> OriginalState
```

### Display y MonitorSetup

- **Display:** un monitor físico. Tiene identificador estable (ruta del dispositivo o EDID, nunca `\\.\DISPLAY1` ni `HMONITOR`), rectángulo del monitor, área de trabajo (sin barra de tareas), DPI y, opcionalmente, tamaño físico.
- **MonitorSetup:** el conjunto de displays conectados. Su **huella** (`Fingerprint`) se calcula con los identificadores, resoluciones, escalas y disposición. Sirve para elegir qué layout de la escena aplicar cuando cambia la configuración de monitores.

### Scene y SceneLayout

- **Scene:** una actividad con nombre (Código, Investigación, Documentos, Llamada, Juego). Contiene las asignaciones de ventanas y un `SceneLayout` por cada huella de monitores conocida.
- **SceneLayout:** la geometría de la escena para una huella concreta: una `CenterRegion` y uno o más `PanelRegion`. Las regiones se expresan en **fracciones del área de trabajo** de su display, no en píxeles, para sobrevivir a cambios de resolución.

Si no hay layout para la huella actual, se deriva uno por defecto (ver [Cambios de monitores](#cambios-de-monitores)).

#### Implementación (M1)

- **Qué guarda `SavedScene`:** por cada ventana, la app (`AppKey`: el AUMID, o el ejecutable), una pista de título, su hogar, si estaba en el Centro, en un panel o estacionada, y el monitor. Guarda también los templates de cada panel. Aún no guarda geometría propia: los pesos de las columnas son los de siempre.
- **Monitores:** los auxiliares se guardan por orden de izquierda a derecha (`AuxIndex` 1, 2…), no por `\\.\DISPLAYn`. Si al aplicar falta ese auxiliar, sus ventanas van a los paneles del principal y se avisa. Los "layouts por huella" quedan para cuando haya geometría por escena.
- **Emparejado (`SceneMatcher`):** cada ventana de la escena se empareja con una abierta de la misma app. Si hay varias, se prefiere:
  1. el mismo título;
  2. un título que contenga la pista, o al revés;
  3. la primera en orden Z.

  Las de título exacto se emparejan primero, para que una pista vaga no se lleve la ventana de otra. Las que no están abiertas se informan; todavía no se lanzan.
- **`ApplyScene`** monta el modelo de una vez:
  - las ventanas de la escena, en su orden, con sus templates;
  - captura en el diario las nuevas;
  - devuelve a su sitio original las que no son de la escena;
  - la ventana anterior del Centro queda como "anterior" para `SwapWithPrevious`.

### Monitor principal y monitores auxiliares

Ver [ADR 0008](adr/0008-centro-solo-en-monitor-principal.md).

- El **monitor principal** (por defecto, el principal de Windows) es el único que tiene `CenterRegion`. Aloja Contexto | Centro | Trabajo.
- Un **monitor auxiliar** (cualquier otro, incluidas las tablets que extienden el escritorio, [ADR 0009](adr/0009-tablets-como-monitores-auxiliares.md)) sólo tiene `PanelRegion`. Cada panel auxiliar tiene un hogar (`Work` o `Context`).
- `Promote` siempre lleva la ventana al Centro del principal. Al volver, regresa al panel del que salió, aunque esté en un auxiliar.
- Si un auxiliar desaparece, sus ventanas pasan a los paneles de su mismo hogar en el principal (en pila si no caben), nunca al Centro. Siguen siendo de ese monitor: cuando vuelve (`DisplayAdded`), regresan a él con sus templates. Los monitores se reconocen por su ruta de dispositivo, estable entre reconexiones (S6).

### Region, PanelRegion y PanelTemplate

- **CenterRegion:** una por escena. Aloja como máximo una ventana.
- **PanelRegion:** tiene un tipo (`Work` o `Context`), un display, un rectángulo fraccional y un `PanelTemplate`. Una escena del MVP tiene exactamente un panel de cada tipo; el modelo admite varios.
- **PanelTemplate:** decide cómo se presentan las ventanas del panel. No cambia su hogar.

| Template | Parámetros | Presentación |
| --- | --- | --- |
| `Tiles` | orientación, proporciones de los divisores | Todas las ventanas `InPanel` visibles a la vez, una por tile. Si sobran ventanas, las de más se apilan en el último tile. |
| `Stack` | ventana activa | Una ventana visible; las demás minimizadas y accesibles desde la tira de pestañas del gestor. |
| `Fill` | ventana activa | Sólo en monitores auxiliares: una ventana maximizada ocupa el monitor entero (monitoreo, depuración). Las demás, como en `Stack`. Maximizar con el botón de Windows en un auxiliar equivale a elegir `Fill`. |
| `Collapsible` *(post-MVP)* | ventana expandida | Una expandida; el resto como encabezados de 1 línea. |
| `Mixed` *(post-MVP)* | composición de los anteriores | Por ejemplo, un tile fijo arriba y una pila debajo. |

Cambiar de template sólo recalcula rectángulos y visibilidad. Ninguna ventana cambia de hogar ni de ubicación.

### ManagedWindow, WindowIdentity y OriginalState

- **ManagedWindow:** una ventana top-level externa que el gestor administra. Tiene `Home`, `Placement` y un orden dentro de su panel.
- **WindowIdentity:** cómo se reconoce una ventana. En vivo basta con el handle, pero para sobrevivir a un reinicio del gestor se guarda: `hwnd`, id y hora de inicio del proceso, ruta del ejecutable, clase de ventana, AUMID (para apps empaquetadas/UWP) y título. Ver [03 — Arquitectura](03-arquitectura.md#identidad-de-ventanas).
- **OriginalState:** foto de la ventana antes de que el gestor la toque: `WINDOWPLACEMENT` (rectángulo normal, maximizada/minimizada), display, DPI y visibilidad. Se captura **una vez**, en el primer contacto, y se escribe en el diario de recuperación antes de mover nada ([ADR 0005](adr/0005-diario-de-recuperacion.md)).

## Ubicaciones y transiciones

```text
                 Restaurar                Promover
   ┌─────────┐ ───────────▶ ┌─────────┐ ───────────▶ ┌────────┐
   │ Parked  │              │ InPanel │              │ Center │
   └─────────┘ ◀─────────── └─────────┘ ◀─────────── └────────┘
       ▲         Estacionar               Devolver      │   ▲
       │                                                │   │ Salir de
       └──────────────── Estacionar ────────────────────┘   │ pantalla completa
                                                 Pantalla   │
                                                 completa ▼ │
                                                     ┌────────────┐
                                                     │ Fullscreen │
                                                     └────────────┘
```

| Acción (`Command`) | Precondición | Efecto |
| --- | --- | --- |
| `Promote(w)` | `w` está `InPanel` o `Parked` | Si hay ventana `c` en el Centro, `c` pasa a `InPanel` en su hogar. `w` pasa a `Center`. `w` recibe el foco. |
| `ReturnToPanel(w)` | `w` está en `Center` | `w` pasa a `InPanel` en su hogar. El Centro queda vacío. |
| `SwapWithPrevious()` | Hay historial de Centro | Promueve la ventana que ocupó el Centro antes. Es el "volver" de un solo atajo (CA2). |
| `Park(w)` | `w` está en `InPanel` o `Center` | `w` pasa a `Parked`; aparece en la tira de su panel como estacionada. |
| `Restore(w)` | `w` está `Parked` | `w` pasa a `InPanel` en su hogar (CA4). |
| `SendTo(w, home)` | cualquiera salvo `Fullscreen` | Cambia el hogar. Si estaba `InPanel`, se mueve al otro panel. |
| `SetTemplate(panel, t)` | — | Recalcula el panel. No cambia hogares ni ubicaciones (CA3). |
| `EnterFullscreen(w, display)` | `w` está en `Center` | Guarda una instantánea de la escena; `w` llena `display`; los overlays del gestor en ese display se ocultan. |
| `ExitFullscreen()` | hay ventana en `Fullscreen` | Aplica la instantánea; `w` vuelve a `Center` (CA5). |
| `Adopt(w, home)` | `w` no está gestionada y es elegible | Captura `OriginalState`, la escribe en el diario y la pone `InPanel`. |
| `Release(w)` | `w` está gestionada | Aplica su `OriginalState` y la olvida. |
| `RestoreAll()` | — | `Release` de todas las ventanas. Es el botón de pánico (CA6). |

### Implementación en M0

`FocusWShell.Core.Domain.Workspace` implementa `Adopt`, `Promote`, `ReturnToPanel`, `SwapWithPrevious`, `SendTo`, `Release`, `RestoreAll` y el evento `WindowClosed`. `Park`, `Restore`, `SetTemplate` y la pantalla completa llegan en M1 y M2.

- El modelo es puro. Cada comando devuelve un `CommandOutcome`: aceptado con una lista de **efectos** (`CaptureOriginal`, `RestoreOriginal`, `RestoreEverything`, `ApplyLayout`, `Activate`), o rechazado con un motivo legible. La app ejecuta los efectos en orden: capturas y restauraciones, un solo layout y, al final, el foco.
- El orden de las ventanas dentro de un panel es su orden en el modelo. Una ventana en el Centro conserva su puesto, así que al volver a su panel lo recupera.
- El historial del Centro guarda las ventanas que lo ocuparon, de la más reciente a la más antigua. `SwapWithPrevious` promueve la primera que siga gestionada; repetirlo alterna entre las dos últimas (CA2).
- En M0 el cierre de una ventana se detecta al refrescar la lista (aún no hay hooks; S4 en M1).

### Implementación de los monitores auxiliares (M1)

- `ManagedWindow.Display` guarda el monitor auxiliar del panel de la ventana (`null` es el principal). Por ahora la clave es el nombre GDI (`\\.\DISPLAYn`); la identidad estable llega con S6.
- Comandos nuevos:
  - `SendToDisplay` mueve el panel de una ventana a otro monitor sin cambiar su hogar.
  - `SetTemplate` cambia el template de un panel. `Fill` se rechaza en el principal.
  - `DisplayRemoved` funde las ventanas de un auxiliar desconectado en los paneles del principal, nunca en el Centro.
- `SceneComposition.Aux` lista, por monitor auxiliar, sus paneles de Contexto y Trabajo con su template.
- `AuxLayoutEngine` coloca los paneles de un auxiliar:
  - Los paneles vacíos no ocupan sitio, y el resto se reparte por peso.
  - Un panel que no llega al mínimo de su ventana toma ancho de los otros.
  - **Nada se desborda**: la ventana que no cabe se minimiza con una nota.
  - Con `Fill`, la primera ventana del panel se maximiza y todo lo demás de ese monitor se minimiza. Si dos paneles están en `Fill`, gana el primero.

## Hacia grupos, vistas y deck (propuesta)

Tres decisiones propuestas cambiarán este modelo en la siguiente etapa:

- **Grupos de apps ([ADR 0011](adr/0011-grupos-de-apps-y-catalogo.md)).** El hogar de una ventana deja de ser `Work` o `Context` y pasa a ser un grupo libre con un tipo: lateral, monitor, compacto o deck. Los grupos se llenan desde un catálogo de apps que aprende con el uso, y las apps cerradas se pueden abrir desde él.
- **Vistas ([ADR 0012](adr/0012-vistas-y-areas-de-foco.md)).** El Centro pasa a ser una o dos **áreas de foco**, según la vista (Foco 1 o Foco 2). El invariante 1 cambia a "como máximo una ventana por área de foco". Pantalla completa significa maximizada en el monitor elegido.
- **Deck ([ADR 0013](adr/0013-deck-superficie-de-control.md)).** Una vista y un control táctil sobre todo lo anterior, opcional, en una esquina del principal o en otro monitor.

## Invariantes

El núcleo debe garantizarlos después de cada comando; las pruebas unitarias los verifican:

1. Como máximo una ventana con `Placement = Center` por escena, y como máximo una `Fullscreen` por display.
2. Toda ventana gestionada tiene un `Home` y una entrada en el diario con su `OriginalState`.
3. Un comando nunca cambia el `Home` de una ventana distinta de la que nombra.
4. Ningún evento externo (foco, notificación, cambio de título) cambia `Home` ni `Placement`. Sólo los comandos los cambian (CA7).
5. Cambiar el template de un panel no cambia ningún `Home` ni `Placement`.
6. `RestoreAll` deja el conjunto de ventanas gestionadas vacío y el diario marcado como limpio.

## Eventos externos y cómo los interpreta el núcleo

`Workspace.Interpret` traduce cada cambio externo detectado (S4, [ADR 0007](adr/0007-cambios-externos-por-reconciliacion.md)) en comandos. Nunca genera `Promote` ni `SwapWithPrevious` (CA7, verificado en las pruebas). En la app ya está activo:

| Cambio detectado | Comandos |
| --- | --- |
| Se cerró | `WindowClosed` |
| La minimizaste | `Park`; si ya estaba estacionada, nada |
| La abriste desde la barra de tareas | Si estaba estacionada, `Restore`; si la escondía una pila o un `Fill`, `MakeActiveInPanel` (pasa a ser la visible) |
| La maximizaste en un auxiliar | `MakeActiveInPanel` + `SetTemplate(…, Fill)` |
| La maximizaste en el principal | `Detach` (fuera de layout) |
| La des-maximizaste estando su panel en `Fill` | `SetTemplate(…, Tiles)` |
| La arrastraste o redimensionaste | `Detach`; los demás de su panel ocupan su sitio |

Una ventana fuera de layout (`Detached`) sigue gestionada (está en el diario), pero el gestor no la mueve hasta que se **reencaje** (`Reattach`), se promueva o se estacione.

| Evento | Interpretación |
| --- | --- |
| La ventana se cierra | Se elimina del modelo. Si estaba en el Centro, el Centro queda vacío. **No** se promueve otra automáticamente. |
| El usuario la minimiza con el botón de Windows | Se interpreta como `Park`. |
| El usuario la restaura desde la barra de tareas | Si estaba `Parked`, se interpreta como `Restore`. |
| El usuario la maximiza con el botón de Windows | Se respeta: la ventana pasa a estar "fuera de layout" hasta que la desmaximice o el usuario aplique la escena otra vez. El gestor no pelea con el usuario. |
| El usuario la arrastra o redimensiona | Igual que maximizar: queda fuera de layout. Un comando "reencajar" la devuelve a su rectángulo. |
| Recibe foco o notificación | Nada. Como mucho, se actualiza un indicador en la microbarra. |
| Aparece una ventana nueva elegible | En el MVP no se adopta sola; se ofrece en la lista "sin gestionar". |

## Cambios de monitores

Cuando cambia la huella de monitores:

1. Si la escena tiene un `SceneLayout` para la nueva huella, se aplica.
2. Si no, se deriva uno:
   - Si falta un auxiliar, sus paneles se funden con los del mismo hogar en el principal.
   - Si falta el principal, el auxiliar más grande pasa a principal y recibe el Centro.

   El layout derivado se marca como "provisional" y no se guarda hasta que el usuario lo ajuste o lo confirme.
3. Las ventanas nunca quedan fuera de todas las áreas visibles.

Al volver la huella original, se aplica de nuevo su layout guardado.

## Tamaños mínimos

Muchas apps imponen un tamaño mínimo y lo aplican aunque el gestor pida otro. En S2 y S5, Slack exigió 670 px de ancho, GitHub Desktop 962×661 y Docker Desktop 942 px de ancho. Windows no ofrece una forma de consultar ese mínimo desde otro proceso, así que el gestor lo **aprende**.

### Aprendizaje (`MinSizeCatalog`)

- Tras cada colocación se compara lo pedido con lo obtenido. Si una dimensión quedó más de 1 px por encima, la medida obtenida se guarda como mínimo de esa app. Si ya había un mínimo, se queda el mayor de los dos.
- La clave es el AUMID de la app si lo tiene y, si no, el nombre del ejecutable.
- Se guarda en píxeles lógicos (96 DPI), redondeando hacia arriba, para poder usarlo en monitores con otra escala.
- Si en una colocación se aprende algo nuevo, se recalcula el layout y se vuelve a colocar en el mismo momento. Así la primera vez puede haber un instante de desborde, pero a partir de la siguiente el layout ya sale bien a la primera.

### Reglas del motor (`SceneLayoutEngine`)

En este orden:

1. **Template efectivo.** Un panel en tiles cuyas ventanas no caben en altura (suma de mínimos + separaciones) se muestra como pila. Es temporal y se explica; no cambia el template guardado. En modo pila sólo cuenta el mínimo de la ventana visible.
2. **Centro con mínimo.** Si la ventana del Centro no cabe, el Centro crece primero a costa de Contexto y luego de Trabajo. Ningún panel baja del mínimo de su ventana más ancha.
3. **Paneles con mínimo.** Un panel que no cabe crece a costa del Centro, pero el Centro no baja de su **piso**: el mayor entre el mínimo de su ventana y la mitad del ancho de trabajo (`MinCenterFraction`, 0,5 por defecto, o el ancho pedido si es menor).
4. **Desborde hacia fuera.** Si aun así una ventana de panel no cabe, se ancla al borde exterior del monitor y el exceso queda fuera de pantalla. **Nunca encima del Centro.**
5. **Filas.** Dentro de un panel en tiles, cada fila recibe al menos su mínimo y el resto de la altura se reparte por igual.
6. Si la ventana del Centro sigue sin caber después del paso 2, se centra sobre su columna. Es el único caso en que una ventana invade un panel, y nunca al revés.

Cada ajuste genera una nota en lenguaje natural, por ejemplo: "Contexto se amplió 54 px (el Centro queda en 1830 px) porque Slack necesita 670 px de ancho". Esa nota alimentará la microbarra y cumple el principio de que toda acción automática se explique.

### Bordes compartidos con otro monitor

Con varios monitores, "fuera de pantalla" puede caer en el monitor vecino. Por eso, cuando el borde exterior de un panel limita con otro monitor (`PanelInput.OuterEdgeShared`, calculado con `DisplayTopology.SharedEdges`), el paso 4 cambia:

- El Centro puede ceder más que la mitad: baja hasta el mínimo de su ventana, pero nunca por debajo del 30 % del ancho de trabajo (`SharedEdgeMinCenterFraction`), por si ese mínimo no se conoce.
- La ventana que ni así cabe **sale del layout**: se minimiza, con una nota que lo explica, y el resto del panel ocupa su sitio. Nunca invade el otro monitor ni el Centro.
- Si los dos paneles tienen el borde compartido, el de la izquierda (Contexto) reserva antes lo que necesita el de la derecha (Trabajo).

El borde exterior que no limita con nada mantiene la regla de desborde del paso 4.
