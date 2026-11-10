# 01 — Visión y alcance

## El problema

Quien trabaja con un monitor ultrawide y un monitor estándar, y con muchas ventanas abiertas, choca con tres limitaciones de las herramientas actuales:

1. **Snap Layouts y FancyZones piensan en zonas, no en papeles.** Ofrecen rectángulos, pero no saben que una ventana es *la tarea* y otra es *el chat que hay que vigilar*.
2. **El ultrawide no tiene una buena configuración de trabajo.** Una ventana maximizada a 3440 px es incómoda de leer, y partirlo en tercios iguales deja el centro demasiado estrecho.
3. **Recuperar una ventana cuesta.** Hay que buscarla en la barra de tareas global, Alt+Tab o moviendo ventanas a mano. Lo mismo pasa cada vez que se reconecta un monitor o se cambia de actividad.

Los dos monitores no cumplen la misma función: el ultrawide es el escenario de trabajo y el estándar es la periferia.

## Principio rector

> El escritorio es una escena de atención, no una cuadrícula de rectángulos.

De ahí salen cuatro reglas que todo el diseño debe respetar:

- **El rol de una ventana es persistente y no depende del foco.** Hacer clic en el chat no lo lleva al centro. Ver [ADR 0003](adr/0003-rol-independiente-del-foco.md).
- **El centro es una región continua y protegida.** Ningún widget, barra ni notificación del gestor se superpone a la ventana central.
- **Nada se mueve sin una acción explícita.** Las notificaciones cambian indicadores; no reordenan el escritorio. Las automatizaciones futuras son opt-in, se pueden probar en seco, se explican y se pueden deshacer.
- **Siempre hay salida.** Un atajo global devuelve todas las ventanas a su geometría previa al gestor, y una caída del proceso no deja el escritorio roto.

## Para quién

Primero, para el autor: un uso personal diario con ultrawide + monitor estándar. El proyecto es open source desde el inicio, pero no se diseña para un usuario genérico hasta que el modelo demuestre valor en ese uso real.

## Fundamentos

Las reglas anteriores se apoyan en hipótesis de percepción y ergonomía. No son afirmaciones médicas; son puntos de partida que el uso real debe confirmar o descartar.

