# 05 — Hoja de ruta

Los hitos son incrementales: cada uno se usa a diario antes de pasar al siguiente. Las referencias **CA1–CA7** son los criterios de aceptación de [01 — Visión y alcance](01-vision.md#cómo-sabremos-si-funciona).

## M0 — Núcleo de orquestación (spike)

Objetivo: demostrar que se pueden mover ventanas reales de forma precisa y siempre recuperable.

- Solución .NET con `Core`, `Win32`, `App` y `Core.Tests` ([ADR 0004](adr/0004-stack-dotnet-wpf-cswin32.md)).
- Enumeración y filtro de elegibilidad (S1).
- Aplicador con compensación de bordes y DPI (S2), con movimiento paralelo asíncrono (S3).
- Diario de recuperación y restauración al arrancar (S5).
- Icono de bandeja con: lista de ventanas, asignar a Centro/Trabajo/Contexto, aplicar layout fijo, restaurar todas.
- Atajo global de emergencia.
- Comandos `Adopt`, `Promote`, `SwapWithPrevious`, `Release`, `RestoreAll`.

Criterio de salida: CA1, CA2 y CA6 con layout fijo (sin escenas guardadas ni templates). Spikes S1–S5 documentados.

**Estado (2026-11-09):** completo en el ultrawide. S4 pasa a M1, como estaba previsto. La app de bandeja asigna roles y ejecuta los comandos (`Adopt`, `Promote`, `SwapWithPrevious`, `ReturnToPanel`, `SendTo`, `Release`, `RestoreAll`). Atajos con `Ctrl+Alt+Mayús`: ↑ promover la ventana activa, ← volver a la anterior, ↓ devolver el Centro a su panel, Retroceso restaurar todas. Al arrancar tras una caída, ofrece restaurar. Con el segundo monitor (estándar al 125 %): colocación exacta en cualquiera de los dos, restauración entre DPI distintos y sin desbordes sobre el monitor vecino. Una escena que reparta los paneles entre ambos monitores llega con los layouts por huella (M1).

## M1 — Paneles, templates y escenas

Objetivo: el modelo completo de roles con las dos presentaciones básicas.

- Templates `Tiles` (2 y 4) y `Stack`, con divisores ajustables.
- Overlay de panel con tira de pestañas y lista de estacionadas. **Hecho** (primera versión, sin miniaturas ni arrastrar entre paneles).
- Comandos `Park`, `Restore`, `SendTo`, `SetTemplate`, `ReturnToPanel`. **Hecho**, más `Detach`/`Reattach` (fuera de layout) y `MakeActiveInPanel`.
- Detección de eventos del usuario (minimizar, maximizar, arrastrar) y sus interpretaciones (S4). **Hecho** en la app.
- Escenas con nombre, guardar y aplicar; layouts por huella de monitores. **Hecho** sin geometría propia por escena: guardar, aplicar, borrar, atajos `Ctrl+Alt+Mayús+1…9` y submenú en la bandeja.
- Paneles en monitores auxiliares, con template `Fill` ([ADR 0008](adr/0008-centro-solo-en-monitor-principal.md)). **Hecho** (sin S6).
- Mantener la pantalla encendida ([ADR 0010](adr/0010-mantener-pantalla-encendida.md)). **Hecho.**
- Atajos configurables.

Criterio de salida: CA3, CA4 y CA7. Spike S7 resuelto.

## M1b — Paneles, vistas y deck

Objetivo: manejar el escritorio por paneles propios y vistas, con un deck opcional. Ver [ADR 0011](adr/0011-paneles-y-catalogo.md), [0012](adr/0012-vistas-posicion-del-foco.md) y [0013](adr/0013-deck-superficie-de-control.md). Se hace en este orden:

1. **Sin deck:**
   - Paneles como entidades (crear, renombrar, mover, borrar), hasta dos por columna; Contexto y Trabajo pasan a ser los paneles por defecto. Ventanas libres por defecto. **Hecho**, con dos paneles por columna (arriba y abajo) en el principal y en los auxiliares; de momento se reparten el alto a partes iguales.
   - Catálogo de apps por detección, y abrir apps cerradas en un panel. **Hecho.**
   - Presentaciones nuevas, empezando por tiles adaptables (tamaños de tile y reparto automático) y la lista tipo Explorador.
   - Abrir apps cerradas.
   - Vistas con el foco a la izquierda, al centro o a la derecha. **Hecho**, con presets y separador arrastrado; las proporciones se guardan por vista. Falta el separador entre dos paneles apilados.
   - Pantalla completa (maximizada) en el monitor elegido.

   Todo se usa desde la ventana, los atajos y las tiras.
2. **Deck en una esquina del monitor principal**, que se muestra y se oculta con un clic o un atajo, y se oculta solo al elegir.
3. **Spike S8:** que el toque desde una tablet (Apollo/Artemis) y desde un monitor táctil físico no robe el foco (R12).
4. **"Mandar el deck a…"** otro monitor, con paneles de pantalla completa con pestañas para tablets.

Criterio de salida: una semana de uso diario cambiando de vista y llevando apps al foco desde el deck, sin tocar los atajos.

## M2 — Pantalla completa y robustez

Objetivo: que el gestor aguante el uso real: monitores que van y vienen, suspensión, apps difíciles.

- `EnterFullscreen` / `ExitFullscreen` por monitor.
- Detección de pantalla completa propia de las apps (juegos, presentaciones) y pausa de ese monitor.
- Cambios de monitores con *debounce* y layouts derivados (S6), incluidas tablets por wifi que van y vienen (R11, [ADR 0009](adr/0009-tablets-como-monitores-auxiliares.md)). **Hecho** para monitores auxiliares que se desconectan y vuelven; falta probar una tablet, la pérdida del monitor principal y la suspensión.
- Reanudación tras suspensión.
- Detección y presentación de ventanas no gestionables (elevadas).
- Lista de exclusión editable.

Criterio de salida: CA5 y las siete CA juntas en una semana de uso diario sin tener que usar el atajo de emergencia por un fallo.

## M3 — Capa visual

Objetivo: que el estado del escritorio se entienda de un vistazo. Sólo si la validación de M0–M2 es positiva.

- Microbarras contextuales por panel, con indicadores de cambio discretos.
- Miniaturas DWM en la tira de pestañas.
- Templates `Collapsible` y `Mixed`.
- Transiciones breves que expliquen de dónde viene y a dónde va una ventana.
- Paleta de comandos ("ir a…", "promover…").

## M4 — Reglas opt-in

- Sugerencias de asignación por app al aplicar una escena ("Outlook suele ir a Contexto, ¿aplicar?").
- Reglas automáticas con modo de prueba, explicación de cada acción y deshacer.
- ~~Propuesta: "Alt+Tab lleva al foco"~~. **Adelantado a M1b** y ampliado: Alt+Tab, la vista de tareas y la barra de tareas llevan al foco (activados); el clic en la ventana, desactivado. Soltar una ventana arrastrada sobre el foco o sobre otro panel la lleva allí; si no, vuelve a su sitio. Ver [ADR 0003, enmienda](adr/0003-rol-independiente-del-foco.md#enmienda-2026-11-10-los-gestos-de-cambio-de-ventana-cuentan-como-comando).

## Más adelante (sin compromiso)

- Proceso guardián independiente para la recuperación.
- Adaptadores sin bordes o con reparenting para apps concretas y probadas ([ADR 0001](adr/0001-orquestar-ventanas-top-level.md)).
- Shell propio en una cuenta de Windows dedicada ([ADR 0002](adr/0002-no-reemplazar-shell.md)).
- Calibración física de monitores (tamaño, distancia).
