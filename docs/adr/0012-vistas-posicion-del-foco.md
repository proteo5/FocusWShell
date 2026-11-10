# ADR 0012 — Vistas: un solo foco, que puede ir a la izquierda, al centro o a la derecha

- **Estado:** Aceptada (2026-11-10)
- **Fecha:** 2026-11-10

## Contexto

Hoy el monitor principal es siempre Contexto | Centro | Trabajo, con el foco en medio. Hay trabajo que pide otra disposición. Por ejemplo, al depurar se quiere el IDE en el foco y el navegador muy cerca y grande. O, según dónde esté sentado el usuario o dónde tenga la tablet, el foco se mira mejor en un lado.

Se valoró permitir **dos áreas de foco**: IDE y navegador, ambos como foco. Se descartó por lo siguiente:

- **La atención real tiene un foco.** El segundo sólo sirve para comparar, y eso se resuelve con un panel ancho al lado.
- **Dos focos obligan a revisar el modelo:** el invariante 1 y `Promote`, que tendría que saber a qué área va cada ventana, y el deck, que necesitaría botones "Foco A" y "Foco B".
- **En monitores muy anchos**, el caso típico (como una sala de cámaras de seguridad) no es "muchos focos". Son muchos paneles pequeños en Tiles y un foco al que se lleva lo que se quiere mirar con detalle, y eso ya cabe en el modelo.

## Decisión

1. **Una vista es una plantilla del monitor principal:** dice **dónde va el foco**, **qué paneles van en cada columna** (hasta dos por columna, uno encima del otro; [ADR 0011](0011-paneles-y-catalogo.md)) y **qué proporción** tiene cada columna. Hay un solo foco, siempre; el invariante 1 no cambia.

   | Vista | Columnas, de izquierda a derecha | Proporciones por defecto | Uso típico |
   | --- | --- | --- | --- |
   | **Foco al centro** (la de hoy) | Contexto · **Foco** · Trabajo | 18 · 55 · 27 | Trabajo normal |
   | **Foco a la izquierda** | **Foco** · Trabajo · Contexto | 55 · 30 · 15 | Depurar: IDE en el foco, navegador en Trabajo |
   | **Foco a la derecha** | Contexto · Trabajo · **Foco** | 15 · 30 · 55 | Lo mismo en espejo |

   "Contexto" y "Trabajo" son los dos paneles por defecto y se pueden renombrar. Una vista puede poner otros paneles, o dos en la misma columna. Para ver dos ventanas igual de grandes, basta con poner el panel de al lado al 50 %.
2. **Proporciones: presets más ajuste a mano que se guarda.**
   - **Presets:** cada vista trae proporciones de partida (las de la tabla) y se puede elegir entre algunas variantes, por ejemplo "foco ancho" o "mitad y mitad".
   - **Ajuste arrastrando el borde**, como los separadores de los paneles en las apps de Windows:
     - Si el usuario redimensiona una ventana del layout **sólo por el borde que comparte con la columna vecina** (mismo alto, misma posición vertical), no es "fuera de layout": es mover el separador.
     - El gestor recalcula las proporciones, reajusta la columna vecina y **guarda** las nuevas proporciones en la vista.
     - Cualquier otro cambio (moverla, redimensionarla por otro borde) sigue siendo "fuera de layout" (S4).
   - Las proporciones ajustadas se guardan por vista y, si se guarda una escena, con la escena.
   - Más adelante, las tiras de pestañas pueden ofrecer un separador propio para hacer lo mismo sin tocar la ventana.
   - El ajuste respeta los tamaños mínimos aprendidos y el piso del foco: si un arrastre los viola, se lleva al límite y se explica con una nota.
