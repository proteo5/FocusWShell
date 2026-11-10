# ADR 0012 — Vistas: un solo foco, que puede ir a la izquierda, al centro o a la derecha

- **Estado:** Propuesta
- **Fecha:** 2026-11-10

## Contexto

Hoy el monitor principal es siempre Contexto | Centro | Trabajo, con el foco en medio. Hay trabajo que pide otra disposición. Por ejemplo, al depurar se quiere el IDE en el foco y el navegador muy cerca y grande. O, según dónde esté sentado el usuario o dónde tenga la tablet, el foco se mira mejor en un lado.

Se valoró permitir **dos áreas de foco**: IDE y navegador, ambos como foco. Se descartó por lo siguiente:

- **La atención real tiene un foco.** El segundo sólo sirve para comparar, y eso se resuelve con un panel ancho al lado.
- **Dos focos obligan a revisar el modelo:** el invariante 1 y `Promote`, que tendría que saber a qué área va cada ventana, y el deck, que necesitaría botones "Foco A" y "Foco B".
- **En monitores muy anchos**, el caso típico (como una sala de cámaras de seguridad) no es "muchos focos". Son muchos paneles pequeños en Tiles y un foco al que se lleva lo que se quiere mirar con detalle, y eso ya cabe en el modelo.

## Decisión

1. **Una vista es una plantilla del monitor principal:** dice **dónde va el foco** y **qué proporción** tiene cada columna. Hay un solo foco, siempre; el invariante 1 no cambia.

   | Vista | Columnas, de izquierda a derecha | Proporciones por defecto | Uso típico |
   | --- | --- | --- | --- |
   | **Foco al centro** (la de hoy) | Contexto · **Foco** · Trabajo | 18 · 55 · 27 | Trabajo normal |
   | **Foco a la izquierda** | **Foco** · Trabajo · Contexto | 55 · 30 · 15 | Depurar: IDE en el foco, navegador en Trabajo |
   | **Foco a la derecha** | Contexto · Trabajo · **Foco** | 15 · 30 · 55 | Lo mismo en espejo |

   Las proporciones son de cada vista y se podrán ajustar. Para ver dos ventanas igual de grandes, basta con poner Trabajo al 50 %.
2. **Alternar entre el foco y el panel de al lado** es `SwapWithPrevious`, que ya existe. Tocar o promover otra app la lleva al foco y la anterior vuelve a su grupo, como hoy.
3. **Pantalla completa = maximizada.** `EnterFullscreen(ventana, monitor)` maximiza la ventana del foco en el monitor elegido, que puede ser el principal o un auxiliar. `ExitFullscreen` la devuelve al foco. Mientras dure, las tiras de ese monitor se ocultan.
4. **El motor de layout del principal** pasa de un orden fijo a un **orden de columnas**, definido por la vista. Sus reglas siguen igual:
   - el foco está protegido;
   - los tamaños mínimos se aprenden y se respetan;
   - los bordes que dan a otro monitor no se desbordan.

   Con una salvedad para el **panel de en medio**: con el foco en un extremo, el panel que queda entre el foco y el otro panel no puede desbordarse hacia ningún lado sin tapar a otro. Si una ventana no cabe ahí, el panel pasa a Pila y, si ni así cabe, la ventana sale del layout, como en los monitores auxiliares.

## Consecuencias

- No cambia el modelo de dominio: un solo `Center`, el mismo `Promote` y el mismo historial del Centro.
- Las escenas guardan su vista. Las escenas actuales equivalen a "Foco al centro".
- El deck no necesita destinos por área: tocar una app siempre la lleva al foco.
- Cambiar de vista mueve el foco de sitio sin cambiar qué ventana hay en él.
