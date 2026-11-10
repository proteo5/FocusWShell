# ADR 0011 — Grupos de apps libres y un catálogo que se llena por detección

- **Estado:** Propuesta
- **Fecha:** 2026-11-10

## Contexto

Hasta ahora una ventana pertenece a un panel por su hogar (`Work` o `Context`) y a un monitor. El deck ([ADR 0013](0013-deck-superficie-de-control.md)) y las escenas necesitan algo más:

- **Agrupar apps por cómo se usan**, por ejemplo "Dev", "Comunicación" o "Monitoreo", y no sólo por panel.
- **Mostrar también las apps que no están abiertas**, para abrirlas con un toque. Hasta ahora las escenas sólo informaban de las que faltaban.
- **No complicar el arranque:** escanear todas las apps instaladas del sistema (menú Inicio, AppX, registro) es lento, ruidoso y difícil de mantener.

## Decisión

1. **Catálogo de apps por detección suave.**
   - Cada vez que FocusWShell ve una ventana elegible de una app que no conocía, la añade al catálogo (`apps.json`) en el grupo **General**.
   - Por cada app guarda:
     - la clave (`AppKey`, el AUMID o el ejecutable, la misma que usan los tamaños mínimos y las escenas);
     - el nombre;
     - el icono;
     - **cómo abrirla**: la ruta del ejecutable o el AUMID para apps empaquetadas.
   - No guarda títulos de ventanas.
   - El usuario puede mover cada app a otro grupo, renombrarla, ocultarla del deck u olvidarla.
2. **Grupos libres con un tipo.** El usuario crea los grupos que quiera. Cada grupo tiene un **tipo** que dice dónde y cómo se presentan sus apps:

   | Tipo | Dónde | Presentación por defecto |
   | --- | --- | --- |
   | Lateral | A un lado del foco, en el monitor principal | Tiles o Pila, como los paneles de Contexto y Trabajo de hoy |
   | Monitor | Un monitor auxiliar | Tiles, Pila o Fill ([ADR 0008](0008-centro-solo-en-monitor-principal.md)) |
   | Compacto | Una pantalla pequeña (tablet o monitor táctil) | La pantalla entera con **pestañas**, una app visible a la vez; más adelante, paneles plegables (`Collapsible`) |
   | Deck | El propio deck | Sólo iconos para lanzar y controlar; no coloca ventanas |

3. **Los paneles actuales son grupos con nombre fijo.** Contexto y Trabajo pasan a ser los dos grupos laterales por defecto. El hogar (`Home`) de una ventana se generaliza a "su grupo". La transición no cambia el comportamiento actual.
4. **Tocar una app cerrada la abre.** Al aparecer su ventana, se adopta en su grupo. Lo mismo vale para las apps que faltan al aplicar una escena.

## Consecuencias

- El catálogo empieza vacío y aprende con el uso. No hace falta un asistente de configuración.
- `Home` (dos valores) se convierte en una referencia a un grupo. Es el cambio de modelo más grande de esta etapa: hay que migrar el diario, las escenas y la UI con cuidado.
- Abrir apps tiene sus casos difíciles: apps que ya estaban abiertas en otro escritorio, apps que tardan en mostrar su ventana o que muestran primero una pantalla de bienvenida. Se espera a la primera ventana elegible del proceso o del AUMID, con un plazo.
- **Privacidad:** el catálogo guarda nombres de apps y rutas de ejecutables, no contenido. Es local, en `%LOCALAPPDATA%`.
