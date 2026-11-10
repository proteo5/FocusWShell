# 04 — Riesgos y spikes

Antes de construir escenas y UI hay que comprobar que la base técnica aguanta. Cada riesgo tiene un spike: un experimento corto, con un resultado observable que lo confirma o lo descarta.

## Riesgos

| # | Riesgo | Impacto | Probabilidad | Mitigación |
| --- | --- | --- | --- | --- |
| R1 | Huecos o solapes entre ventanas por los bordes invisibles y el DPI mixto | Alto: el layout se ve roto | Alta | Medir con `DWMWA_EXTENDED_FRAME_BOUNDS`, compensar por ventana y verificar tras mover. Comprobar que cada ejecutable lleva el manifiesto PerMonitorV2 (ver S2 con dos monitores). |
| R2 | Una app se redimensiona por su cuenta después de moverla (`WM_DPICHANGED`, tamaño mínimo, Electron) | **Alto** (confirmado en S2: invade el Centro) | **Alta** | Verificar el rectángulo final; aprender el tamaño mínimo de cada app y que el layout lo respete (ver resultados de S2). |
| R3 | Parpadeo visible al aplicar una escena con muchas ventanas | Medio: rompe la sensación de continuidad | Media | Movimiento paralelo con `SWP_ASYNCWINDOWPOS` (S3: ~13 ms para 6 apps; `DeferWindowPos` no agrupa entre procesos), primero minimizar y luego mover. |
| R4 | El gestor confunde sus propios movimientos con los del usuario | Alto: bucles o ventanas que "se escapan" del layout | Alta si no se diseña | Reconciliar el estado final con lo esperado tras 120 ms sin eventos ([ADR 0007](adr/0007-cambios-externos-por-reconciliacion.md)). Validado en S4: 0 falsos positivos. |
| R5 | Ventanas elevadas o protegidas que no se pueden mover | Bajo si se detecta | Segura | Detectar y mostrarlas como "no gestionables"; nunca elevar el gestor. |
| R6 | Perder la geometría original tras una caída | Alto: rompe la confianza | Baja con diario | Diario escrito antes de mover ([ADR 0005](adr/0005-diario-de-recuperacion.md)). |
| R7 | Reconexión de monitores: eventos en ráfaga, `HMONITOR` nuevos, ventanas fuera de pantalla | Alto | Alta | Identidad por ruta de dispositivo, *debounce*, layouts por huella. |
| R8 | Minimizar en la pila produce animaciones molestas o apps que pausan contenido | Medio | Media | Probar con apps reales; alternativa: dejar la ventana restaurada detrás de la activa (sin minimizar). |
| R9 | La tira de pestañas (overlay) queda tapada o desalineada | Medio | Media | Franja reservada sin solape; reposicionar tras cada plan y tras cambios de DPI. |
| R10 | El modelo de roles no mejora lo que ya dan FancyZones + DisplayFusion | Alto: el proyecto no se justifica | Desconocida | Validación diaria temprana ([06](06-validacion.md)) antes de invertir en UI. |
| R11 | Monitores remotos (tablets por wifi) que se desconectan a menudo; Windows mueve sus ventanas al principal, encima del Centro | Alto: rompe el foco justo en el monitor que importa | Media | Reaccionar al cambio de monitores antes de que se note: reubicar en paneles del principal, nunca en el Centro ([ADR 0008](adr/0008-centro-solo-en-monitor-principal.md), [0009](adr/0009-tablets-como-monitores-auxiliares.md)). Probar S6 con una tablet. |
| R12 | El toque en el deck, desde una tablet por streaming o un monitor táctil, le quita el foco a la app del Centro | Alto: el deck interrumpiría justo lo que debe proteger | Desconocida | Ventanas del deck con `WS_EX_NOACTIVATE`; medir en S8 antes de construir el deck en otro monitor ([ADR 0013](adr/0013-deck-superficie-de-control.md)). |

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
| S8 — Toque sin robar el foco | ¿Un toque en una ventana propia con `WS_EX_NOACTIVATE`, en una tablet (Apollo/Artemis) o en un monitor táctil físico, llega como toque o como clic, y deja el foco en la app del Centro? | Ventana de prueba con botones en la tablet y en un monitor táctil; tocar 50 veces mientras se escribe en la app del Centro. | El toque se registra siempre; la app del Centro conserva el foco y el teclado; respuesta percibida < 100 ms. | R12 |
| S9 — Apuntar desde el deck | ¿Se puede apuntar con precisión en la app del foco desde el deck? | Miniatura DWM en vivo con cuadrícula y zoom en la tablet; scroll por franja lateral en Edge; lectura de controles con UI Automation en Edge, Slack, SSMS y VS Code. | El toque cae a ±1 control del objetivo con zoom; el scroll mueve la zona tocada; lectura de controles < 300 ms en apps nativas y < 1 s en Chromium. | — |
| S10 — Miniaturas | ¿Se pueden mostrar miniaturas en vivo de las ventanas de un panel en la lista ([ADR 0015](adr/0015-tiles-adaptables-y-lista.md))? | `DwmRegisterThumbnail` de una ventana minimizada y de una tapada por otra ventana propia; 10–20 miniaturas a la vez; `PrintWindow` como alternativa en Edge, Slack y VS Code. | La tapada se ve en vivo; 20 miniaturas sin coste perceptible; alternar entre lista y app sin parpadeo. | — · **Hecho** (opción B confirmada) |

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