3. **Alternar entre el foco y el panel de al lado** es `SwapWithPrevious`, que ya existe. Tocar o promover otra app la lleva al foco y la anterior vuelve a su panel, como hoy.
4. **Pantalla completa = maximizada.** `EnterFullscreen(ventana, monitor)` maximiza la ventana del foco en el monitor elegido, que puede ser el principal o un auxiliar. `ExitFullscreen` la devuelve al foco. Mientras dure, las tiras de ese monitor se ocultan.
5. **El motor de layout del principal** pasa de un orden fijo a un **orden de columnas**, definido por la vista. Sus reglas siguen igual:
   - el foco está protegido;
   - los tamaños mínimos se aprenden y se respetan;
   - los bordes que dan a otro monitor no se desbordan.

   Con una salvedad para el **panel de en medio**: con el foco en un extremo, el panel que queda entre el foco y el otro panel no puede desbordarse hacia ningún lado sin tapar a otro. Si una ventana no cabe ahí, el panel pasa a pestañas y, si ni así cabe, la ventana sale del layout, como en los monitores auxiliares. Lo mismo vale para dos paneles en una columna: se reparten el alto y ninguno invade al otro.

## Consecuencias

- No cambia el modelo de dominio: un solo `Center`, el mismo `Promote` y el mismo historial del Centro.
- Las escenas guardan su vista. Las escenas actuales equivalen a "Foco al centro".
- El deck no necesita destinos por área: tocar una app siempre la lleva al foco.
- Cambiar de vista mueve el foco de sitio sin cambiar qué ventana hay en él.
- El intérprete de cambios externos (`Workspace.Interpret`) gana un caso: "se redimensionó sólo por el borde con su vecina" se convierte en "mover el separador" y no en `Detach`. Hay que distinguirlo con una tolerancia de ±1 px en los demás bordes.

## Implementación (2026-11-10)

Primera parte hecha: las tres vistas con sus proporciones de partida.

- **Columnas por lado, orden por vista.** Cada panel sigue teniendo su lado (`Side`); la vista sólo decide el orden en pantalla. El panel del lado izquierdo (Contexto) queda siempre en un borde del monitor y el del derecho (Trabajo), junto al foco: foco a la izquierda = foco · Trabajo · Contexto; a la derecha = Contexto · Trabajo · foco. Así cada columna conserva sus paneles (hasta dos) al cambiar de vista.
- **`SetView(foco)`** cambia la vista sin tocar qué ventana está en el foco ni en qué panel está cada una. La vista se guarda en `panels.json` y en las escenas (las anteriores son "foco al centro").
- **Selector "Vista"** en la ventana principal y **Ctrl+Alt+Mayús+V** para pasar de una a otra (centro → izquierda → derecha).
- **Motor:** el panel de en medio no desborda (la ventana que no cabe sale del layout); el del borde se ancla hacia fuera, salvo que ese borde dé a otro monitor. Ese borde se calcula según la vista. Si ni así cabe la ventana del foco en un extremo, se ancla a su borde del monitor.

Segunda parte hecha, las proporciones:

- **Presets por vista**, en el selector "Proporciones": Normal (la tabla), Foco ancho (12 · 70 · 18 al centro; 10 · 70 · 20 a los lados) y Mitad y mitad (el foco y Trabajo al 45 %, Contexto al 10 %).
- **Separador arrastrado** (`ColumnSeparator`): si una ventana del principal, del foco o de un panel, cambia sólo el borde que comparte con la columna vecina (los demás quietos, ±2 px) y estaba alineada con su columna, los anchos resultantes pasan a ser las proporciones de la vista (`SetProportions`) y la ventana vuelve a su columna, ya con el ancho nuevo. Ninguna columna baja de 40 px al arrastrar; después, el motor aplica los mínimos aprendidos y el piso del foco, con sus notas. El borde del monitor no es un separador.
- **Se guardan por vista** en `panels.json` y, al guardar una escena, las de su vista. Si coinciden con un preset, el selector lo muestra; si no, "Personalizadas".

Pendiente: el separador entre dos paneles apilados en una columna (ADR 0011) y la pantalla completa (punto 4).
