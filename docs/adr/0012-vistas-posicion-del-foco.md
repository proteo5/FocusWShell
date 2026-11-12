# ADR 0012 — Vistas: un solo foco, que puede ir a la izquierda, al centro o a la derecha

- **Estado:** Aceptada (2026-11-10); enmendada el 2026-11-12 (la disposición del principal son columnas que se prenden y apagan)
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

Tercera parte hecha:

- **Separador entre dos paneles apilados** (`StackSeparator`): redimensionar la ventana de arriba por su borde de abajo, o la de abajo por su borde de arriba, reparte el alto de la columna (`SplitColumn`) y se guarda en los paneles (`Height`). El reparto nunca deja a una ventana sin sitio: si no cabe su mínimo, se lleva al límite con una nota, y sólo sale del layout si ni así cabe.
- **Pantalla completa** (punto 4): `EnterFullscreen(ventana, monitor)` la lleva al foco si no estaba y la maximiza en el monitor elegido; la columna del foco queda vacía y las tiras de ese monitor se ocultan. `ExitFullscreen`, o des-maximizarla con el botón de Windows, la devuelve al foco. Promover otra ventana termina la pantalla completa como termina el Centro: la anterior vuelve a su panel. Botón "Pantalla completa" con el monitor elegido y **Ctrl+Alt+Mayús+F** (el foco, en el principal). Si su monitor se desconecta, sigue en pantalla completa en el principal.

## Enmienda (2026-11-12): la disposición del principal son columnas que se prenden y apagan

### Contexto

Las tres vistas y sus anchos se refinaron en un ultrawide (3440 × 1440), donde funcionan bien. En la práctica, los anchos fijos (Normal, Foco ancho, Mitad y mitad) no se usaban: arrastrar el separador da el ancho exacto y se guarda. Y las vistas no sirven a quien tiene un principal normal (16:9 o 16:10):

- **Con un solo monitor normal**, tres columnas dejan cada panel en unos 380 px. Los paneles hacen falta, pero sólo uno a la vez, junto al foco (unos 66 / 33).
- **Con un principal normal y más monitores**, lo habitual es no tener paneles en el principal: el foco lo ocupa entero y los paneles viven en los demás monitores, que ya se pueden tener.

### Decisión

1. **La disposición del principal se elige prendiendo y apagando sus dos columnas**: la de la izquierda (Contexto) y la de la derecha (Trabajo). El foco ocupa lo que queda. La pregunta deja de ser «¿dónde va el foco?» y pasa a ser «¿qué quiero alrededor del foco?».
2. **Una columna apagada** minimiza las apps de sus paneles y oculta sus tiras, como las ocultas de una pila. Las apps siguen en su panel: el deck lo muestra apagado, tocar una de sus apps la lleva al foco, y prender la columna devuelve todo a su sitio.
3. **En un ultrawide** (proporción del área del principal de 2,1 : 1 o más), aparece un botón extra en el borde exterior cuando el lado contrario está apagado. Ese botón **trae la columna del otro lado a este lado**: así se conservan las vistas con el foco a un lado y los dos paneles juntos.
4. **Las seis disposiciones**, con el orden de siempre (Contexto en el borde, Trabajo junto al foco):

   | Columnas prendidas | Pantalla | Proporciones por defecto | Antes |
   | --- | --- | --- | --- |
   | Contexto y Trabajo | Contexto · **Foco** · Trabajo | 18 · 55 · 27 | Foco al centro |
   | Contexto y «⇠ Trabajo» (ultrawide) | Contexto · Trabajo · **Foco** | 15 · 30 · 55 | Foco a la derecha |
   | Trabajo y «Contexto ⇢» (ultrawide) | **Foco** · Trabajo · Contexto | 55 · 30 · 15 | Foco a la izquierda |
   | Sólo Contexto | Contexto · **Foco** | 35 · 65 | (nueva) |
   | Sólo Trabajo | **Foco** · Trabajo | 65 · 35 | (nueva) |
   | Ninguna | **Foco** (Solo foco) | 100 | (nueva) |

5. **Reglas de los botones**, para que no haya estados raros:
   - El extra existe sólo si su lado está prendido y el contrario apagado.
   - Prender el lado contrario devuelve la columna prestada a su sitio, y el extra desaparece.
   - Apagar el lado donde está la columna prestada apaga las dos.
6. **Si el principal deja de ser ultrawide**, las dos disposiciones con columna prestada se muestran como «Contexto y Trabajo» (la prestada vuelve a su lado). Lo guardado no cambia: al volver el ultrawide, vuelve como estaba.
7. **Se quitan los anchos fijos.** Los anchos se ajustan arrastrando el separador y se guardan por disposición, como antes por vista. Para volver al ancho por defecto queda «Restablecer ancho» en el menú de los botones.
8. **La pantalla completa de una app** (punto 4, `EnterFullscreen`) no cambia: maximiza esa app en el monitor elegido, también un auxiliar. Solo foco es otra cosa: una disposición, que vale para la app que esté en el foco.

### Consecuencias

- El modelo de dominio no cambia: `FocusPosition` gana tres valores (`ContextOnly`, `WorkOnly` y `FocusOnly`) que el motor calcula como la disposición al centro sin esas columnas. Lo guardado (`panels.json`, escenas) sigue valiendo: las tres vistas de antes son tres de las seis disposiciones.
- El atajo Ctrl+Alt+Mayús+V recorre las disposiciones que ofrece el principal.
- El principal sigue necesitando un panel, aunque esté apagado. Permitirlo sin paneles queda para cuando haga falta: con «Ninguna» no se ve.
- Detectar el ultrawide por proporción falla con un 16:9 muy grande (4K). Si hace falta, se podrá forzar en Ajustes.

### Implementación de la enmienda (2026-11-12)

- **Core:** `FocusPosition` gana `ContextOnly`, `WorkOnly` y `FocusOnly`. `MainArrangement` reúne las reglas: qué botones hay, qué hace cada uno, cuándo es ultrawide (`IsUltrawide`), la disposición efectiva sin ultrawide (`Effective`) y el orden del atajo (`Next`).
- **Motor:** una columna apagada mide cero, no consume separación y sus ventanas salen como `Minimized`. La disposición se calcula como la de foco al centro sin esa columna; el foco que no cabe se ancla al borde del monitor que toca. El separador arrastrado ignora las columnas apagadas.
- **Los anchos fijos se quitaron** (`ViewProportions.Presets`). `SetProportions(disposición, null)` restablece el ancho.
- **Deck:** «Disposición del principal» es una fila de interruptores con el nombre de cada columna (el de su primer panel). En el deck estrecho van en la cabecera, sólo con su icono. El menú de cada interruptor tiene «Restablecer ancho». Un panel apagado se ve atenuado y su lugar dice «apagado».
- **Ventana principal:** el selector lista las disposiciones que ofrece el principal, con el ancho actual y «Restablecer ancho».
- **Las tiras** de un panel apagado se ocultan solas: no tiene banda en el layout.