| Fundamento | Qué implica para el gestor |
| --- | --- |
| La visión foveal tiene alta agudeza y la periferia sufre con el amontonamiento (*crowding*). [Revisión](https://pmc.ncbi.nlm.nih.gov/articles/PMC10528557/), [crowding](https://pmc.ncbi.nlm.nih.gov/articles/PMC3070834/) | El centro es para leer y escribir; la periferia, para señales breves (icono, estado, contador), no para columnas densas de texto. |
| La búsqueda visual se guía por contraste, movimiento y ubicación. [Revisión](https://pmc.ncbi.nlm.nih.gov/articles/PMC9879335/) | Una gramática de alertas sobria: color fuerte y animación sólo para lo accionable. |
| La carga cognitiva sube cuando hay que recordar dónde quedó algo. [Encuesta HCI](https://doi.org/10.1145/3582272) | Cada app tiene un "hogar" estable; promover al centro nunca destruye el lugar de la anterior. |
| *Change blindness*: los cambios sin transición visible pasan desapercibidos. [Estudio](https://pubmed.ncbi.nlm.nih.gov/12930482/), [cine y mirada](https://arxiv.org/abs/2102.13378) | No reorganizar en segundo plano; los cambios de escena son nombrados, visibles y reversibles. |
| Las interfaces críticas separan alertas por urgencia. [NASA HIDH](https://www.nasa.gov/reference/10-0-crew-interfaces-vol-2/) | Una notificación no da permiso para robar el foco. |
| Ergonomía del puesto: distancia de 50–100 cm, borde superior a la altura de los ojos o más abajo. [OSHA](https://www.osha.gov/etools/computer-workstations/components/monitors/) | Las proporciones dependen del monitor físico; el contenido de lectura larga no va a los extremos del ultrawide. |

## Layout de partida

```text
Ultrawide
┌──────────────┬──────────────────────────────┬────────────────┐
│ CONTEXTO     │ CENTRO                       │ TRABAJO        │
│ 15–20 %      │ 50–60 %                      │ 25–30 %        │
└──────────────┴──────────────────────────────┴────────────────┘
Monitor estándar: CONTEXTO (correo, chat, calendario, llamadas)
```

Las proporciones son una hipótesis. Hay una variante igual de válida: llevar todo el contexto al monitor estándar y repartir el ultrawide sólo entre Centro y Trabajo. El modelo de dominio admite las dos sin cambios.

## Alcance del MVP

Entra:

1. Aplicación de escritorio normal que convive con `Explorer.exe` y la barra de tareas.
2. Detección de ventanas elegibles, con lista de inclusión y exclusión por aplicación.
3. Asignación manual de una ventana a Centro, Trabajo, Contexto o Estacionada (por atajo o menú).
4. Una escena con Centro, un panel de Trabajo y un panel de Contexto, repartidos entre los dos monitores.
5. Templates de panel: **tiles** y **pila**. Los paneles colapsables y los mixtos quedan para una segunda iteración.
6. Promover al centro, estacionar, restaurar y pantalla completa por monitor.
7. Guardar y aplicar escenas; restaurar la geometría original de todas las ventanas.
8. Atajo global de emergencia "restaurar todas".
9. Comportamiento seguro ante conexión o desconexión de monitores y ante una caída del gestor.

No entra (y por qué):

| Fuera de alcance | Motivo |
| --- | --- |
| Reemplazar el shell u ocultar la barra de tareas | Riesgo alto y responsabilidades ajenas al problema. Ver [ADR 0002](adr/0002-no-reemplazar-shell.md). |
| Incrustar apps ajenas con `SetParent` | Frágil con DPI, Electron, Office y navegadores. Ver [ADR 0001](adr/0001-orquestar-ventanas-top-level.md). |
| Capturar una ventana y reenviar el input | Termina siendo una imitación del escritorio, con latencia y problemas de foco. |
| Reglas automáticas por aplicación | Primero hay que validar el modelo manual; las reglas llegan después y siempre opt-in. |
| Eye tracking, IA, telemetría remota, perfiles médicos | No aportan a la validación del modelo y añaden complejidad o problemas de privacidad. |

## Cómo sabremos si funciona

Criterios de aceptación de la primera demo útil (se citan en el resto de la documentación como **CA1–CA7**):

| ID | Criterio |
| --- | --- |
| CA1 | Con ultrawide + segundo monitor, se colocan 3–4 apps conocidas en Centro, Trabajo y Contexto mediante atajos o menú. |
| CA2 | Una app de Trabajo pasa al Centro, y la anterior se recupera, en menos de dos acciones. |
| CA3 | El panel de Trabajo cambia entre dos tiles y pila sin perder apps ni sesiones. |
| CA4 | Una app estacionada se restaura a su panel original. |
| CA5 | La app del Centro va a pantalla completa en un monitor concreto y, al salir, la escena anterior vuelve intacta. |
| CA6 | Al cerrar el gestor o usar el atajo de emergencia, todas las ventanas vuelven a su posición y estado previos. |
| CA7 | Ninguna notificación ordinaria mueve por sí sola una app al Centro. |

Más allá de la demo, la pregunta de fondo es si el modelo de roles y escenas mejora de forma perceptible lo que ya se consigue con **FancyZones + DisplayFusion**. Si después de semanas de uso diario la respuesta es "no", el proyecto debe detenerse o cambiar de rumbo. El protocolo está en [06 — Validación de uso](06-validacion.md).

## Panorama de herramientas existentes

| Herramienta | Qué resuelve | Qué le falta para este problema |
| --- | --- | --- |
| [FancyZones](https://learn.microsoft.com/en-us/windows/powertoys/fancyzones) | Zonas personalizadas por monitor | No conoce roles ni pilas; no restaura escenas. |
| [DisplayFusion](https://www.displayfusion.com/features/windowpositionprofiles/) | Perfiles de posición ligados a perfiles de monitor | Restaura geometría, no modela atención. De pago. |
| [FancyWM](https://github.com/FancyWM/fancywm) | Tiling dinámico | Reacomoda el conjunto; no protege un centro estable. |
| [komorebi](https://komorebi.lgug2z.com/usage/rules/) | Tiling con reglas y workspaces | Orientado a quien configura un tiling manager; mismo paradigma geométrico. |
