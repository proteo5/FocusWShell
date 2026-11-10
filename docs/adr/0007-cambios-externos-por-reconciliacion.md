# ADR 0007 — Los cambios externos se detectan reconciliando el estado final, no atribuyendo eventos

- **Estado:** Aceptada (validada en S4)
- **Fecha:** 2026-11-09

## Contexto

El gestor tiene que saber cuándo el usuario minimiza, maximiza, arrastra o cierra una ventana gestionada (riesgo R4) y no confundirlo con sus propios movimientos. Windows avisa con `SetWinEventHook` (`LOCATIONCHANGE`, `MINIMIZESTART/END`, `MOVESIZESTART/END`, `DESTROY`), pero los eventos no dicen **quién** provocó el cambio.

La idea original de [03 — Arquitectura](../03-arquitectura.md#eventos-que-se-escuchan) era registrar cada geometría esperada con un plazo e ignorar los `LOCATIONCHANGE` que coincidieran con ella. Con el aplicador de S3 (movimiento asíncrono en paralelo) eso falla: un solo movimiento propio produce varios eventos intermedios que no coinciden con el destino.

- Normalizar una ventana maximizada.
- Llegar al rectángulo pedido.
- Al cruzar a un monitor con otro DPI, el reescalado que hace la propia app.
- La corrección posterior del aplicador.

## Decisión

1. **Reconciliar el estado final.** El observador (`WindowEventWatcher`) espera a que una ventana vigilada lleve 120 ms sin eventos y entonces lee su estado (existe, minimizada, maximizada, rectángulo visible). `ExpectationRegistry` lo compara con lo que el gestor dejó en ella:
   - Si coincide con lo esperado (±1 px), los eventos eran del gestor.
   - Si no coincide, hubo un cambio externo: `Closed`, `Minimized`, `Shown`, `Maximized` o `MovedOrResized`.
   - Cuando hay un cambio externo, la expectativa pasa a ser lo observado, para no informar dos veces de lo mismo.
2. **Silencio mientras el gestor mueve.** Cada aplicación abre un `OwnChange()`; mientras esté abierto no se entrega nada. Antes de cerrarlo, el gestor registra las nuevas expectativas a partir del resultado verificado (el rectángulo *obtenido*, no el pedido). Al cerrarse, las ventanas tocadas se comprueban contra ellas.
3. **Un arrastre se evalúa al soltar.** Entre `MOVESIZESTART` y `MOVESIZEEND` una ventana no se evalúa.
4. **Las expectativas son lo que el aplicador verificó.** Si el aplicador deja una ventana fuera de sitio, por ejemplo porque una app se reescala tras cruzar de DPI, la reconciliación lo detecta como cambio externo. Por eso el aplicador tiene que dejar las ventanas donde dice. S4 encontró justo ese caso: ver [04 — Riesgos y spikes](../04-riesgos-y-spikes.md#s4--eventos-propios-2026-11-09).

## Consecuencias

- No hace falta atribuir eventos ni ajustar plazos frágiles. El criterio es el mismo para cualquier origen del cambio: el usuario, otra app o un gestor de ventanas de terceros.
- Un cambio que el usuario deshace antes de 120 ms (mover y volver al mismo sitio) no se detecta. Es el comportamiento deseado.
- Una app que se mueve sola (por ejemplo, al reconectar un monitor) se trata como cambio externo. Es correcto: el gestor no la dejó así.
- Hay que vigilar sólo las ventanas gestionadas: el filtro por handle se hace en el callback, antes de guardar nada.
- Qué hacer con un `MovedOrResized` depende de la geometría: el núcleo propone `SnapBack` y el controlador, que conoce el último layout, lo cambia por `Promote` o `SendTo` según dónde se soltó la ventana (ver [02 — Modelo de dominio](../02-modelo-de-dominio.md#eventos-externos-y-cómo-los-interpreta-el-núcleo)). Hasta el 2026-11-09 un arrastre dejaba la ventana fuera de layout (`Detach`).
