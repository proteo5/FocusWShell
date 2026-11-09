# 04 — Riesgos y spikes

Antes de construir escenas y UI hay que comprobar que la base técnica aguanta. Cada riesgo tiene un spike: un experimento corto, con un resultado observable que lo confirma o lo descarta.

## Riesgos

| # | Riesgo | Impacto | Probabilidad | Mitigación |
| --- | --- | --- | --- | --- |
| R1 | Huecos o solapes entre ventanas por los bordes invisibles y el DPI mixto | Alto: el layout se ve roto | Alta | Medir con `DWMWA_EXTENDED_FRAME_BOUNDS`, compensar por ventana y verificar tras mover. |
| R2 | Una app se redimensiona por su cuenta después de moverla (`WM_DPICHANGED`, tamaño mínimo, Electron) | **Alto** (confirmado en S2: invade el Centro) | **Alta** | Verificar el rectángulo final; aprender el tamaño mínimo de cada app y que el layout lo respete (ver resultados de S2). |
| R3 | Parpadeo visible al aplicar una escena con muchas ventanas | Medio: rompe la sensación de continuidad | Media | `DeferWindowPos`, orden de aplicación (primero minimizar, luego mover, luego mostrar), y medir. |
| R4 | El gestor confunde sus propios movimientos con los del usuario | Alto: bucles o ventanas que "se escapan" del layout | Alta si no se diseña | Registro de geometrías esperadas con plazo. |
| R5 | Ventanas elevadas o protegidas que no se pueden mover | Bajo si se detecta | Segura | Detectar y mostrarlas como "no gestionables"; nunca elevar el gestor. |
| R6 | Perder la geometría original tras una caída | Alto: rompe la confianza | Baja con diario | Diario escrito antes de mover ([ADR 0005](adr/0005-diario-de-recuperacion.md)). |
| R7 | Reconexión de monitores: eventos en ráfaga, `HMONITOR` nuevos, ventanas fuera de pantalla | Alto | Alta | Identidad por ruta de dispositivo, *debounce*, layouts por huella. |
| R8 | Minimizar en la pila produce animaciones molestas o apps que pausan contenido | Medio | Media | Probar con apps reales; alternativa: dejar la ventana restaurada detrás de la activa (sin minimizar). |
| R9 | La tira de pestañas (overlay) queda tapada o desalineada | Medio | Media | Franja reservada sin solape; reposicionar tras cada plan y tras cambios de DPI. |
| R10 | El modelo de roles no mejora lo que ya dan FancyZones + DisplayFusion | Alto: el proyecto no se justifica | Desconocida | Validación diaria temprana ([06](06-validacion.md)) antes de invertir en UI. |

## Spikes

Todos usan la app `tools/TestWindows` (ventanas maniquí) más un conjunto de apps reales: VS Code, un navegador Chromium, Windows Terminal, Explorador, Outlook o el cliente de correo, Teams o Slack, una app UWP (Calculadora) y una app de Office.

| Spike | Pregunta | Prueba | Éxito si… | Riesgos |
| --- | --- | --- | --- | --- |
| S1 — Enumeración | ¿El filtro de elegibilidad da la lista que un humano esperaría? | Volcar ventanas candidatas con su motivo de inclusión o exclusión. | No aparecen diálogos, barras ni ventanas fantasma; aparecen todas las apps reales, incluida la UWP. | — |
| S2 — Colocación precisa | ¿Se puede colocar cada app en un rectángulo exacto en ambos monitores? | Colocar las apps de prueba en tres regiones del ultrawide y una del estándar, con escalas distintas. | Bordes visibles a ±1 px del destino en todas las apps. | R1, R2 |
| S3 — Lote sin parpadeo | ¿Cuánto parpadeo produce aplicar una escena de 6–8 ventanas? | Aplicar y revertir la escena 20 veces; grabar la pantalla. | Transición percibida como un único cambio. | R3 |
| S4 — Eventos propios | ¿Se distinguen movimientos propios de los del usuario? | Aplicar layouts mientras se registran eventos; luego mover ventanas a mano. | Cero falsos positivos en movimientos propios; todos los manuales detectados. | R4 |
| S5 — Diario y caída | ¿Se recupera todo tras matar el proceso? | Aplicar escena, `taskkill /f`, arrancar de nuevo, restaurar. | Todas las ventanas vuelven a su `WINDOWPLACEMENT` original, incluidas las que estaban maximizadas o minimizadas. | R6 |
| S6 — Reconexión | ¿Qué pasa al desconectar y reconectar el monitor estándar? | Con escena aplicada, desconectar el cable, esperar y reconectar. | Ninguna ventana queda fuera de pantalla; al reconectar se reaplica el layout guardado. | R7 |
| S7 — Pila | ¿Minimizar funciona bien para las ventanas ocultas de la pila? | Alternar pestañas 50 veces entre 3–4 apps. | Sin pérdidas de estado; la alternancia se siente inmediata. | R8 |

