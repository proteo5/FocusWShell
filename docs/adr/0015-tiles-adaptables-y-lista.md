# ADR 0015 — Presentaciones: tiles adaptables y lista tipo Explorador

- **Estado:** Aceptada (2026-11-10); la opción B de la lista, confirmada por el spike S10
- **Fecha:** 2026-11-10

## Contexto

El [ADR 0011](0011-paneles-y-catalogo.md) prevé dos presentaciones nuevas para los paneles:

- **Tiles adaptables:** cada ventana con un tamaño de tile, colocadas solas según el ancho disponible (como un `flex-wrap`) y con "scroll" si no caben.
- **Lista tipo Explorador:** una vista dibujada por FocusWShell con las apps del panel, con modos como los del Explorador (iconos grandes con miniatura en vivo, lista, detalles).

Hoy "Tiles" pone una ventana por fila y, si no caben en alto, el panel pasa a pestañas. Antes de programar hay que decidir cuatro cosas, porque las ventanas de Windows tienen límites que una página web no tiene:

1. **No hay scroll de verdad.** Una ventana de otra app no se puede desplazar dentro de un panel. Las que "no caben" tienen que quitarse de la vista.
2. **Las ventanas tienen tamaño mínimo** (ya lo aprendemos, `MinSizeCatalog`). Un tile más pequeño que su app no sirve.
3. **Ocultamos minimizando** ([ADR 0006](0006-ocultar-con-minimizar.md)). Una ventana minimizada no tiene miniatura en vivo con DWM: `DwmRegisterThumbnail` muestra lo que la ventana pinta, y minimizada no pinta. Hay que comprobarlo (S10).
4. **La lista es una ventana propia** que ocupa el panel. Como las tiras, no debe robar el foco (`WS_EX_NOACTIVATE`).

## Propuesta

### 1. Tiles adaptables

**Rejilla del panel.** El panel se divide en celdas:

- **Columnas:** salen del ancho. Caben tantas como permita un ancho mínimo de celda, de partida 480 px lógicos. Un panel estrecho del principal tiene 1 columna; un auxiliar de 1920 px, 3 o 4.
- **Filas visibles:** son la **densidad** del panel: 1, 2, 3 o 4. La elige el usuario, y la de partida es 2.

**Tamaño de tile por ventana**, en celdas:

| Tamaño | Celdas (ancho × alto) | Uso típico |
| --- | --- | --- |
| Pequeño | 1 × 1 | Chat, monitor de estado |
| Ancho | 2 × 1 | Logs, terminal |
| Alto | 1 × 2 | Correo, lista de tareas |
| Grande | 2 × 2 | Navegador, dashboard |

- El tamaño se guarda en la ventana gestionada, en la escena y, como sugerencia, en el catálogo ("Grafana suele ir en Grande").
- Si un tamaño pide más columnas de las que hay, se recorta al ancho del panel.

**Colocación:** en el orden del panel, cada tile ocupa el primer hueco donde quepa (como `grid-auto-flow: dense`). Así un Pequeño puede rellenar el hueco que dejó un Grande.

**Mínimos:** si la app no cabe en las celdas de su tamaño, el tile crece al siguiente tamaño que la contenga. Si ni el panel entero la contiene, pasa a la página siguiente, o fuera del layout si es la única, con una nota, como hoy.

**"Scroll" = páginas por filas.** Lo que no cabe en las filas visibles queda en filas de más abajo, y esas ventanas se ocultan como las pestañas de una pila.

- La tira del panel muestra en qué filas estás (por ejemplo "1–2 de 4") con ▲ ▼. La rueda del ratón sobre la tira, o un atajo, avanza una fila.
- Llevar al panel una ventana oculta (desde la tira, el deck o la barra de tareas) desplaza el panel hasta su fila. Ya existe el caso parecido `MakeActiveInPanel`.

**Compatibilidad:** con 1 columna, tamaño Pequeño y la densidad igual al número de ventanas, sale exactamente el "Tiles" de hoy. Las escenas y los paneles guardados no cambian de aspecto.

### 2. Lista tipo Explorador