### S5 — Diario y caída (2026-11-09, sólo ultrawide)

- **Preparación:** `tools/FocusWShell.TestWindows` abre ventanas de prueba en un estado controlado. Proceso A: `normal maximized minimized`; proceso B: `normal`. A eso se suma Docker Desktop, una app Electron real que estaba minimizada.
- **Prueba:**
  1. `focus-probe place --context docker --center testwindows --work testwindows,testwindows,testwindows --gap 8 --pause-after 4`: el probe captura las 5 ventanas en el diario, mueve 4 y se detiene.
  2. `taskkill /F` del probe a mitad de la escena: cuatro ventanas movidas y una sin tocar.
  3. `taskkill /F` del proceso B, para simular una app que se cierra mientras el gestor está caído.
  4. `focus-probe restore` desde un proceso nuevo.
- **Resultado:**
  - Tras la caída, el diario tenía las 5 ventanas con su estado **original**: `ShowCmd` 2 (minimizada), 3 (maximizada) y 1 (normal).
  - Restauración: 4/4 ventanas vivas volvieron exactamente a su estado. La maximizada volvió maximizada y las dos minimizadas (Docker y la de prueba) volvieron minimizadas. La que no se llegó a mover quedó intacta.
  - La ventana del proceso cerrado se detectó (handle inválido o de otro proceso), se descartó sin error y el diario se borró.
- **Hallazgos:**
  - Escribir el diario **antes** de mover es suficiente: una caída en cualquier punto deja un diario completo y correcto.
  - Restaurar sobre una ventana que no se movió no tiene efectos visibles, así que no hace falta registrar cuáles se llegaron a mover.
  - Docker Desktop (Electron) también tiene un ancho mínimo, de 942 px, y en Contexto se desbordó 326 px. Confirma el hallazgo de S2 sobre los tamaños mínimos.
  - `SetWindowPlacement` con `SW_SHOWMAXIMIZED` activa la ventana: no hay variante sin activación. Restaurar una ventana maximizada puede cambiar el foco; se acepta para la restauración de emergencia.
  - Si una ventana sigue viva pero no se puede restaurar (por ejemplo, la app no responde), su entrada se conserva en el diario para reintentar.
- **Decisión:** S5 aprobado. El ADR 0005 pasa a **Aceptada**.

### Tamaños mínimos (2026-11-09, sólo ultrawide)

