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
- Overlay de panel con tira de pestañas y lista de estacionadas.
- Comandos `Park`, `Restore`, `SendTo`, `SetTemplate`, `ReturnToPanel`.
- Detección de eventos del usuario (minimizar, maximizar, arrastrar) y sus interpretaciones (S4).
- Escenas con nombre, guardar y aplicar; layouts por huella de monitores.
- Paneles en monitores auxiliares, con template `Fill` ([ADR 0008](adr/0008-centro-solo-en-monitor-principal.md)).
- Mantener la pantalla encendida ([ADR 0010](adr/0010-mantener-pantalla-encendida.md)). **Hecho.**
- Atajos configurables.

Criterio de salida: CA3, CA4 y CA7. Spike S7 resuelto.

## M2 — Pantalla completa y robustez

Objetivo: que el gestor aguante el uso real: monitores que van y vienen, suspensión, apps difíciles.

- `EnterFullscreen` / `ExitFullscreen` por monitor.
- Detección de pantalla completa propia de las apps (juegos, presentaciones) y pausa de ese monitor.
- Cambios de monitores con *debounce* y layouts derivados (S6), incluidas tablets por wifi que van y vienen (R11, [ADR 0009](adr/0009-tablets-como-monitores-auxiliares.md)).
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

## Más adelante (sin compromiso)

- Proceso guardián independiente para la recuperación.
- Adaptadores sin bordes o con reparenting para apps concretas y probadas ([ADR 0001](adr/0001-orquestar-ventanas-top-level.md)).
- Shell propio en una cuenta de Windows dedicada ([ADR 0002](adr/0002-no-reemplazar-shell.md)).
- Calibración física de monitores (tamaño, distancia).
