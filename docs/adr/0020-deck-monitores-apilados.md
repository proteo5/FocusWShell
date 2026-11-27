# ADR 0020 — El deck muestra todos los monitores a la vez, uno arriba del otro

- **Estado:** Aceptada (0.6.0-alpha; propuesta y prototipo del 2026-11-26)
- **Fecha:** 2026-11-26

## Contexto

La parte derecha del deck ([ADR 0013](0013-deck-superficie-de-control.md)) tiene una pestaña por monitor («Main · 5», «ZL480X1920 · 3»), la pestaña 🗂 Páginas y, debajo, los paneles del monitor elegido y la zona Libre. Con el uso de varios días se ven tres problemas:

- **Las pestañas esconden la mitad de los paneles.** Para tocar Slack desde Main hay que cambiar de pestaña primero, aunque sobra espacio.
- **Mucho espacio vacío.** Cada panel ocupa media pantalla y suele tener de 1 a 4 apps.
- **Controles que se usan poco ocupan mucho.** Cada panel lleva 3 o 4 botones de vista (cuadrícula, pestañas, lista, pantalla completa) de 48 pt.

La parte izquierda (foco, acciones, disposición y sistema) funciona bien y no cambia.

## Opciones evaluadas

| Opción | Idea | A favor | En contra |
| --- | --- | --- | --- |
| **A. Todos los monitores a la vista** | Sin pestañas: cada monitor con sus paneles en columnas. | Casi nada que aprender; sigue siendo la cuadrícula de iconos de hoy. | Con muchos monitores o apps, hay que desplazar. |
| B. Mapa del escritorio | Cada monitor dibujado con su forma, sus paneles y el foco en su sitio. | Lo más intuitivo; abre la puerta a arrastrar. | Las apps quedan como etiquetas, no iconos de 48 pt; un monitor vertical sale muy angosto; el cambio más grande. |
| C. Carriles por panel | Cada panel es una fila de etiquetas, agrupadas por monitor. | Lo más compacto. | Se pierden los iconos grandes, lo que hace rápido el deck en la tablet. |

## Decisión

La **opción A, con los monitores apilados**:

- **Sin pestañas de monitor.** Cada monitor es una franja con sus paneles en columnas, en su orden físico (izquierda, centro, derecha). Las franjas van una arriba de la otra: primero el principal y luego los auxiliares, en el orden de siempre.
- **El nombre del monitor va en vertical** a la izquierda de su franja, con cuántas apps tiene, para no gastar alto. Es sólo una etiqueta: hoy la pestaña del monitor no tiene menú propio. El panel muestra sólo su lado («izquierda»), porque el monitor ya se lee a la izquierda.
- **Cada franja mide lo que piden sus apps,** con un mínimo de una fila de iconos. Si no cabe todo, se desplaza la zona entera, nunca cada panel por separado.
- **Los botones de vista de cada panel siguen a la vista** (cuadrícula, pestañas, lista y, fuera del principal, llenar), como hoy. Se probó moverlos a un menú ⋯ y se descartó (2026-11-26): un toque más para algo que se ve y se cambia directo no compensaba el espacio ganado.
- **Abajo, una franja con Libre a la izquierda y 🗂 Páginas a la derecha,** las dos cosas que están fuera de los paneles. Páginas conserva su número y abre la misma lista de hoy.
- **El título del lado izquierdo** deja de decir qué pestaña se ve: muestra el total («2 monitores · 8 apps»).
- **Los iconos siguen siendo de 48 pt.** Las apps ancladas y cerradas siguen atenuadas en su panel.

## Plan

1. **Prototipo en la vista previa** (`--deck-preview`, datos de ejemplo), sin tocar el deck real. Se prueba en la tablet entera y en la esquina del principal, y con dos y tres monitores.
2. Si convence, se cambia el deck real y se actualizan el manual y sus capturas.

## Prototipo (2026-11-26)

`FocusWShell.exe --deck-preview <carpeta> es --apilado` dibuja el prototipo con datos de ejemplo: dos y tres monitores, muchas apps, modo mover y la lista de páginas, en la tablet entera y en la esquina del principal.

Para probarlo con el uso, el deck real lo trae prendido con un interruptor en General, «Deck: todos los monitores a la vez» (`AppSettings.DeckStacked`, `DeckWindow.Stacked`). Apagado, vuelve una pestaña por monitor.

Lo que se vio:

- **Dos monitores** (el caso de uso diario): los cuatro paneles caben con iconos grandes y sobra aire.
- **Tres monitores en la tablet:** caben sin desplazar; los iconos de las franjas con pocas apps se encogen un poco.
- **Mover:** todos los paneles de todos los monitores son destino a la vez; ya no hace falta cambiar de pestaña a mitad del gesto.
- **Esquina del principal:** los paneles van uno debajo de otro con el nombre del monitor encima; «Páginas» queda como un botón de icono junto a Libres.

## Consecuencias

- Todas las apps de todos los monitores se ven y se tocan de un toque, sin cambiar de pestaña.
- En la esquina del principal, con tres monitores, la zona de paneles puede necesitar desplazarse. El prototipo dirá si basta con eso o si hacen falta iconos más pequeños en ese tamaño.

## Aceptación (0.6.0-alpha)

Tras varios días de uso diario, el diseño apilado funciona mejor que las pestañas y queda como el de siempre. Ajustes desde el prototipo:

- Los botones de presentación de cada panel siguen a la vista (el menú ⋯ se descartó).
- En el deck estrecho (media tablet, esquina del principal), la zona de paneles se desplaza con el dedo y Libres y Sistema quedan fijos debajo; cada panel mide lo que piden sus apps.
- El interruptor de General sigue para volver a una pestaña por monitor.