Implementa la decisión que salió de S2. Ver [02 — Modelo de dominio](02-modelo-de-dominio.md#tamaños-mínimos).

- **Escena de S2 repetida** (`--gap 8`), empezando con el catálogo vacío:
  - **1ª colocación:** se aprendieron Slack (670 px de ancho) y GitHub Desktop (962×661). El layout se recalculó en la misma ejecución: Contexto pasó a 670 px, Trabajo a 962 px y el Centro a 1792 px (52 %). **6/6 exactas y Centro protegido.**
  - **2ª colocación:** salió bien a la primera, sin nada nuevo que aprender. Trabajo sigue en tiles: GitHub Desktop recibe sus 661 px de alto y las otras dos filas se reparten el resto (357/358 px).
- **Desborde hacia fuera:** Docker Desktop (942 px) en Contexto. El Centro sólo podía ceder hasta su piso (1720 px, 50 %), así que Contexto quedó en 780 px y Docker se ancló a la izquierda con 162 px fuera de pantalla. **Exacto y Centro protegido.**
- **Restauración:** 6/6 y 4/4 exactas.
- **Pendiente:** con el segundo monitor conectado, el desborde hacia fuera podría caer en el monitor vecino. Resuelto el mismo día: ver "S1 y S2 con dos monitores".

### S4 — Eventos propios (2026-11-09)

Decisión de diseño en [ADR 0007](adr/0007-cambios-externos-por-reconciliacion.md): reconciliar el estado final de cada ventana en lugar de atribuir eventos.

- **Prueba:** `focus-probe watch <escena> --cycles 5`.
  - **Fase 1:** aplica la escena y la revierte 5 veces vigilando los eventos: son 11 aplicaciones propias y ninguna debe atribuirse al usuario.
  - **Fase 2:** mueve, minimiza, muestra y maximiza una ventana sin avisar al observador, como lo haría otra app. Cada cambio debe detectarse.
  - **Fase 3 (opcional):** con `--listen N`, escucha durante N segundos lo que haga el usuario.
- **Resultado en el ultrawide** (5 apps): 55 eventos propios, **0 falsos positivos**, **4/4** cambios externos detectados.
- **Resultado en el monitor al 125 %**, con las ventanas cruzando entre DPI en cada ciclo:
  - **Primer intento: 33 "falsos positivos", que no lo eran.** Las ventanas estaban de verdad fuera de sitio: ChatGPT quedó en 750×1275 en vez de 600×1020 (×1,25), y al volver al ultrawide en ×0,8.
  - **La causa:** el movimiento asíncrono se daba por llegado en cuanto la ventana alcanzaba el rectángulo pedido. Pero al cambiar de DPI la app recibe `WM_DPICHANGED` y se reescala *después*. En `place` no se notaba porque la segunda pasada lo corregía; en los ciclos de S3 no hay segunda pasada.
  - **Arreglo en el aplicador:** si una ventana cambia a un monitor con otro DPI, se espera a que lleve 100 ms quieta y después se verifica y corrige.
  - **Tras el arreglo:** 150 eventos propios, **0 falsos positivos**, **4/4** detectados.
- **Coste del arreglo:** una transición que cruza DPI tarda unos 240 ms (con una corrección por ventana) en lugar de 9 ms. Sin cruce de DPI no cambia nada (9 ms).
  - Mejora posible: pedir de antemano el tamaño ya escalado para que el reescalado de la app caiga en el destino.
- **No medido:**
  - Arrastres y redimensionados hechos con el ratón por una persona: la fase 3 está lista, pero nadie la ha ejecutado aún.
  - Escritorios virtuales (`CLOAKED`).
- **Decisión:** S4 aprobado. Riesgo R4 mitigado.

### Catálogo de apps (M1b, 2026-11-10)

- **Detección:**
  - Cada refresco registra las apps que ve en `apps.json`: clave, nombre, ejecutable, AUMID y último panel. Nunca títulos.
  - El nombre sale de la descripción del ejecutable. Si es genérica ("Electron", o el propio archivo), se usa el nombre del producto: así "Electron" pasa a "GitHub Desktop".
  - En este equipo detectó 10 apps sin configurar nada.
- **Abrir:**
  - Las apps empaquetadas (AUMID con `!`) se abren por `shell:AppsFolder`; el resto, por su ejecutable.
  - FocusWShell espera hasta 20 s la ventana. Prefiere una nueva y, pasados 3 s, acepta una que ya estuviera abierta y libre. Después la asigna al panel elegido; por defecto, el último en el que estuvo.
  - Probado con el Bloc de notas cerrado: "abierta en Trabajo", lanzado desde su ejecutable en `WindowsApps`.
- **Pendiente:**
  - Iconos.
  - Renombrar y ocultar apps desde la UI (el modelo ya lo permite).
  - Abrir apps que falten al aplicar una escena.

### Paneles como entidades (M1b, 2026-11-10)

Implementa la primera parte del [ADR 0011](adr/0011-paneles-y-catalogo.md).

- **Modelo:**
  - `Panel` (id, nombre, monitor, lado, presentación) con `CreatePanel`, `RenamePanel`, `MovePanel`, `SetPresentation` y `DeletePanel`. Borrar un panel deja sus ventanas libres, sin moverlas.
  - Cada ventana apunta a su panel; desaparecen `Home`, `SendToDisplay` y `SetTemplate`.
  - En el principal, de momento, un panel por columna.
  - 158 pruebas.
- **Persistencia:**
  - `panels.json` guarda los paneles cada vez que cambian y se carga al arrancar.
  - Las escenas guardan sus paneles; las del formato anterior se migran solas al cargarlas (`SavedScene.Upgrade`).
- **App:**
  - La columna Panel ("libre", "Al foco" o un panel) sustituye a Rol y Monitor.
  - Una sección Paneles permite renombrar, mover, cambiar la presentación, borrar y crear paneles.
- **Prueba de principio a fin con UI Automation:**
  - Se creó "Monitoreo" en el HP, se asignaron ventanas y se renombró Contexto como "Comunicación". Las tiras muestran el nombre nuevo al momento.
  - Tras reiniciar la app, los paneles seguían ahí.
  - La escena antigua "Código" se migró y se aplicó: 5/5 exactas.
- **Fallos encontrados y corregidos:**
  - Crear y renombrar un panel leían un `TextBox` desde una tarea en segundo plano. La excepción activó la restauración de emergencia, que funcionó como estaba diseñada: devolvió todas las ventanas.
  - Al refrescar la lista, WPF escribía un valor vacío en las filas antes de desconectarlas, y se generaban asignaciones a un panel "vacío".
  - Desde ahora, los errores no controlados se registran en `%LOCALAPPDATA%\FocusWShell\errors.log`.
- **Decisión tomada:** aplicar una escena **suma** sus paneles a los del usuario.
  - Un panel que ya existe conserva su nombre y su ubicación, y sólo toma la presentación de la escena.
  - Si la escena pone un panel nuevo en una columna del principal que ya está ocupada, sus ventanas van al panel que está ahí.
  - Una ventana cuyo panel no se puede resolver queda **libre**.
  - Probado: con "Comunicación" y "Monitoreo" creados a mano, aplicar "Código" los conservó (5/5).

### Gestos de cambio de ventana (M1b, 2026-11-10)

Medido en Windows 11 con `EVENT_SYSTEM_FOREGROUND`, registrando la clase de cada ventana que toma el primer plano y el estado de Alt y Win:

- **Windows 11 no emite `EVENT_SYSTEM_SWITCHSTART/END`** con Alt+Tab. El selector es una ventana `XamlExplorerHostIslandWindow`, la misma que la vista de tareas. Se distinguen por la tecla pulsada cuando aparece: Alt en Alt+Tab, Win en Win+Tab (o ninguna, si se abrió con el botón de la barra).
- Entre una ventana y otra aparecen ventanas de paso `ForegroundStaging`: se ignoran.
- Tras elegir, el selector vuelve a tomar el primer plano unos milisegundos (un eco). Sin descartarlo, la siguiente activación por programa parecía venir de la vista de tareas y movía la ventana: un fallo de CA7 que encontró la prueba. Se descarta el selector si aparece menos de 400 ms después de un gesto.
- La barra de tareas es `Shell_TrayWnd` (o `Shell_SecondaryTrayWnd`); su gesto sólo cuenta si la ventana se activa en menos de 2 s.
- Volver a la ventana que ya estaba (Esc en el selector) no cuenta como gesto.

Prueba con dos ventanas de Paint, una en Contexto y otra en Trabajo, desde otro proceso:

| Caso | Resultado |
| --- | --- |
| Activarlas por programa (`SetForegroundWindow`), incluso justo después de un Alt+Tab | Nada se mueve |
| Alt+Tab real (teclas inyectadas) a la del panel | Pasa al foco: «elegida con Alt+Tab» |
| Win+Tab, flecha y Enter | Pasa al foco: «elegida en la vista de tareas» |
| Alt+Tab con su casilla desactivada | Nada se mueve; el ajuste se guarda en `settings.json` |
| Clic en la ventana del panel (clic desactivado) | Queda activa en su panel; nada se mueve |
| Soltarla sobre la columna del foco | Pasa al foco |
| Soltarla sobre la columna de otro panel | Cambia de panel |
| Sacar la del foco y soltarla sobre otro panel | Vuelve del foco y cambia de panel |
| Moverla dentro de su propio panel | Vuelve a su sitio |

Pendiente: el clic real en un botón de la barra de tareas (no se simuló) y arrastres con el ratón de verdad.

### Dos paneles por columna (M1b, 2026-11-10)

Prueba con dos ventanas de Paint en el ultrawide, desde la interfaz y desde otro proceso:

| Caso | Resultado |
| --- | --- |
| Crear «Notas» en «Principal · izquierda del foco · abajo» | Se crea; `panels.json` guarda `"Slot": "Lower"` |
| Una ventana en Contexto y otra en Notas | Cada una ocupa media columna (675 px de alto), con su tira encima: «CONTEXTO» arriba y «NOTAS» a media altura |
| Soltar la de Notas sobre la banda de Contexto | Cambia a Contexto; las dos se reparten la columna como tiles |
| Soltarla en la mitad de abajo cuando Notas ya no tiene ventanas | Vuelve a su sitio: un panel vacío no ocupa sitio, la banda de Contexto es la columna entera |
| Mover Notas a «derecha del foco · abajo» | Su ventana pasa a la columna derecha; como Trabajo está vacío, la ocupa entera |
| Arrancar con un `panels.json` anterior (sin `Slot`) | Carga bien: todos los paneles quedan arriba |

### Vistas (M1b, 2026-11-10)

Prueba en el ultrawide (con el monitor auxiliar a la derecha), con una ventana de Paint en Contexto y otra en el foco:

| Caso | Resultado |
| --- | --- |
| Foco a la izquierda | Foco 0–1718 px, Trabajo (vacío) en medio y Contexto pegado al borde derecho. Como ese borde da al auxiliar, el foco cede más (piso compartido) para que Contexto quepa sin invadir el otro monitor |
| Foco a la derecha | Contexto al borde izquierdo, Trabajo en medio y el foco a la derecha |
| Ctrl+Alt+Mayús+V | Pasa a la siguiente vista; el selector de la ventana lo refleja |
| Reiniciar la app | Vuelve con la última vista (guardada en `panels.json`) |

### Proporciones (M1b, 2026-11-10)

Prueba en el ultrawide con una ventana de Paint en el foco y otra en Contexto (foco al centro):

| Caso | Resultado |
| --- | --- |
| Preset «Foco ancho» | Foco 2131 px. Contexto pedía el 12 % pero se queda en 690 px, el mínimo de Paint |
| Redimensionar la del foco 300 px por su borde derecho (desde otro proceso) | Separador: la vista queda en 20 · 44 · 36 %, Trabajo se ensancha y el selector muestra «Personalizadas» |
| Redimensionar la de Contexto por su borde izquierdo (el del monitor) | No es un separador: vuelve a su sitio |
| Reiniciar la app | Vuelve con las proporciones personalizadas (en `panels.json`, por vista) |
| Volver a «Normal» | Se borran las personalizadas de esa vista |

Pendiente: arrastrar el borde con el ratón de verdad (la prueba usó `SetWindowPos` desde otro proceso).

### Catálogo: escenas que abren apps, iconos, renombrar y ocultar (M1b, 2026-11-10)

| Caso | Resultado |
| --- | --- |
| Escena con Paint en Trabajo; Paint cerrado; aplicar la escena | Paint se abre (por su ejecutable en `WindowsApps`) y su ventana entra en Trabajo en unos segundos: «mspaint se abrió y está en Trabajo» |
| Iconos | Salen del ejecutable de cada app, también el de Paint (paquete de la Store) |
| Renombrar Paint en la lista | Se guarda en `apps.json` |
| Ocultar | Desaparece de la lista; con «Ver ocultas» vuelve, en gris y con «Mostrar» |

Al terminar se dejaron `scenes.json`, `apps.json`, `settings.json` y `panels.json` como estaban.

### Separador apilado y pantalla completa (M1b, 2026-11-10)

Dos ventanas de Paint, una en Contexto (arriba) y otra en Notas (abajo, a la izquierda del foco):

| Caso | Resultado |
| --- | --- |
| La de abajo, 350 px más alta por su borde de arriba | Separador: 37 % arriba y 63 % abajo; se guarda en `panels.json` |
| La de arriba, 600 px más alta por abajo | El reparto se lleva al límite: la de abajo conserva los 500 px que necesita Paint |
| Primera prueba, antes de la corrección | La de abajo quedaba más baja que su mínimo y se minimizaba; ahora el motor respeta el mínimo si caben las dos |
| Ctrl+Alt+Mayús+F con una en el foco | Se maximiza en el principal y desaparecen las tiras del principal; otra vez F vuelve al foco |
| Botón «Pantalla completa» con el monitor HP | Se maximiza en el auxiliar; la tira del principal sigue |
| Des-maximizarla desde otro proceso | Sale de pantalla completa y vuelve al foco |

### Tiles adaptables (M1b, 2026-11-10)

Cuatro Calculadoras en un panel «Monitoreo» de prueba en el monitor HP (1920 px a 120 DPI: 3 columnas de 640 px):

| Caso | Resultado |
| --- | --- |
| Las cuatro en Pequeño | Tres en la primera fila; la cuarta va a la fila 2. Calculadora pide 627 px de alto, así que cabe una sola fila: la cuarta queda oculta y la tira muestra «▲ 1–1 / 2 ▼» |
| ▼ en la tira | Se ve la fila 2 (la cuarta) y se ocultan las otras tres |
| Clic en la pestaña de una oculta de la fila 1 | El panel vuelve a la fila 1 sin cambiar el orden |
| «Tamaño: Grande» en el menú de la primera | Ocupa las tres columnas y las demás pasan a la fila 2 |

Encontrado aquí y ya corregido: las apps de la Store aparecían como «ApplicationFrameHost» en las pestañas y en la lista, porque la etiqueta salía del proceso, y como «Application Frame Host» en el catálogo. Ahora el nombre de una app empaquetada se pide al shell por su AUMID (`shell:AppsFolder`) y la lista y las etiquetas nuevas usan el nombre del catálogo. Los nombres automáticos ya guardados se corrigen solos al volver a ver la app, salvo los que el usuario renombró (`Renamed`). Comprobado con la Calculadora: «Calculator» en la lista, en la pestaña y en `apps.json`.

### S10 — Miniaturas (2026-11-10)

Herramienta: `tools/FocusWShell.ThumbSpike`. Fuentes: una ventana propia animada (WinForms) y una ventana de Edge con un reloj que cambia en cada fotograma. Cada miniatura DWM se capturó dos veces, con 900 ms de diferencia.

| Fuente | Visible | Tapada por una ventana propia opaca | Tapada por una semitransparente | Minimizada |
| --- | --- | --- | --- | --- |
| Propia (WinForms) | En vivo | En vivo | En vivo | Congelada (última imagen) |
| Edge (Chromium) | En vivo | En vivo | En vivo | Congelada (última imagen) |

- **Chromium sigue pintando tapado** por una ventana nuestra, al menos en los 3 s medidos. Se temía que su detección de oclusión lo parara; no lo hizo.
- **Minimizada no queda vacía:** DWM conserva la última imagen, pero no se actualiza.
- **`PrintWindow` (`PW_RENDERFULLCONTENT`):**
  - ventanas visibles o tapadas: imagen completa en 6–28 ms (la propia, el reloj de Edge, Chrome con Jira y Edge con GitHub);
  - ventanas minimizadas: sólo una barrita de 160×28, así que no sirve.
- **Coste:** con 20 miniaturas a la vez de 4 ventanas que cambian sin parar (el peor caso), `dwm` pasa de 0,26 % a 0,51 % de la CPU total (16 núcleos; contadores de rendimiento, 12 muestras por fase). El proceso anfitrión, 0,1 %.

**Conclusión:** la opción B del [ADR 0015](adr/0015-tiles-adaptables-y-lista.md) (las ventanas detrás de la lista, sin minimizar) da miniaturas en vivo con un coste despreciable. Las miniaturas de ventanas minimizadas (estacionadas, o en pestañas en otros paneles) se muestran congeladas, que basta para reconocerlas. Queda por confirmar en uso real que Chromium no deja de pintar tras más tiempo tapado.

### S6 — Reconexión (2026-11-10)

- **Identidad estable:** `QueryDisplayConfig` + `DisplayConfigGetDeviceInfo` dan, por cada monitor activo, su nombre GDI (para cruzarlo con `EnumDisplayMonitors`), su `monitorDevicePath` y su nombre comercial.
  - En este equipo: `SPT8928…UID256` (Sceptre O35) y `HPN3545…UID280` (HP 24fw).
  - El modelo y las escenas usan `DisplayInfo.Key`: la ruta del dispositivo o, si no hay, el nombre GDI.
- **Modelo:**
  - `DisplayRemoved` ya no reescribe el monitor de las ventanas: lo marca como ausente. Mientras falta, sus ventanas se colocan en los paneles de su mismo hogar en el principal.
  - `DisplayAdded` las devuelve a su monitor, con sus templates.
  - Una ventana enviada a un monitor ausente espera en el principal.
- **Detección:**
  - Una ventana oculta de nivel superior recibe `WM_DISPLAYCHANGE` y `WM_SETTINGCHANGE` (`SPI_SETWORKAREA`); una ventana sólo de mensajes no recibe estos avisos.
  - **Al primer aviso, el observador de S4 calla.** Windows mueve ventanas por su cuenta durante el cambio, y sin este silencio se tomarían por movimientos del usuario y quedarían fuera de layout.
  - Tras 1,5 s sin avisos, se reconcilian los monitores, se reaplica el layout y se avisa en la bandeja.
- **Prueba real:** desconexión y reconexión con `DisplaySwitch.exe /internal` y `/extend`, dos ciclos.
  - **Al desconectar el HP:** "Se desconectó HP 24fw…". Sus ventanas pasaron al panel de Trabajo del principal, sin tocar el Centro y sin quedar fuera de layout. 4/4 y 5/5 exactas.
  - **Al reconectarlo:** "Volvió HP 24fw…". Sus ventanas regresaron a él con su template (Pila en un ciclo y Tiles en el otro, como estaban).
- **No medido:**
  - Desconectar el cable físicamente, que puede generar más ráfagas de avisos.
  - Una tablet por wifi (R11).
  - Que desaparezca el monitor principal.
  - Suspender y reanudar el equipo.
- **Decisión:** S6 aprobado para monitores que se apagan y vuelven. Riesgo R7 mitigado; R11 queda pendiente de probar con una tablet.

### Escenas guardadas (2026-11-09)

- **Prueba:** desde la interfaz se guardaron dos escenas.
  - **"Código":** cinco ventanas, una de ellas en el monitor auxiliar, y Trabajo en pila.
  - **"Lectura":** Explorer en el Centro y Edge en Contexto.
- **Cambiar con atajos:**
  - `Ctrl+Alt+Mayús+1` montó "Código" entera: 5/5 exactas.
  - `Ctrl+Alt+Mayús+2` pasó a "Lectura": las tres ventanas que no son de esa escena volvieron a su sitio original.
- **Persistencia:** con la app cerrada y vuelta a abrir, "Código" se aplicó desde el selector: 5/5.
- **Privacidad:** `scenes.json` guarda el título de cada ventana como pista para reconocerla, por ejemplo el nombre de un chat o de una página. Es un archivo local en `%LOCALAPPDATA%`, fuera del repositorio, pero conviene saberlo.
- **Pendiente:**
  - Lanzar las apps que faltan.
  - Geometría por escena (pesos de columnas) y layouts por huella de monitores.
  - Opción de estacionar en lugar de liberar las ventanas que no son de la escena.

### Tiras de pestañas (2026-11-09)

- **Posición:** Contexto 670×28 y Trabajo 962×28 en el principal; 1920×35 en el auxiliar al 125 %. Las ventanas de los paneles empiezan justo debajo y el Centro sigue ocupando toda la altura.
- **Clics**, por UI Automation y con el ratón:

  | Acción | Resultado |
  | --- | --- |
  | Clic en una pestaña oculta de la pila | Pasa a ser la visible |
  | Clic en `↑ msedge` | Edge vuelve del Centro a su panel |
  | Clic en `⏸ explorer` | Se recupera |
  | Doble clic real en una pestaña | La lleva al Centro con el foco |
  | Restaurar todas | Cierra todas las tiras |

- **Dos fallos encontrados y corregidos:**
  - Las tiras salían negras: se mostraban sin `Show()`, así que WPF no las pintaba.
  - Un doble clic promovía la pestaña equivocada: el primer clic reordenaba la tira antes del segundo.
- **Pendiente:**
  - Probar las tiras en uso diario: legibilidad, y si 28 px son suficientes.
  - Tamaño táctil en los auxiliares (M3).
  - Arrastrar pestañas entre paneles.
  - Tira para una ventana en `Fill`, que ahora cubre todo el monitor.

### Estacionar y reaccionar al usuario (2026-11-09)

La app usa la detección de S4. Prueba de principio a fin: la escena se armó desde la interfaz, y cada cambio se hizo desde **otro proceso**, igual que si lo hiciera el usuario o la barra de tareas.

| Acción externa | Reacción del gestor |
| --- | --- |
| Minimizar GitHub Desktop | Queda **estacionada**; Explorer ocupa todo el panel de Trabajo |
| Abrirla otra vez (`SW_RESTORE`) | **Vuelve a su panel**, en su mismo puesto |
| Arrastrar Explorer 300 px | Queda **fuera de layout** y el gestor no la devuelve; GitHub ocupa su sitio |
| Botón "Reencajar" | Explorer vuelve al layout |
| Maximizar WhatsApp en el monitor auxiliar | Su panel pasa a **Fill** |
| Restaurar todas | 5/5; el gestor no reacciona a su propia restauración |

- **Pendiente:**
  - Probarlo con arrastres reales con el ratón y con la barra de tareas de Windows de verdad.
  - Estacionar desde la tira de pestañas del panel, que llega con la capa visual.

### Monitores auxiliares (2026-11-09)

Implementa el [ADR 0008](adr/0008-centro-solo-en-monitor-principal.md) con el monitor estándar al 125 % como auxiliar.

- **Tiles en el auxiliar:**
  - Principal Slack | Edge | Explorer y auxiliar WhatsApp | ChatGPT: **5/5 exactas**, Centro protegido, en un solo lote para los dos monitores.
  - Las dos ventanas que cruzaban de DPI necesitaron una corrección.
- **Fill:**
  - ChatGPT queda maximizado en el auxiliar y GitHub Desktop, minimizado. **5/5.**
  - El foco siguió en Edge: maximizar no lo robó.
  - Primer intento con 2 px de desviación, porque se midió durante la animación de maximizar. Ahora se mide cuando el rectángulo deja de cambiar.
- **S4 con la escena en los dos monitores y Fill:** 99 eventos propios, **0 falsos positivos**, 4/4 cambios externos detectados. Se añadió la expectativa "maximizada en este monitor" y el cambio `Unmaximized`.
- **App de principio a fin**, con UI Automation:
  - Asignar monitor y rol a cada ventana, y pasar el Trabajo del auxiliar a Fill desde la ventana principal.
  - Promover ChatGPT del auxiliar al Centro: sale de Fill, llega al Centro con el foco, y WhatsApp vuelve a verse en el auxiliar.
  - `Volver a la anterior` lo devuelve maximizado a su panel auxiliar.
  - Restaurar todas: 5/5.
- **Pendiente:**
  - Identidad estable de monitores y desconexión real de un auxiliar (S6).
  - Reaccionar a eventos del usuario en la app: maximizar en un auxiliar debería equivaler a pasar ese panel a Fill (llega con Estacionar/Restaurar).

### S1 y S2 con dos monitores (2026-11-09)

- **Monitores:** ultrawide 3440×1440 al 100 % y, a su derecha, un monitor estándar 1920×1080 al 125 % (área de trabajo 1920×1020, 120 DPI).
- **S1:** el filtro sigue dando 11 candidatas sin falsos positivos, con el DPI correcto de cada monitor.
- **Hallazgo 1: el manifiesto no se aplicaba.** Al pasar el estándar al 125 %, el probe lo veía como 1536×864 a 96 DPI. Windows lo estaba virtualizando: el proceso no era PerMonitorV2.
  - La causa: el manifiesto se asignaba en `Directory.Build.props`, donde `OutputType` aún no existe, así que los ejecutables llevaban el manifiesto por defecto de .NET.
  - Se movió a `Directory.Build.targets`. Con un solo monitor al 100 % no había forma de notarlo: es justo el riesgo R1.
  - La prueba hecha con el proceso virtualizado aprendió mínimos falsos (Explorer 658×845). Se borraron del catálogo.
- **Hallazgo 2: restaurar entre monitores con distinto DPI reescala la ventana.**
  - `SetWindowPlacement` dimensiona la ventana todavía en el monitor al 125 % y, al cruzar al de 100 %, la app se reescala: WhatsApp volvió con 734×663 en vez de 921×830, el factor 96/120.
  - Ahora la restauración hace una segunda pasada si la colocación no coincide.
  - Si ni así coincide, la entrada **se conserva** en el diario en lugar de descartarse, para no perder el original.
- **S2 con DPI mixto:** escena WhatsApp | Explorer | ChatGPT en el estándar al 125 %.
  - 3/3 exactas.
  - Los bordes invisibles miden 8 px a 125 % (7 px a 100 %) y se compensan solos, porque se miden por ventana.
  - El catálogo aprendió WhatsApp con 610 px físicos y lo guardó como 488 px lógicos; en el ultrawide se había medido 486. El catálogo independiente del DPI funciona.
  - Restauración al ultrawide: 3/3 exactas, con dos pasadas.
- **Borde compartido:**
  - Antes del cambio, WhatsApp se desbordaba 44 px sobre el ultrawide.
  - Con la regla nueva, Contexto pasa a 610 px y el Centro cede hasta 792 px (41 %) sin que nada cruce al otro monitor.
  - ChatGPT, en el borde derecho, que no limita con nada, sigue desbordando hacia fuera como antes.
- **Pendiente:**
  - S6 (reconexión).
  - Una escena que use los dos monitores a la vez (Contexto en el estándar), que llega con los layouts por huella de monitores (M1).

### S3 — Lote sin parpadeo (2026-11-09, sólo ultrawide)

- **Prueba:** la escena de S2 (6 apps reales), revertida a su geometría de partida y reaplicada 20 veces, con 800 ms entre transiciones. `focus-probe place … --mode seq|batch|async --cycles 20`. Se mide el tiempo entre el primer cambio visible y la llegada de la última ventana a su sitio.
- **Resultado:**

  | Modo | Cómo mueve | Aplicar (media / máx) | Revertir (media / máx) |
  | --- | --- | --- | --- |
  | `seq` | `SetWindowPos` ventana por ventana | 145 / 154 ms | 154 / 174 ms |
  | `batch` | Un lote `BeginDeferWindowPos`…`EndDeferWindowPos` | 146 / 161 ms | 149 / 168 ms |
  | `async` | `SetWindowPos` con `SWP_ASYNCWINDOWPOS` a todas y espera a que lleguen | **13 / 14 ms** | **13 / 16 ms** |

  En los tres modos las 6 ventanas quedaron exactas (±1 px), con el Centro protegido y sin correcciones posteriores. En `batch` no falló ningún lote.
- **Hallazgos:**
  - **`DeferWindowPos` no agrupa ventanas de otros procesos.** El lote tarda lo mismo que moverlas una a una: Windows entrega cada movimiento al hilo dueño de forma síncrona, uno detrás de otro. Unos 25 ms por app (~9 fotogramas a 60 Hz para 6 apps), así que la escena se ve llegar "en cascada".
  - **Con `SWP_ASYNCWINDOWPOS` el movimiento es paralelo:** la petición se deja en la cola de cada app y todas se mueven a la vez. La escena entera llega en menos de un fotograma.
  - El aplicador tiene que **verificar después**: espera a que el rectángulo de cada ventana coincida con el pedido. Si una ventana se queda 150 ms quieta en otro tamaño (porque la app impuso su mínimo), la da por llegada y deja que el catálogo de mínimos aprenda de ella.
  - Con ventanas de prueba que empezaban maximizada y minimizada, primero se normalizaron (`SW_SHOWNOACTIVATE`) y después llegaron en el mismo movimiento paralelo: 4/4 exactas, 9 ms de media. Normalizar es un paso visible aparte, aunque solo afecta a las ventanas que estaban así.
- **No medido:**
  - El repintado del **contenido** de cada app. Llegar al rectángulo no garantiza que la app haya terminado de dibujar.
  - El juicio visual con grabación de pantalla. Queda para que el usuario lo confirme con `--cycles`.
  - Una app que impone un mínimo nuevo en modo asíncrono: su espera de 150 ms está implementada pero no se ha probado con una app real.
- **Decisión:** el aplicador usa movimiento **asíncrono en paralelo con verificación**. `DeferWindowPos` queda descartado como mecanismo principal: no aporta nada con ventanas de otros procesos. S3 aprobado en tiempos; falta la confirmación visual.
