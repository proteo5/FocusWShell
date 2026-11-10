# ADR 0013 — El deck es una superficie de control opcional, en el monitor que se elija

- **Estado:** Propuesta (pendiente del spike S8)
- **Fecha:** 2026-11-10

## Contexto

Mover el foco con atajos obliga a memorizarlos, y la ventana principal de FocusWShell tapa el trabajo. La idea es un **deck**, al estilo de un Stream Deck pero consciente del escritorio. Muestra las apps por grupos ([ADR 0011](0011-grupos-de-apps-y-catalogo.md)), las vistas ([ADR 0012](0012-vistas-posicion-del-foco.md)) y el estado actual, y desde ahí se manejan con un toque.

Puede vivir en un monitor táctil, sea una tablet que extiende el escritorio ([ADR 0009](0009-tablets-como-monitores-auxiliares.md)) o un monitor táctil físico conectado al PC. Para FocusWShell da igual cuál: sólo necesita un monitor y que lleguen los toques.

## Decisión

1. **Todo funciona sin el deck.** Es una vista y un control encima del modelo y de los comandos que ya existen. Si no está, o se desconecta su monitor, siguen la bandeja, los atajos y las tiras de pestañas.
2. **Qué muestra:**
   - **Grupos de apps:** iconos en color vivo si la app está abierta y apagados si no. Tocar una cerrada la abre.
   - **Estado actual:** qué hay en el foco, en cada grupo y estacionado.
   - **Vistas:** foco al centro, a la izquierda o a la derecha.
   - **Botones fijos:** escenas, pantalla encendida y "Restaurar todas", este último con confirmación.
3. **Gestos:**
   - Tocar una app la lleva al foco, y la anterior vuelve a su grupo.
   - Un botón "anterior" alterna entre la app del foco y la que estaba antes (`SwapWithPrevious`).
   - Arrastrar un icono a otro grupo o monitor lo cambia de sitio.
   - Mantenerlo pulsado abre su menú, que incluye pantalla completa en un monitor elegido.
   - Un control de navegación permite recorrer pestañas y grupos, cambiar el template, cambiar la vista (mover el foco) o el ancho del foco.
4. **Dónde vive, por fases:**
   1. **En una esquina del monitor principal.** Se muestra y se oculta con un clic o un atajo. Al elegir una app se oculta solo, como una paleta de comandos, así que sólo tapa el foco un instante.
   2. **En otro monitor**, con "mandar el deck a…". Ahí ocupa una zona fija; en una tablet puede compartir pantalla con un grupo compacto (Spotify, monitoreo).
   3. *(Opcional, más adelante)* **Como página web** en la tablet o el teléfono, por la red local. No es un driver ni un cliente de monitor, así que no contradice el ADR 0009, pero exige emparejar el dispositivo y proteger el acceso.
5. **Una "API de deck" en el controlador**, con el estado y los comandos, que usan por igual la ventana del deck y, si llega, la versión web. Así la fase 3 no obliga a reescribir nada.

## Consecuencias

- La ventana del deck es nuestra, con `WS_EX_NOACTIVATE`, como las tiras de pestañas. El riesgo principal es que un toque remoto (Apollo/Artemis) o físico **robe el foco** a la app del foco. El spike S8 lo mide antes de construir las fases 2 y 3.
- Los objetivos táctiles necesitan unos 48 px a la escala del monitor (R11, ADR 0009). En el principal, con ratón, pueden ser más pequeños.
- El deck reacciona a la desconexión de su monitor como cualquier auxiliar (S6): vuelve a la esquina del principal, oculto.
