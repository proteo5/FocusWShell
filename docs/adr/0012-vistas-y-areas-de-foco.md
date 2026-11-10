# ADR 0012 — Vistas con una o dos áreas de foco

- **Estado:** Propuesta (revisa el invariante 1 del modelo de dominio)
- **Fecha:** 2026-11-10

## Contexto

Hoy el Centro aloja una sola ventana (invariante 1). Hay trabajo que necesita **dos cosas a la vez** en el foco: por ejemplo, depurar con el IDE y el navegador lado a lado, o comparar dos documentos. Para eso el usuario arrastra ventanas a mano y sale del layout.

También se planteó si hacen falta más de dos áreas de foco en monitores muy anchos. El caso típico de esos monitores, como una sala de cámaras de seguridad, no es "muchos focos". Son **muchos paneles pequeños en Tiles y un foco** al que se lleva lo que se quiere mirar con detalle. Eso ya cabe en el modelo: paneles en Tiles y `Promote`.

## Decisión

1. **Una vista es una plantilla del escritorio completo.** Dice cuántas **áreas de foco** hay en el monitor principal, qué ancho tiene cada una y qué grupos laterales van a cada lado. No es lo mismo que el template de un panel (Tiles, Pila, Fill), que sólo decide cómo se presenta un panel.

   | Vista | Foco | Laterales |
   | --- | --- | --- |
   | **Foco 1** (la de hoy) | Un área: el Centro | Uno a cada lado |
   | **Foco 2** | Dos áreas lado a lado (A y B) | Uno a cada lado |
   | Foco 2 compacta | Dos áreas | Un solo lateral, para monitores más estrechos |

2. **Como máximo dos áreas de foco.** La atención real tiene un foco; el segundo sirve para comparar o trabajar en paralelo; un tercero ya no se mira a la vez. El modelo guarda las áreas como una lista, para no cerrar la puerta, pero las vistas y la interfaz se limitan a una o dos.
3. **Invariante 1 revisado:** como máximo **una ventana por área de foco**, y como máximo una `Fullscreen` por monitor.
4. **A qué área va cada ventana:**
   - En el deck y en la UI hay un destino explícito por área (**Foco A** y **Foco B**), así que no hay que adivinar.
   - Con un atajo o un doble clic sin área indicada, va al área que se usó por última vez.
   - La ventana que ocupaba esa área vuelve a su grupo, como hoy con `Promote`.
5. **Pantalla completa = maximizada.** `EnterFullscreen(ventana, monitor)` maximiza la ventana de foco en el monitor elegido, que puede ser el principal o un auxiliar. `ExitFullscreen` la devuelve a su área de foco. Mientras dure, las tiras de ese monitor se ocultan (03-arquitectura, "Overlays del gestor").
6. **Proporciones:** en Foco 2, por defecto mitad y mitad, ajustable por vista. Cada área conserva la protección del Centro: nada de un lateral la invade y respeta los tamaños mínimos de sus ventanas.

## Consecuencias

- El motor de layout del principal pasa de "Contexto | Centro | Trabajo" a "laterales | áreas de foco | laterales". Las reglas de tamaños mínimos y de bordes compartidos se aplican a cada área de foco.
- `SwapWithPrevious` se aplica al área de foco activa.
- Las escenas guardan su vista. Las escenas actuales equivalen a "Foco 1".
- Hay que decidir qué pasa con la ventana del área B al cambiar de Foco 2 a Foco 1. Propuesta: vuelve a su grupo, como la que sale del Centro hoy.
