# ADR 0013 — El deck es una superficie de control opcional, en el monitor que se elija

- **Estado:** Propuesta; S8 hecho (2026-11-10): tocar una ventana WPF sin foco, también desde la tablet por Artemis, no le quita el foco a la app del Centro
- **Fecha:** 2026-11-10

## Contexto

Mover el foco con atajos obliga a memorizarlos, y la ventana principal de FocusWShell tapa el trabajo. La idea es un **deck**, al estilo de un Stream Deck pero consciente del escritorio. Muestra las apps por paneles ([ADR 0011](0011-paneles-y-catalogo.md)), las vistas ([ADR 0012](0012-vistas-posicion-del-foco.md)) y el estado actual, y desde ahí se manejan con un toque.

Puede vivir en un monitor táctil, sea una tablet que extiende el escritorio ([ADR 0009](0009-tablets-como-monitores-auxiliares.md)) o un monitor táctil físico conectado al PC. Para FocusWShell da igual cuál: sólo necesita un monitor y que lleguen los toques.

## Decisión

1. **Todo funciona sin el deck.** Es una vista y un control encima del modelo y de los comandos que ya existen. Si no está, o se desconecta su monitor, siguen la bandeja, los atajos y las tiras de pestañas.
2. **Qué muestra:**
   - **Paneles y sus apps:** iconos en color vivo si la app está abierta y apagados si no. Tocar una cerrada la abre.
   - **Estado actual:** qué hay en el foco, en cada panel, estacionado y libre.
   - **Vistas:** foco al centro, a la izquierda o a la derecha.
   - **Botones fijos:** escenas, pantalla encendida y "Restaurar todas", este último con confirmación.
3. **Gestos:**
   - Tocar una app la lleva al foco, y la anterior vuelve a su panel.
   - Un botón "anterior" alterna entre la app del foco y la que estaba antes (`SwapWithPrevious`).
   - Arrastrar un icono a otro panel lo cambia de sitio; arrastrar una ventana libre a un panel la asigna.
   - Mantenerlo pulsado abre su menú, que incluye pantalla completa en un monitor elegido.
   - Un control de navegación permite recorrer pestañas y paneles, cambiar el template, cambiar la vista (mover el foco) o el ancho del foco.
4. **Dónde vive, por fases:**
   1. **En una esquina del monitor principal.** Se muestra y se oculta con un clic o un atajo. Al elegir una app se oculta solo, como una paleta de comandos, así que sólo tapa el foco un instante.
   2. **En otro monitor**, con "mandar el deck a…". Ahí ocupa una zona fija; en una tablet puede compartir pantalla con un panel de pantalla completa con pestañas (Spotify, monitoreo).
   3. *(Opcional, más adelante)* **Como página web** en la tablet o el teléfono, por la red local. No es un driver ni un cliente de monitor, así que no contradice el ADR 0009, pero exige emparejar el dispositivo y proteger el acceso.
5. **Una "API de deck" en el controlador**, con el estado y los comandos, que usan por igual la ventana del deck y, si llega, la versión web. Así la fase 3 no obliga a reescribir nada.

## Consecuencias

- La ventana del deck es nuestra, con `WS_EX_NOACTIVATE`, como las tiras de pestañas. El riesgo principal es que un toque remoto (Apollo/Artemis) o físico **robe el foco** a la app del foco. El spike S8 lo mide antes de construir las fases 2 y 3.
- Los objetivos táctiles necesitan unos 48 px a la escala del monitor (R11, ADR 0009). En el principal, con ratón, pueden ser más pequeños.
- El deck reacciona a la desconexión de su monitor como cualquier auxiliar (S6): vuelve a la esquina del principal, oculto.

## Implementación de la fase 2 (2026-11-10, primera versión)

- **Ventana del deck** (`DeckWindow`): sin foco y siempre encima, en la esquina inferior derecha del área de trabajo del principal (640 × 980 px lógicos como máximo).
- **Qué muestra:**
  - el foco y su título, con «Anterior» (`SwapWithPrevious`) y «Foco al panel»;
  - las tres vistas, con la actual resaltada;
  - cada panel con sus apps: abiertas con su icono, cerradas del catálogo apagadas, y marcas ↑ (en el foco), ⏸ (estacionada) y ↗ (fuera de layout);
  - las ventanas libres;
  - las escenas;
  - «Pantalla encendida» y «Restaurar todas», éste con un segundo toque de confirmación.
