# ADR 0009 — Las tablets y otros equipos que extienden el escritorio son monitores auxiliares; FocusWShell no incluye drivers ni clientes propios

- **Estado:** Aceptada
- **Fecha:** 2026-11-09

## Contexto

Hay software que convierte una tablet u otro PC en un monitor secundario por wifi o USB y además aprovecha su pantalla táctil. El usuario quiere que FocusWShell aproveche esos monitores. También se evaluó si convenía desarrollar un cliente de extensión propio.

En Windows, todas estas soluciones instalan un **driver de pantalla indirecto** (IddCx): para Windows, la tablet es un monitor más, con su resolución, su DPI y una ruta de dispositivo. FocusWShell no necesita saber cómo llegan los píxeles.

## Decisión

1. **Se tratan como monitores auxiliares normales** ([ADR 0008](0008-centro-solo-en-monitor-principal.md)): paneles de trabajo o contexto, nunca Centro.
2. **FocusWShell no desarrolla drivers de pantalla ni clientes de extensión.** Un cliente propio supone un driver firmado, captura y codificación de vídeo, transporte por red y apps para cada plataforma: es un producto aparte y ya hay buenas alternativas. La documentación para el usuario **sugiere** las existentes:

   | Opción | Licencia | Notas |
   | --- | --- | --- |
   | [Sunshine](https://github.com/LizardByte/Sunshine) (servidor en el PC) + [Moonlight](https://moonlight-stream.org/) (cliente en la tablet) | Código abierto (GPL-3.0) | Pensado para streaming de baja latencia; clientes para Android, iPadOS, Windows, macOS y Linux, con soporte táctil. Para usarlo como monitor extendido necesita un monitor virtual (driver de pantalla virtual o dummy plug). |
   | spacedesk | Gratuito, cerrado | Crea el monitor virtual por sí mismo; cliente para Android, iOS y Windows. |
   | Duet Display, SuperDisplay, Splashtop Wired XDisplay | Comerciales | Monitor extendido con táctil; algunos con lápiz. |

   FocusWShell no depende de ninguna y no se integra con su API.
3. **Requisitos que añaden:**
   - **Conexiones inestables.** Por wifi, el monitor aparece y desaparece varias veces al día. Hay que esperar a que la configuración se estabilice (*debounce*, S6) y que, mientras tanto, nada caiga encima del Centro (ADR 0008, punto 4).
   - **Identidad.** Cada solución puede presentar el monitor con una ruta de dispositivo distinta o cambiarla al reinstalarse. La identidad estable del monitor se valida también con estos drivers.
   - **Táctil.** Las tiras de pestañas y los controles del gestor en un monitor con digitalizador necesitan objetivos de al menos 40 px lógicos y gestos simples: tocar una pestaña para cambiar de ventana, tocar un botón para promoverla.
   - **Latencia.** El contenido llega comprimido y con retraso. El gestor no debe depender de animaciones en esos monitores.

## Consecuencias

- FocusWShell funciona con cualquier solución de extensión que el usuario ya tenga, sin acoplarse a ninguna.
- El riesgo R11 (monitores remotos inestables) se prueba en S6 con una tablet por wifi.
- La UI táctil de los paneles en auxiliares entra en la capa visual (M3).
