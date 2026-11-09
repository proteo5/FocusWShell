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

### Region, PanelRegion y PanelTemplate

- **CenterRegion:** una por escena. Aloja como máximo una ventana.
- **PanelRegion:** tiene un tipo (`Work` o `Context`), un display, un rectángulo fraccional y un `PanelTemplate`. Una escena del MVP tiene exactamente un panel de cada tipo; el modelo admite varios.
- **PanelTemplate:** decide cómo se presentan las ventanas del panel. No cambia su hogar.

| Template | Parámetros | Presentación |
| --- | --- | --- |
| `Tiles` | orientación, proporciones de los divisores | Todas las ventanas `InPanel` visibles a la vez, una por tile. Si sobran ventanas, las de más se apilan en el último tile. |
| `Stack` | ventana activa | Una ventana visible; las demás minimizadas y accesibles desde la tira de pestañas del gestor. |
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

## Invariantes

El núcleo debe garantizarlos después de cada comando; las pruebas unitarias los verifican:

1. Como máximo una ventana con `Placement = Center` por escena, y como máximo una `Fullscreen` por display.
2. Toda ventana gestionada tiene un `Home` y una entrada en el diario con su `OriginalState`.
3. Un comando nunca cambia el `Home` de una ventana distinta de la que nombra.
4. Ningún evento externo (foco, notificación, cambio de título) cambia `Home` ni `Placement`. Sólo los comandos los cambian (CA7).
5. Cambiar el template de un panel no cambia ningún `Home` ni `Placement`.
6. `RestoreAll` deja el conjunto de ventanas gestionadas vacío y el diario marcado como limpio.

## Eventos externos y cómo los interpreta el núcleo

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
2. Si no, se deriva uno: si falta el monitor estándar, el panel de Contexto se mueve al extremo izquierdo del ultrawide; si falta el ultrawide, el Centro y el Trabajo se reparten el monitor que queda; y así sucesivamente. El layout derivado se marca como "provisional" y no se guarda hasta que el usuario lo ajuste o lo confirme.
3. Las ventanas nunca quedan fuera de todas las áreas visibles.

Al volver la huella original, se aplica de nuevo su layout guardado.
