# ADR 0009 — Las tablets y otros equipos que extienden el escritorio son monitores auxiliares; el cliente propio queda fuera del producto

- **Estado:** Aceptada (la parte del cliente propio es una evaluación, no un compromiso)
- **Fecha:** 2026-11-09

## Contexto

Hay software que convierte una tablet u otro PC en un monitor secundario por wifi o USB y además aprovecha su pantalla táctil. Ejemplos: spacedesk, Duet Display, SuperDisplay, Splashtop Wired XDisplay, o Sunshine + Moonlight en modo escritorio extendido. El usuario quiere que FocusWShell los aproveche. También pregunta si conviene desarrollar un cliente de extensión propio.

En Windows, todas estas soluciones instalan un **driver de pantalla indirecto** (IddCx): para Windows, la tablet es un monitor más, con su resolución, su DPI y una ruta de dispositivo. FocusWShell no necesita saber cómo llegan los píxeles.

## Decisión

1. **Se tratan como monitores auxiliares normales** ([ADR 0008](0008-centro-solo-en-monitor-principal.md)): paneles de trabajo o contexto, nunca Centro.
2. **Requisitos que añaden:**
   - **Conexiones inestables.** Por wifi, el monitor aparece y desaparece varias veces al día, a veces con cortes de segundos. Hay que esperar a que la configuración se estabilice (*debounce*, S6). Lo que no puede pasar es que las ventanas del auxiliar caigan encima del Centro mientras tanto (ADR 0008, punto 4).
   - **Identidad.** Cada solución puede presentar el monitor con una ruta de dispositivo distinta, o cambiarla al reinstalarse. La identidad estable del monitor se valida también con estos drivers.
   - **Táctil.** Las tiras de pestañas y los controles del gestor en un monitor con digitalizador necesitan objetivos de al menos 40 px lógicos y gestos simples: tocar una pestaña para cambiar de ventana, tocar un botón para promoverla.
   - **Latencia.** El contenido llega comprimido y con retraso. El gestor no debe depender de animaciones ni de verificar el resultado visualmente en esos monitores.
3. **El cliente de extensión propio no forma parte de FocusWShell.** Si se hace, será un proyecto aparte. Construirlo implica:
   - Un **driver IddCx** (UMDF) que cree el monitor virtual. Para distribuirlo hay que firmarlo con un certificado EV y la firma de atestación de Microsoft: coste anual y un proceso de publicación de drivers.
   - **Captura y codificación** de baja latencia (swap chain del driver o Desktop Duplication, y H.264/HEVC por hardware).
   - **Transporte** por red local (UDP o WebRTC) con control de congestión.
   - **Clientes** para Android, iPadOS y Windows, y la **inyección del toque** de vuelta en Windows (`InjectSyntheticPointerInput`).

   Es un producto del tamaño de FocusWShell o mayor. Antes de plantearlo conviene ver si se puede apoyar en proyectos abiertos existentes: Sunshine + Moonlight ya resuelven el streaming y tienen clientes táctiles, y hay drivers IddCx de ejemplo con licencia abierta.

## Consecuencias

- FocusWShell funciona con cualquier solución de extensión que el usuario ya tenga, sin acoplarse a ninguna.
- Se añade el riesgo R11 (monitores remotos inestables) y una prueba de S6 con una tablet por wifi.
- La UI táctil de los paneles en auxiliares entra en la capa visual (M3), no en M1.
- Si un día existe el cliente propio, podría ofrecer algo que los genéricos no dan: anunciar a FocusWShell qué es, para que ese monitor reciba directamente su layout de paneles táctiles. Sería una mejora, no un requisito.
