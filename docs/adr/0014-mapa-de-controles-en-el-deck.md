# ADR 0014 — Apuntar en la app del foco desde el deck

- **Estado:** Propuesta (idea en exploración; pendiente del deck y del spike S9)
- **Fecha:** 2026-11-10

## Contexto

Con el deck ([ADR 0013](0013-deck-superficie-de-control.md)) la mano está en la tablet o en el monitor táctil. Volver al ratón para llegar a una caja de texto, un checkbox o una barra de scroll de la app del foco cuesta movimientos. La idea es mostrar en el deck la app del foco y apuntar en ella con un toque.

## Decisión (propuesta): por capas, de lo universal a lo específico

| Capa | Para qué apps | Qué ofrece |
| --- | --- | --- |
| **1. Imagen en vivo + cuadrícula** (base) | Todas | Una miniatura DWM en vivo de la ventana del foco (`DwmRegisterThumbnail`): la dibuja Windows, sin capturas ni coste propio. Un toque lleva el cursor al punto equivalente de la app. Una cuadrícula permite **acercar** una zona; la miniatura admite un rectángulo de origen, así que el zoom sale gratis. |
| **2. Sólo cuadrícula** | Apps donde la imagen no aporta o sale en negro (contenido protegido) | Un rectángulo proporcional a la ventana con su cuadrícula y el mismo acercamiento. |
| **3. Controles detectados** (mejora opcional) | Apps nativas; Chromium/Electron con más coste | UI Automation resalta cajas de texto, checkboxes y botones sobre la imagen. Tocar uno actúa directamente, sin mover el ratón: `SetFocus`, `Toggle`, `Invoke`, `ExpandCollapse`. |
| — | Apps elevadas | No se ofrece la opción (no se pueden leer ni controlar, R5). |

- **Tocar no es hacer clic.** Un toque mueve el cursor; hacer clic es un gesto aparte (doble toque o un botón "clic"), para que no haya pulsaciones accidentales.
- **Scroll:**
  - Una franja lateral del deck, como un touchpad: deslizar envía la rueda del ratón **en el último punto tocado de la app**. Funciona en los navegadores, que es donde más cuesta.
  - Deslizar sobre la propia imagen hace lo mismo, en ese punto.
  - Con la capa 3, además se puede saltar a un porcentaje del documento (`ScrollPattern`).
- **Privacidad:** la capa 3 muestra formas y etiquetas, nunca valores, y omite los campos de contraseña (`IsPassword`). Nada se guarda en disco.

## Consecuencias

- La capa 1 es simple y casi universal. Funciona con el deck como ventana, también en una tablet con Apollo, porque la miniatura se compone en ese monitor y viaja en el streaming. En un deck web (fase 3 del ADR 0013) habría que capturar y enviar vídeo: es mucho más pesado y no forma parte de esta propuesta.
- Mover el cursor y hacer clic es entrada sintética (`SendInput`). Va bien con la app del foco, que ya está en primer plano. Si se usa a la vez que el ratón real, el cursor salta, así que debe poder desactivarse.
- Precedente de la capa 3: *Hunt and Peck* (Windows, código abierto) etiqueta con UI Automation todo lo que se puede pulsar en la ventana activa.
- **Spike S9:**
  - Precisión del toque con la miniatura y el zoom en la tablet.
  - Scroll en Edge.
  - Tiempo de lectura de controles con UI Automation en las apps reales (Edge, Slack, SSMS, VS Code).