S1–S5 forman el hito M0. S6 y S7 se pueden posponer a M1, pero deben resolverse antes de dar por bueno el MVP.

## Resultados

Cada spike deja una nota breve en esta sección: fecha, configuración de monitores, apps probadas, resultado y decisión.

### S1 — Enumeración (2026-11-09, primera pasada)

- **Herramienta:** `focus-probe list` (`tools/FocusWShell.Probe`). Sólo lee; no mueve ventanas.
- **Monitores:** sólo el ultrawide 3440×1440 al 100 % (96 DPI). El monitor estándar no estaba conectado o no tenía barra de tareas secundaria: no apareció `Shell_SecondaryTrayWnd`.
- **Resultado:** de 514 ventanas top-level, 38 visibles, 12 candidatas y 12 gestionables. Las 12 son apps reales: Zoom (reunión), Claude, Chrome, SSMS, WhatsApp, Slack, Edge, Explorador, ChatGPT, GitHub Desktop, NetBird y Docker Desktop (minimizada). Ningún falso positivo del sistema.
- **Exclusiones correctas:** barra de tareas y escritorio (`SystemClass`); 11 ventanas auxiliares de Zoom, CptHost y el WebView2 interno de WhatsApp (`ToolWindow`); la barra flotante de notas de Zoom (`TooSmall`); Configuración y superficies del shell en segundo plano (`Cloaked`).
- **AUMID:** se obtiene para Configuración (UWP), Chrome y Edge. Para apps empaquetadas que no lo publican en la ventana (WhatsApp) sale vacío; habrá que leerlo del proceso (`GetApplicationUserModelId`) cuando se necesite para reglas.
- **Dudas abiertas:**
  - NetBird (366×653) es candidata. Es una ventana de configuración de una app de bandeja; se deja como candidata y se resolverá con la lista de exclusión del usuario.
  - Faltan casos por ver en vivo: una ventana elevada (debería salir `NO-GESTIONABLE`), un juego o vídeo en pantalla completa (`Fullscreen`), una ventana en otro escritorio virtual (`Cloaked`) y el monitor estándar con escala distinta.
- **Decisión:** el filtro es suficiente para continuar con S2. Repetir S1 con los dos monitores y los casos pendientes.

### S2 — Colocación precisa (2026-11-09, sólo ultrawide)

- **Comando:** `focus-probe place --context slack,whatsapp --center msedge --work explorer,ssms,githubdesktop`, con `--gap 0` y luego `--gap 8`; después `focus-probe restore`.
- **Monitor:** ultrawide 3440×1440 al 100 %, área de trabajo 3440×1392. Sin escala mixta: la parte de DPI de S2 queda pendiente para cuando se conecte el segundo monitor.
- **Layout:** pesos 18/55/27 → Contexto 619 px, Centro 1892 px, Trabajo 929 px. Contexto con 2 filas y Trabajo con 3.

| App | Tecnología | Bordes invisibles (L,T,R,B) | Resultado |
| --- | --- | --- | --- |
| Edge | Chromium | 7,0,7,7 | Exacto al primer intento |
| WhatsApp | WinUI 3 | 7,0,7,7 | Exacto |
| Explorador | Win32 clásico | 7,0,7,7 | Exacto |
| SSMS | WPF / shell de Visual Studio | **5,0,5,5** | Exacto |
| Slack | Electron | 7,0,7,7 | **Mínimo de 670 px de ancho**: se pasa 51–54 px a la derecha, dentro del Centro |
| GitHub Desktop | Electron | 7,0,7,7 | **Mínimo de 962×661**: se pasa 33–38 px a la derecha y ~200 px por debajo del área de trabajo |

- **Restauración:** 6/6 ventanas volvieron exactamente a su `WINDOWPLACEMENT` original. El diario conservó la primera captura aunque se colocó dos veces, como exige el ADR 0005.
- **Hallazgos:**
  1. La compensación de bordes funciona, y los márgenes **varían según la app** (SSMS usa 5 px, no 7). Hay que medirlos por ventana, como ya preveía la arquitectura.
  2. **Los tamaños mínimos de las apps son el problema real**, no la precisión. Una app que no cabe en su región se desborda, y en el caso de Slack invade el Centro. Eso rompe la regla del centro protegido.
  3. No hay una API para preguntar a otra app su tamaño mínimo (`WM_GETMINMAXINFO` no cruza procesos). La única forma fiable es **medirlo**: pedir un tamaño y ver qué acepta la app.
- **Decisión:** S2 aprobado en precisión y restauración. Antes de M1, el motor de layout tiene que tratar los mínimos:
  - recordar el tamaño mínimo observado por app (ejecutable o AUMID);
  - un panel donde sus ventanas no caben en tiles pasa a mostrarlas como pila;
  - si una ventana sigue sin caber, se ancla al borde exterior del monitor para que el exceso nunca invada el Centro.
  
  Se recoge en [02 — Modelo de dominio](02-modelo-de-dominio.md#tamaños-mínimos).