**Qué es.** Una ventana de FocusWShell que ocupa el área del panel (debajo de su tira) y muestra sus apps en tres modos:

| Modo | Qué muestra |
| --- | --- |
| **Iconos grandes** | Miniatura en vivo de cada ventana (DWM), con su icono y nombre |
| **Lista** | Icono y nombre, en varias columnas |
| **Detalles** | Icono, nombre, título de la ventana, estado (en el panel, en el foco, estacionada), monitor |

**Qué muestra además:** las apps **cerradas** del catálogo cuyo último panel es éste, apagadas. Tocarlas las abre en el panel, como el botón "Abrir" del catálogo. Así el panel funciona como un lanzador de su actividad.

**Elegir una app:**

- **Clic: la muestra en el panel.** La lista pasa a la tira, como una pestaña más ("◧ Lista"), y la app ocupa el panel. Volver a "Lista" en la tira vuelve a la vista.
- **Doble clic, o el botón "Al foco": la lleva al foco.**

Así la lista se comporta como una pila con un índice visual, y no se pierde la regla de que el foco sólo cambia por un gesto explícito ([ADR 0003](0003-rol-independiente-del-foco.md)).

**Dónde están las ventanas mientras se ve la lista.** Es la decisión que depende de S10:

| Opción | Miniaturas | Si FocusWShell cae | Coste |
| --- | --- | --- | --- |
| **A. Minimizadas** (como hoy, [ADR 0006](0006-ocultar-con-minimizar.md)) | Sin vida: icono, o la última imagen capturada con `PrintWindow` al ocultarla | Quedan minimizadas: el usuario las recupera como siempre | Ninguno nuevo |
| **B. Detrás de la lista**, en su rectángulo, sin minimizar | En vivo | La lista desaparece con el proceso y las ventanas quedan visibles en su sitio | Pintan aunque no se vean; pueden asomar en Alt+Tab en un orden raro |

Propongo **B** si S10 confirma que las miniaturas en vivo funcionan así y que no hay parpadeo al alternar. Si no, **A** con la última imagen. La opción B no contradice el ADR 0006: su objeción a "dejar detrás" era que asomaran; aquí las tapa una ventana nuestra del tamaño exacto del panel.

### 3. Orden de trabajo

1. **Tiles adaptables:** es sólo motor de layout y modelo, se prueba con pruebas unitarias y no depende de nada nuevo de Windows.
2. **Spike S10 — miniaturas:**
   - `DwmRegisterThumbnail` de una ventana minimizada frente a una que está detrás de otra;
   - coste de 10–20 miniaturas en vivo;
   - `PrintWindow` como alternativa, en Edge, Slack y VS Code.
3. **Lista tipo Explorador**, con la opción que salga de S10. Las miniaturas también le sirven al deck ([ADR 0013](0013-deck-superficie-de-control.md), S9).

## Decisiones del usuario (2026-11-10)

Cambian la propuesta en estos puntos; lo demás queda como está arriba.

