# ADR 0002 — Convivir con Explorer; no reemplazar el shell en la primera etapa

- **Estado:** Aceptada
- **Fecha:** 2026-11-09

## Contexto

Una interfaz propia con microbarras por panel podría, a la larga, sustituir la barra de tareas global. Windows permite reemplazar `Explorer.exe` (Shell Launcher u otras vías), pero eso obliga a resolver lanzamiento de apps, archivos, notificaciones, recuperación en el arranque, compatibilidad con actualizaciones y otras responsabilidades ajenas al problema. Shell Launcher, además, sólo está soportado en ediciones Enterprise y Education.

## Decisión

- FocusWShell es una aplicación normal que corre junto a `Explorer.exe` y la barra de tareas. No las oculta ni las modifica.
- La cuenta principal y personal del usuario **nunca** se convertirá en un shell personalizado.
- Un posible shell propio se explorará sólo más adelante, en una cuenta de Windows adicional creada para eso, y sólo si el gestor demuestra utilidad real.

## Consecuencias

- Se puede cerrar, actualizar y depurar el gestor sin riesgo de quedarse sin escritorio.
- La barra de tareas sigue ocupando espacio vertical y duplica parcialmente las microbarras. Se acepta en esta etapa.
- El gestor tiene que convivir con lo que Explorer haga (Snap, Win+flechas, Alt+Tab) y no pelear con ello.