- **Toques:** botones de 48 px como mínimo.
  - Tocar una app la lleva al foco. Si es libre, primero la asigna al panel derecho del principal; si está cerrada, la abre en su panel.
  - Clic derecho, o pulsación larga en táctil, abre su menú: al foco, pantalla completa en un monitor, estacionar o recuperar, mover a otro panel, liberar.
- **Se oculta solo** al elegir una app o una escena. También con su ✕, con **Ctrl+Alt+Mayús+D** (configurable), con «Mostrar u ocultar el deck» en la bandeja o con el botón «Deck» de la ventana principal.
- **«API de deck»:** `ShellController.DeckState()` reúne el estado en un solo sitio; los comandos son los de siempre.
- **Arrastrar:** soltar una app sobre otro panel la mueve (`SendTo`); soltar una ventana libre la asigna (`Adopt`). La zona se ilumina al pasar por encima. Moverla menos de 12 px cuenta como un toque.
- **Navegación:**
  - en la cabecera de cada panel, ◀ ▶ recorren sus pestañas (en pestañas o pantalla completa) y un botón muestra la presentación y pasa a la siguiente (Tiles, Pestañas, Lista y, en un auxiliar, Pantalla completa);
  - bajo «Vista», los presets de ancho del foco, con el actual resaltado.
- **Pendiente:** el [ADR 0014](0014-mapa-de-controles-en-el-deck.md).

## Implementación de la fase 4 (2026-11-10)

- **«Mandar el deck a…»** (📌 en el deck): a la esquina del principal, como siempre, o a otro monitor. En otro monitor se elige la **mitad izquierda**, la **mitad derecha** o **entero**; entero sólo si ese monitor no tiene paneles.
- **Fijado en un monitor:**
  - el deck **no se oculta al elegir**: vive ahí;
  - los paneles de ese monitor se colocan **en el resto de la pantalla** (`DeckDock.Split`), porque el controlador reduce el área de trabajo de ese monitor antes de calcular el layout.
- **Se guarda** en `settings.json` (`DeckDisplay`, `DeckZone`) y se recupera al arrancar.
- **Desconexión:** si su monitor se desconecta, el deck se oculta; si vuelve, reaparece allí.
- **Pendiente:**
  - comprobar la desconexión con la tablet de verdad;
- **Pantalla completa junto al deck:** en el monitor donde está fijado el deck, un panel en «Pantalla completa» no se maximiza. Ocupa el área que deja el deck, bajo su tira de pestañas (`AuxInput.NoMaximize`). Es la idea de Spotify en la tablet.

## Enmienda: el lugar del foco y el modo Minimizar (2026-11-13)

- **El foco tiene su lugar propio.** En el deck ancho, la cabecera pasa a ser FocusWShell (logo, el monitor que se ve y cuántas apps tiene), y debajo, la app del foco en un tile grande que ocupa el espacio que sobra, encima de los botones (lo primero que se mira; los botones quedan abajo, más cerca de la mano): icono, nombre, de qué panel viene y el título de su ventana. En el deck angosto sigue arriba, en la cabecera. En los dos, se toca como una app: en un modo, el modo; si no, su menú. En «Mover», con otra app elegida, es destino: la lleva al foco. En su panel, la del foco ya no va pintada de azul: lleva un borde azul y la ↑, para que no haya dos cosas iguales.
- **Botones.** Arriba las de acción inmediata: Anterior, Al panel y Liberar todas (que pide un segundo toque). Debajo los cuatro modos en 2×2: Mover y Anclar, que acomodan, a la izquierda; Minimizar y Cerrar, que quitan de la vista, a la derecha. En el deck angosto, dos filas: tres acciones y cuatro modos. Ratón al foco pasa a *Sistema*, junto a Pantalla encendida, Energía y Reordenar todas.
- **Modo Minimizar…** (verde azulado): como Cerrar, se apaga después de un toque (encendido como Anclar se sentía pegado: cada clic minimizaba otra app). Alterna: minimiza una app en su panel (`Park`) o la trae de vuelta (`Restore`). Sólo para apps en un panel; las libres no tienen dónde quedarse.
- **«Estacionar» se llama «Minimizar»** en la interfaz y en el sitio. En el código y en el dominio sigue siendo `Park` ([ADR 0006](0006-ocultar-con-minimizar.md)).