1. **Tamaño de tile = columnas.** Pequeño, Mediano y Grande ocupan **1, 2 y 3 columnas**, y una fila de alto. Las columnas salen del ancho del panel; un tamaño que pide más columnas de las que hay se recorta. No hay Ancho ni Alto.
2. **Densidad automática.** Las filas no se eligen: se calculan con el alto disponible y los mínimos de las apps. Lo que no cabe pasa a filas de más abajo (▲ ▼).
3. **Clic en la lista: la lleva al foco** (`Promote`). No hay "mostrar en el panel".
4. **Las apps cerradas salen siempre en la lista**, apagadas; tocarlas las abre.
5. **Opción B** (ventanas detrás de la lista, miniaturas en vivo). **S10 la confirma:** las miniaturas de ventanas tapadas por una ventana propia siguen en vivo, también en Chromium, y 20 a la vez apenas cuestan. Las minimizadas se ven con su última imagen. Ver [04 — Riesgos y spikes](../04-riesgos-y-spikes.md#s10--miniaturas-2026-11-10).

Detalles de implementación que siguen de esto:

- **Al mostrar una ventana oculta por filas** (desde la tira o la barra de tareas), el panel se desplaza hasta su fila sin cambiar el orden de las ventanas. En las pestañas sigue como hoy: pasa a ser la primera.
- **"Tiles" pasa a ser la rejilla adaptable**, sin presentación nueva. En una columna del principal (1 columna) se ve como hoy, salvo que, si no caben en alto, ya no pasa a pestañas: pagina por filas.

## Implementación de los tiles adaptables (2026-11-10)

- **`TileGrid`** (motor): las columnas salen del ancho (480 px lógicos por columna, escalados al DPI del monitor), cada ventana ocupa 1, 2 o 3 columnas (`TileSize`), crece si su app no cabe, y va al primer hueco libre. Las filas se reparten el alto sin bajar del mínimo de sus ventanas ni de 160 px lógicos.
- **Filas que no caben:** sus ventanas se minimizan, como las pestañas ocultas. El resultado del motor trae `Pages` (primera fila, visibles, total) por panel, y una nota.
- **Modelo:** `ManagedWindow.Size` y `SetTileSize`; `ScrollPanel(panel, fila)`. `MakeActiveInPanel` en tiles no reordena: guarda la ventana a mostrar y el motor empieza por su fila. Mostrar desde la barra de tareas una ventana de una fila oculta hace lo mismo. El tamaño se guarda en las escenas.
- **Tiras:** ▲ «a–b / n» ▼ a la derecha, la rueda del ratón para recorrer las filas, y "Tamaño: Pequeño / Mediano / Grande" en el menú contextual de cada pestaña.
- **Cambio de comportamiento:** un panel en tiles que no cabe en alto ya no pasa a pestañas; se ven las filas que caben.

## Implementación de la lista (2026-11-10)

- **Motor:** la presentación `List` coloca cada ventana del panel en el área de la lista (bajo su tira), detrás de ella. La que no cabe se minimiza, para que no asome fuera de la lista. La lista no ensancha su columna. El resultado trae `Lists` (el área de cada panel en lista).
- **Modelo:** `Panel.ListLayout` (Iconos grandes, Lista, Detalles) y `SetListLayout`, que no mueve ventanas. Se guarda en `panels.json`.
- **Ventana de la lista** (`ListPanelWindow`, una por panel en lista, gestionada por `ListManager`): sin foco y fuera de la barra de tareas, como las tiras. Tiene los tres modos en botones arriba a la derecha.
  - En Iconos grandes, cada tarjeta lleva la miniatura en vivo de su ventana (`DwmThumbnail`), encajada sin deformarla. Se oculta la que sale de la zona visible del scroll.
  - Las apps cerradas (en el catálogo, con este panel como último) salen con su icono apagado. Tocar una abierta la lleva al foco; tocar una cerrada la abre en este panel.
- **Se oculta** en el monitor de la pantalla completa. Se actualiza con cada layout y cada cambio del catálogo. Una ventana cerrada pasa a "cerrada" enseguida.
- **Lanzador:** un panel en lista ocupa su sitio aunque no tenga ventanas abiertas, y muestra sus apps cerradas (o un texto que explica cómo llenarla). El layout también se aplica sin ventanas gestionadas si hay alguna lista, y al arrancar.
- **Ventana activada detrás de la lista:** al activarse (con los gestos desactivados, o si se activa sola) se pone encima y se deja ver mientras tenga el primer plano. Cuando el primer plano pasa a otra ventana, la lista vuelve a quedar encima, sin activarse.

## Consecuencias

- "Tiles" pasa a ser la rejilla adaptable y `PanelTemplate` gana `List`.
- `ManagedWindow`, `SceneWindow` y el catálogo guardan un tamaño de tile.
- El motor de layout gana un empaquetador de rejilla y un desplazamiento por filas por panel. Las tiras ganan ▲ ▼, y la "pestaña" Lista.
- Aparece un tercer tipo de ventana propia, después de las tiras y la ventana principal, con los mismos cuidados: no robar el foco, DPI por monitor y ocultarse en pantalla completa.
