# ADR 0018 — Apps que se cierran a la bandeja: no mostrar nosotros sus ventanas

- **Estado:** Aceptada
- **Fecha:** 2026-11-12

## Contexto

Muchas apps no se cierran con su X ni con «Cerrar» del deck: ocultan su ventana y siguen en la bandeja (Codex, Discord, Teams y otras de Electron). La ventana sigue existiendo, sólo sin `WS_VISIBLE`.

FocusWShell sólo daba por cerrada una ventana que ya no existía. Una gestionada que dejaba de ser elegible seguía en su panel con su última foto, una regla pensada para las que cambian de estilo un momento (pantalla completa propia). Con Codex eso causó dos problemas:

- Seguía en el deck como si estuviera abierta.
- Al tocarla, colocarla en el foco la mostraba con `ShowWindow`. La ventana aparecía, pero **congelada**: Electron detiene el dibujo y la entrada al ocultarse, y sólo los reanuda cuando la app misma se muestra. Al mostrarla desde su icono de la bandeja, volvía a responder.

Cualquier acomodo (Reordenar todas, una escena, cambiar la disposición) la habría mostrado igual, sin que el usuario lo pidiera.

## Decisión

**Una ventana que su app ocultó está "en la bandeja", y FocusWShell nunca la muestra.**

- Al ocultarse (el evento `EVENT_OBJECT_HIDE` o, si se perdió, el siguiente refresco), sale del modelo como si se cerrara (`WindowClosed`): ningún acomodo, ni salir, ni la emergencia la tocan. El controlador la recuerda aparte, con su app y su panel.
- El deck y la presentación de lista la muestran **atenuada con ▴** en su panel, aunque no esté anclada.
- **Tocarla lanza la app otra vez** (como una cerrada, `OpenApp`). Las apps de bandeja son de una sola instancia: la que corre recibe el aviso y muestra su ventana ella misma, viva. `OpenApp` la adopta en el panel y la lleva al foco: se tocó para usarla. Sólo adoptarla no basta: en un panel apagado o en pestañas quedaba minimizada al aparecer, y había que tocarla otra vez.
- Si la app vuelve a mostrar su ventana por su cuenta (su icono de la bandeja), el refresco la adopta en su panel de antes y la lleva al foco.
- Si la ventana deja de existir, se olvida.

## Consecuencias

- Al volver va al foco aunque antes estuviera en su panel; su panel se conserva.
- Si una app oculta su ventana sólo un momento, sale del modelo y regresa a su panel, en el foco, en el siguiente refresco.
- Si la app no se puede lanzar (sin ejecutable ni AUMID), tocarla dice por qué; se puede abrir desde su icono de la bandeja.
- Una ventana oculta no sale en **Ventanas** de la ventana principal.
- «Ocultar» como acción del usuario es minimizar: ya existe como **Estacionar** ([ADR 0006](0006-ocultar-con-minimizar.md)).
