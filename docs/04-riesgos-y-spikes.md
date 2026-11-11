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
| R12 | El toque en el deck, desde una tablet por streaming o un monitor táctil, le quita el foco a la app del Centro | Alto: el deck interrumpiría justo lo que debe proteger | Desconocida | Ventanas del deck con `WS_EX_NOACTIVATE`; medir en S8 antes de construir el deck en otro monitor ([ADR 0013](adr/0013-deck-superficie-de-control.md)). **Descartado por S8** (2026-11-10). |

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
| S7 — Pila | ¿Minimizar funciona bien para las ventanas ocultas de la pila? | Alternar pestañas 50 veces entre 3–4 apps. | Sin pérdidas de estado; la alternancia se siente inmediata. | R8 · **Hecho** |
| S8 — Toque sin robar el foco | ¿Un toque en una ventana propia con `WS_EX_NOACTIVATE`, en una tablet (Apollo/Artemis) o en un monitor táctil físico, llega como toque o como clic, y deja el foco en la app del Centro? | Ventana de prueba con botones en la tablet y en un monitor táctil; tocar 50 veces mientras se escribe en la app del Centro. | El toque se registra siempre; la app del Centro conserva el foco y el teclado; respuesta percibida < 100 ms. | R12 · **Hecho** (toques inyectados y tablet con Artemis) |
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

### Lista tipo Explorador (M1b, 2026-11-10)

Panel de prueba «Monitoreo» en lista, en el monitor HP, con dos Paint y una Calculadora:

| Caso | Resultado |
| --- | --- |
| Iconos grandes | Tres tarjetas con miniatura en vivo de las ventanas que hay detrás, y los nombres del catálogo («Calculator», «Paint») |
| Clic en la tarjeta de Calculator | Pasa al foco; en la lista queda con «↑» |
| Detalles y Lista | Nombre, título y estado; o icono y nombre en columnas. El modo se guarda en `panels.json` |
| Cerrar la Calculadora | Sale enseguida como cerrada, con su icono apagado |
| Tocar la Calculadora cerrada | Se abre y entra en «Monitoreo» |

Encontrado al probar:
- **La lista no veía cerrada una app recién cerrada**, porque la foto de ventanas abiertas era la anterior al cierre. Corregido: al detectar el cierre, la ventana sale de la foto.
- **Un clic de prueba caía fuera de su tarjeta.** El fallo estaba en el script de prueba, no en la app: se declaraba sólo «system-aware» y Windows reescalaba sus coordenadas en el monitor de 120 DPI. Las pruebas con ratón en monitores de otro DPI deben declararse per-monitor (`SetProcessDpiAwarenessContext(-4)`).

### Lista como lanzador y ventanas activadas detrás (M1b, 2026-11-10)

Panel de prueba «Monitoreo» en lista, en el monitor HP:

| Caso | Resultado |
| --- | --- |
| Sin ninguna ventana, al arrancar la app | La lista se ve, con su nombre y un texto que explica cómo llenarla |
| Abrir la Calculadora en «Monitoreo» desde el catálogo | Aparece en la lista con su miniatura en vivo |
| Activarla por programa (sin gesto) | Se pone encima de la lista y se ve entera |
| Activar después otra ventana | La lista vuelve a quedar encima, con la miniatura de la Calculadora |
| Cerrar la Calculadora | La lista sigue, con la Calculadora apagada: tocarla la abre en el panel |

Encontrado aquí, fallo previo: **el diario de recuperación no olvidaba nada**. El efecto `ForgetOriginal` (borrar un panel deja sus ventanas libres) no se ejecutaba, y una ventana gestionada que se cerraba seguía en el diario. Tras un cierre no limpio, la app habría ofrecido "restaurar" ventanas que ya no existen o que debían quedarse donde estaban. Corregido: `ForgetOriginal` quita la entrada del diario y `WindowClosed` lo emite. Comprobado: con la Calculadora abierta en Trabajo figura en el diario y, al cerrarla, el diario queda vacío.

### S7 — Pila (2026-11-10)

Panel Trabajo en pestañas con dos Paint y una Calculadora. Se alternó 50 veces con las pestañas de la tira (UI Automation) y se midió desde el clic hasta que la ventana elegida deja de estar minimizada. Animación de minimizar de Windows activada; tiempo de doble clic, 500 ms.

| Versión | Cambios | Latencia (mediana · p95 · máx.) |
| --- | --- | --- |
| Antes: la tira esperaba al tiempo de doble clic | 50 de 50 | 512 · 521 · 553 ms |
| Después: el clic actúa al momento | 50 de 50 | 18 · 24 · 36 ms |

- **Sin pérdidas de estado:** las tres ventanas vivas, con su título, y exactamente una visible al terminar.
- **Minimizar no era el problema; la espera de doble clic sí.** Existía porque la tira se reordenaba al cambiar de pestaña y el segundo clic caía en otra. Ahora la tira mantiene el orden de sus pestañas, el clic actúa al momento, y un segundo clic en la misma pestaña dentro del tiempo de doble clic la lleva al foco. Comprobado con el ratón de verdad.
- **Conclusión:** se confirma el [ADR 0006](adr/0006-ocultar-con-minimizar.md) (ocultar minimizando).

### Atajos configurables (M1, 2026-11-10)

| Caso | Resultado |
| --- | --- |
| Abrir «Atajos…» y pulsar Ctrl+Alt+Mayús+J sobre «Al foco» | El cuadro muestra «Ctrl+Alt+Mayús+J»; al guardar, `settings.json` lo guarda y la cabecera lo muestra |
| Ctrl+Alt+Mayús+↑ con una ventana de Trabajo activa | Ya no hace nada |
| Ctrl+Alt+Mayús+J | La lleva al foco |
| «Por defecto» y guardar | Vuelve a Ctrl+Alt+Mayús+↑; el cambio desaparece de `settings.json` |

### S8 — Toque sin robar el foco (2026-11-10)

Herramienta: `tools/FocusWShell.TouchSpike`. Una ventana tipo deck en WPF (la tecnología del deck), con `WS_EX_NOACTIVATE`, siempre encima, fuera de la barra de tareas, y seis botones grandes. Una Paint propia hace de "app del Centro", en primer plano. Los toques se inyectan con `InjectTouchInput`: Windows los trata como toques de verdad, con la misma pila de entrada que un dedo.

| Entrada | Registradas | El primer plano dejó Paint | Paint perdió el foco de teclado | Latencia hasta el `Click` (mediana · p95 · máx.) |
| --- | --- | --- | --- | --- |
| 50 toques inyectados | 50 | 0 veces | 0 veces | 5 · 24 · 92 ms, desde que se levanta el dedo |
| 20 clics de ratón (`SendInput`) | 20 | 0 veces | 0 veces | 35 · 39 · 53 ms, que incluyen los 30 ms entre pulsar y soltar |

- WPF recibe el toque como `StylusDown(touch)`, `TouchDown` y `Click`. El deck puede reaccionar al tocar (`TouchDown`) o al soltar (`Click`).
- **Conclusión, de momento:** en este equipo, tocar o hacer clic en una ventana WPF sin foco no le quita el foco a la app del Centro, y la respuesta queda muy por debajo de los 100 ms.
**Con la tablet (Apollo 0.4.6 + Artemis 20.2.6):**

- **Montaje:**
  - Apollo instalado en el PC; detecta los codificadores AMF de la Radeon 780M.
  - Para que la tablet **extienda** el escritorio en vez de duplicar el principal, la app «Desktop» necesita «Always use Virtual Display». En «Audio/Video → Advanced display device options → Device configuration» va «Verify that display is enabled», para que el monitor físico siga siendo el principal.
  - Windows ve la tablet como un monitor más (`DISPLAY6`, 1280×720).
  - Apollo crea un dispositivo táctil virtual, así que Artemis manda toques de verdad, no clics de ratón.
  - El aviso de Apollo «ViGEmBus is not installed» sólo afecta a los mandos de juego.
- **Prueba:** `touch-spike manual x y ancho alto` puso el deck en la pantalla de la tablet. El usuario tocó sus botones desde la tablet mientras escribía en el PC.

| Toques | Llegaron como | El deck tomó el primer plano | Primer plano durante la prueba |
| --- | --- | --- | --- |
| 17 | toque (`StylusDown(touch)`, `TouchDown`, `Click`) | 0 veces | Claude (6 toques) y Notepad++ (11), sin cambios |

- El botón reacciona al tocar, y el `Click` llega de 20 a 60 ms después, al levantar el dedo.
- **Conclusión:** R12 queda descartado con la tablet por streaming. El deck puede ser una ventana WPF sin foco, como las tiras, y reaccionar en `TouchDown` para que se sienta inmediato.
- **Falta:** un monitor táctil físico. Es opcional, porque usa la misma pila de entrada que los toques inyectados, que también pasaron.

### Deck, primera versión (M1b, 2026-11-10)

| Caso | Resultado |
| --- | --- |
| Ctrl+Alt+Mayús+D | El deck aparece en la esquina inferior derecha del ultrawide: el foco, las vistas, Contexto («sin apps»), Trabajo con una Paint, las ventanas libres con sus iconos, las escenas del usuario y los botones fijos |
| Tocar la Paint de Trabajo | Pasa al foco y el deck se oculta solo; al volver a abrirlo, el foco muestra «Paint» |
| «Izquierda» y luego «Centro» | La vista cambia y la ventana principal lo refleja |
| «Foco al panel» | La Paint vuelve a Trabajo |
| ✕ | El deck se oculta |

Que tocarlo no robe el foco ya lo confirmó el S8 con la misma técnica de ventana.

### Deck en la tablet (M1b, 2026-11-10)

Tablet por Apollo/Artemis como monitor extendido («Lenovo tablet», 1280×720):

| Caso | Resultado |
| --- | --- |
| 📌 → «Lenovo tablet: entero» | El deck ocupa el área de trabajo de la tablet y el ajuste se guarda en `settings.json` |
| Tocar una Paint libre en el deck fijado | Pasa a Trabajo y al foco; el deck sigue a la vista en la tablet |
| Panel «Tableta» en la tablet, con una Calculadora; 📌 → «mitad izquierda» | El deck queda a la izquierda; el panel, con su tira, a la derecha, sin taparse. «Entero» sale como no disponible («tiene paneles») |

| Arrastrar con el ratón una Paint de Trabajo a Contexto, y una Paint libre a Trabajo | Se mueve la primera; la segunda queda asignada a Trabajo |
| Botón de presentación de Trabajo → «Pestañas», luego ▶ | Trabajo pasa a pestañas; ▶ pone la Calculadora como pestaña visible |
| Preset «Foco ancho» | La vista queda en 12 · 70 · 18 % |
| Panel «Tableta» en la tablet, en Pantalla completa; deck fijado en la mitad izquierda; arrastrar la Calculadora a Tableta | La Calculadora ocupa la mitad derecha sin maximizarse, con su tira «TABLETA \| Calculator» |

Sin probar: desconectar y reconectar la tablet con el deck fijado.

### Pantalla completa propia de las apps (M2, 2026-11-10)

Regla (`Core.Windows.AppFullscreen`): una ventana está en pantalla completa propia si cubre su monitor entero, barra de tareas incluida (±2 px), sin estar maximizada. Una maximizada en un monitor sin barra también lo cubre, pero no cuenta. Hay dos vías:

- **El vigilante** (`AppFullscreenWatcher`): mira la ventana en primer plano cada 500 ms y avisa sólo cuando cambia. Mientras dure la pantalla completa, ese monitor queda **en pausa**: sin tiras, sin listas y sin deck encima.
- **Al reaccionar a un cambio externo:** si una ventana gestionada se movió o cambió de tamaño hasta cubrir su monitor, no se le devuelve a su sitio (`SnapBack`), sino que se suelta del layout (`Detach`). Al salir, el siguiente cambio la reencaja.

Probado con Edge (F11) en el panel Trabajo y el deck abierto en el principal:

| Momento | Resultado |
| --- | --- |
| F11 | Edge ocupa 3440×1440. El estado dice «se puso en pantalla completa». La tira de Trabajo y el deck desaparecen |
| «Actualizar lista» durante la pantalla completa | Edge sigue gestionada |
| F11 otra vez | Edge vuelve a su sitio en Trabajo («la moviste; vuelve a su sitio»). La tira y el deck vuelven |

Encontrado aquí y ya corregido:

- **Una gestionada en pantalla completa se daba por cerrada.** Al refrescar, una ventana que dejaba de ser «elegible» (en pantalla completa Edge cambia de estilo) se trataba como cerrada y se olvidaba. Ahora sólo se da por cerrada la que ya no existe.
- **La lista no la mostraba.** Ahora una gestionada viva que no es elegible sigue en la lista, con su última foto.

Pendiente: un juego en pantalla completa exclusiva y una presentación de PowerPoint.

### Deck: mover apps con toques (validación, 2026-11-11)

Encontrado en uso con la tablet: con el ratón, arrastrar una app a otro panel funciona; con el dedo, el deck desplaza la lista en lugar de arrastrar. El `ScrollViewer` del deck (`PanningMode.VerticalOnly`) se queda el gesto, y WPF no convierte el toque en arrastre. El menú «Mover a» de cada app existe, pero se abre con clic derecho o pulsación larga, que con el dedo no es fiable.

Arreglo: un botón **«⇆ Mover una app»** junto a «Anterior» y «Foco al panel». Se mueve en dos toques:

1. Se toca la app. Queda resaltada y un aviso dice qué hacer.
2. Cada panel que no es el suyo muestra **«⬇ Mover aquí»**. Al tocarlo se mueve (`SendTo`, o `Adopt` si era libre).

«Cancelar», volver a tocar el botón u ocultar el deck salen del modo. Con el ratón, arrastrar sigue funcionando igual.

Probado en la tablet: funciona.

### Deck sin scroll (validación, 2026-11-11)

Encontrado en uso con la tablet: el scroll del deck estorbaba al tocar los botones. Además, todo iba en una columna a la izquierda: la mitad derecha de la tablet quedaba vacía, y las escenas, «Pantalla encendida» y «Restaurar» quedaban abajo, fuera de la vista.

Rediseño: el deck ya no tiene `ScrollViewer`; todo cabe en su espacio. Elige la disposición según su ancho:

- **Ancho ≥ 900 DIP** (la tablet entera, 1280×672):
  - a la izquierda, una columna de 320 con el foco, las acciones, la vista, el ancho, las escenas y, abajo, «Pantalla» y «Restaurar»;
  - a la derecha, los paneles en columnas (en dos filas si son más de tres) y las libres en una franja abajo.
- **Estrecho** (media tablet, 640×672; esquina del principal, 640×980): filas compactas.
  - Vista y ancho comparten una fila de seis.
  - Los paneles se reparten el alto según cuántas apps tienen.
  - Escenas, «Pantalla» y «Restaurar» van en la última fila.

Los botones van en rejillas que se estiran, sin anchos fijos. Las apps de cada panel van en una `FitGrid`:

- da a cada app la celda más grande que cabe (de 64×48 a 132 de ancho) y, a igualdad, más columnas;
- si ni con celdas mínimas caben, la última celda es «+N más», que abre las demás en un menú;
- el icono crece con la celda y el nombre se recorta con «…».

`FocusWShell.exe --deck-preview <carpeta>` dibuja el deck con datos de ejemplo en los tres tamaños, con pocas y con muchas apps, y guarda los PNG. No toca ventanas ni cierra la instancia abierta. Con él se revisó:

- la tablet entera, con 18 apps en Trabajo: caben todas en 4 columnas;
- la media tablet, con 18 apps: 15 a la vista y «+3 más»;
- la esquina: cabe todo.

Probado en la tablet: funciona. Después se añadió el icono de la app del foco a la izquierda de su nombre.

### Deck: una pestaña por monitor, y la ventana principal en pestañas (validación, 2026-11-11)

Encontrado en uso, al querer crear paneles en el monitor HP:

- **La ventana principal se quedaba sin alto.** Sus secciones se apilaban desde abajo (estado, apps conocidas, excluidas, paneles) y la lista de ventanas ocupaba lo que sobrara. A 620 px de alto, «Paneles» y la lista de ventanas quedaban a cero: no había forma de ver dónde se crea un panel. La sección «Excluidas» de M2 fue la que la desbordó.
  - Arreglo: las secciones van en pestañas (**Ventanas**, **Paneles**, **Apps**, **Excluidas**), cada una con todo el alto, y las barras de arriba pasan a `WrapPanel` (antes se cortaban a la derecha).
  - La pestaña Paneles explica cómo crear uno.
- **En el deck, con más monitores, los paneles se apretaban.** Ahora hay una pestaña por monitor («Principal · 6», «HP 24fw · 3»), con el número de apps abiertas en sus paneles. Sólo se ven los paneles del monitor elegido.
  - Ancho: van bajo las escenas. Estrecho: en una fila sobre los paneles.
  - El foco, las acciones, la vista, el ancho y las libres siempre se ven.
  - Sale cada monitor conectado, menos el que el deck ocupa entero. Uno desconectado con paneles sale apagado, «(desconectado)».
  - Un monitor sin paneles dice cómo crearlos.
  - La pestaña elegida se recuerda en la sesión; al principio, el principal.
  - «⇆ Mover» funciona entre pestañas: se toca la app, se cambia de pestaña y se toca «Mover aquí». Con el ratón, pasar una app arrastrada por una pestaña cambia a ese monitor para soltarla allí.

Revisado con `--deck-preview`, que ahora incluye un HP con dos paneles y la vista de cada pestaña.

### Deck: la lista se quedaba debajo, y el foco no iba a la app elegida (validación, 2026-11-11)

Encontrado en uso, siempre desde el deck:

- **Un panel en Lista parecía de pestañas** (con el deck aún diciendo «Lista») después de llevar apps al foco.
  - Causa: una ventana del panel en primer plano se deja ver sobre la lista, y la lista vuelve a taparla sólo cuando cambia el primer plano. El deck nunca se activa (S8) y la activación fallaba (abajo), así que una ventana de Trabajo se quedaba en primer plano y la lista debajo hasta otro cambio.
  - Reproducido: con B, del panel, en primer plano, la lista quedaba debajo de B.
  - Arreglo: tras cada cambio de layout la lista vuelve a quedar encima. «Dejarse ver» sólo dura mientras el usuario sigue en esa ventana. Comprobado: un cambio de vista desde el deck, sin cambiar el primer plano, la pone encima otra vez.
- **Al elegir en el deck la app del foco, el teclado y el ratón se quedaban donde estaban.**
  - Causa: `SetForegroundWindow` sólo funciona si Windows cree que FocusWShell recibió la última entrada. Con el deck, que no se activa, y el toque de la tablet por Apollo, a menudo no lo cree.
  - Arreglo: `WindowMover.Activate` reintenta enganchándose un momento a la cola de entrada del hilo que tiene el primer plano (`AttachThreadInput`), que sí puede cederlo. Además, al llevar una app al foco desde el deck (una app, «Anterior» o pantalla completa), el cursor va al centro de esa ventana.
  - Comprobado con toques simulados por UI Automation (que no dan derechos de entrada): la app elegida queda en primer plano y el cursor en el centro del foco.
- **Seguía pareciendo de pestañas: las ventanas del panel quedaban encima de la lista sin estar activas.**
  - Causa: en la copia del usuario, Outlook (de Trabajo) tapaba la lista con Claude en primer plano. Al colocar las ventanas, Windows trae alguna al frente (p. ej. al restaurar una minimizada) después de que la lista se pusiera encima.
  - Arreglo: tras cada layout, todas las ventanas de un panel en lista van al fondo del orden Z (`WindowMover.SendToBack`, `HWND_BOTTOM`). Se siguen viendo por sus miniaturas, y si el usuario activa una, Windows la trae al frente.
  - Primer intento: se dejaba fuera la ventana en primer plano, pensando en la que el usuario estuviera usando. Falló con el dedo: la app que vuelve del foco aún tiene el primer plano (el deck nunca se activa y nadie se lo quita), y quedaba encima. Las pruebas con UI Automation no lo veían, porque ese «toque» sí activa el deck. Por eso se reprodujo con clics reales del ratón sobre el deck en la tablet, que no lo activan, como el dedo.
  - Comprobado con clics reales, tres apps llevadas al foco una tras otra:
    - sin el arreglo, la que vuelve del foco (C, luego B) quedaba encima de la lista;
    - con él, la lista queda encima en todos los pasos, y la app elegida queda en primer plano.

### Sesión entre reinicios y cerrar apps desde el deck (validación, 2026-11-11)

**La sesión.** Los paneles se guardaban, pero qué app estaba en cuál no, y al abrir FocusWShell todas empezaban libres. Ahora la sesión es una escena automática en `session.json`, que no sale en la lista de escenas:

- **Al guardar:**
  - se guarda 3 s después de cada cambio de layout;
  - y al salir, antes de devolver las ventanas a su sitio. Así, «Salir» y volver a abrir las devuelve a sus paneles.
- **Al arrancar:**
  - vuelve a poner en su panel cada ventana de la sesión que esté abierta, con la vista de entonces;
  - no toca los paneles ni abre apps;
  - las que falten se adoptan si aparecen en los 3 minutos siguientes (cada 10 s): las apps que se abren al iniciar Windows suelen llegar después que FocusWShell;
  - sólo se adoptan ventanas libres: nada que el usuario ya haya puesto.
- **Emparejado estricto** (`SceneMatcher.Match(..., strict: true)`, con pruebas).
  - En las escenas, si no hay una ventana con el mismo título, se toma cualquiera de la misma app. Al arrancar eso es peligroso: probándolo, el sitio de una ventana de prueba cerrada se lo llevó otra ventana de Edge que no tenía nada que ver.
  - En la sesión, una ventana con otro título sólo se toma si ninguna de esa app casó por título y cuadran uno a uno. Es lo que pasa tras reiniciar Windows, cuando los títulos cambian.
- **Probado:**
  - Con A y B en Trabajo se salió con `--salir` y se cerró B.
  - Al volver a abrir FocusWShell, A volvió sola a Trabajo.
  - Al abrir B, se adoptó en Trabajo en segundos.
  - Las otras ventanas de Edge del usuario siguieron libres.

**Cerrar apps desde el deck.** Hay un botón «✕ Cerrar una app», junto a «Mover», que activa un modo:

- cada app que se toca se cierra como con su X (`WM_SYSCOMMAND` + `SC_CLOSE`); si tiene algo sin guardar, la app pregunta;
- primero se trae la app al frente, para que su pregunta (si tiene algo sin guardar) se vea;
- el modo se apaga solo al cerrar una, como «Mover». Al principio seguía activo para cerrar varias, y en uso parecía que el deck se había quedado parado: seguía en rojo sin que cambiara nada;
- la tarjeta dice «Cerrando…» al momento. Si la app sigue abierta a los 5 s (preguntó y se canceló), vuelve a ser normal;
- las apps a punto de cerrarse se marcan en rojo, y las cerradas no se pueden elegir.

Probado con clics reales sobre el deck: la ventana de prueba se cerró, el modo siguió activo y «Listo» lo apagó. El guion sólo podía tocar la tarjeta cuyo título fuera el de la ventana de prueba: una primera versión tomaba la primera tarjeta de Edge, que podía ser una ventana del usuario, y la comprobación de seguridad la bloqueó antes de ejecutarse.

**Modos más claros.** Los avisos de los modos (franjas con «Cancelar» y «Listo») ocupaban sitio y empujaban todo el deck hacia abajo al entrar y salir. Ahora:

- **Botones:** el mismo botón enciende y apaga el modo, y mientras está activo lo dice: «⇆ Moviendo…» (azul) o «✕ Cerrando…» (rojo). Encender uno apaga el otro.
- **Mover:**
  - Al elegir la app, sólo los paneles donde puede ir se ponen con borde amarillo y un velo «⬇ Mover aquí»; el suyo no.
  - Tocar en cualquier parte de ese panel la lleva ahí: un blanco mucho más grande que el botón que había en la cabecera.
- **Cerrar:** las apps abiertas, con borde rojo.
- **Liberar con el mismo gesto:**
  - Antes sólo se podía desde el menú de pulsación larga, poco fiable con el dedo.
  - Ahora LIBRES es un destino más: al mover una app que está en un panel, LIBRES se pone amarillo con «⬇ Liberar aquí», aunque esté vacío, y un toque la libera (vuelve a su sitio original).
  - Con el ratón, soltarla arrastrada ahí hace lo mismo.
  - Los textos de destino van en una etiqueta oscura, para que se lean encima de las tarjetas.
  - «Mover aquí» y «Liberar aquí» son botones. Por un tiempo fueron un velo que escuchaba el ratón: con el mouse y con toques inyectados (`InjectTouchInput`) funcionaba, pero con el toque de la tablet por Apollo no. En el deck, sólo los botones respondían siempre. Comprobado con toques inyectados: la app se movió.
  - LIBRES se dibuja siempre, aunque esté vacía («Sin ventanas libres»). Antes sólo salía si había ventanas libres. Con la sesión restaurada todas estaban en paneles, así que desaparecía, y parecía que se había perdido la función de mover.
  - Probado con clics reales: la ventana de prueba pasó de «En Trabajo» a libre.

Probado con clics reales sobre el deck en la tablet:

- «Mover» se enciende («Moviendo…») y se apaga con el mismo botón;
- con la app de Trabajo elegida, sólo Contexto queda marcado, y un toque en el panel la mueve y apaga el modo;
- «Cerrar» se enciende y se apaga igual.

**El deck se pone al día solo.** Una app recién abierta no salía entre las libres hasta otro cambio de layout. Ahora, mientras el deck está a la vista, cada 3 s se vuelven a leer las ventanas, y sólo se redibuja si cambió algo.

### Spike: escritorios virtuales de Windows (2026-11-11)

**Pregunta:** ¿qué tanto choca FocusWShell con los escritorios virtuales y se pueden aprovechar en lugar de las escenas propias?

**Método:** con FocusWShell abierto (deck en la tablet, tiras de Contexto y Trabajo, Claude en el foco), un script creó un escritorio con `Ctrl+Win+D`, observó a los 2,5 s y a los 5,5 s, lo cerró con `Ctrl+Win+F4` y comparó todas las ventanas visibles antes y después. Usó sólo la API documentada (`IVirtualDesktopManager`) y `DWMWA_CLOAKED`. Windows 11, compilación 26300.

**Resultados:**
- **El deck y las tiras sobreviven al cambio sin hacer nada.** Son ventanas `WS_EX_TOOLWINDOW` y Windows no las asigna a ningún escritorio (`GetWindowDesktopId` = GUID vacío), así que se ven en todos. La ventana principal sí pertenece a un escritorio y se oculta en los demás.
- **Se puede saber el escritorio actual con lo documentado:** una ventana propia recién creada nace en el escritorio actual, y `GetWindowDesktopId` la identifica. Coincidió siempre con el valor del registro (`CurrentVirtualDesktop`, no documentado). También se sabe el escritorio de cada ventana.
- **FocusWShell no reaccionó:** ninguna ventana se movió y ninguna gestionada se dio por cerrada, porque sólo se da por cerrada la que ya no existe. Al volver, todo estaba igual.
- **Pero en el escritorio nuevo el deck engaña:** muestra las apps del escritorio 1 y las tiras se dibujan sobre una pantalla vacía. El modelo es un solo espacio de trabajo para todos los escritorios.

**Prueba manual (el usuario):** mandó SmallNotePad, gestionada en el panel Trabajo, al escritorio 2 desde la Vista de tareas y volvió al 1.
- La app **siguió saliendo en el deck**, en su panel, aunque no estaba en ese escritorio.
- Al tocarla, pareció minimizarse; con otro toque apareció una SmallNotePad en el foco.
- **Lo que pasó en realidad:** se abrió una **segunda instancia** (proceso nuevo a las 15:16:03). La original siguió oculta en el escritorio 2. FocusWShell, al no ver la ventana original como candidata (está *cloaked*), actuó como si la app estuviera cerrada y la abrió. Con una app que no admite varias instancias el resultado sería otro, pero en ningún caso es lo esperado.

**Riesgo:** hoy, mover una app gestionada a otro escritorio deja al deck con información falsa y puede duplicar apps. Mientras no haya soporte de escritorios, conviene no usarlos con FocusWShell.

**Conclusión:** los escritorios son viables como reemplazo de las escenas sin API no documentada, si FocusWShell **los sigue en vez de manejarlos**: un espacio de trabajo por escritorio, que el deck muestra según el escritorio actual. El usuario cambia de escritorio con los gestos de Windows.

### Deck: la presentación del panel, en segmentos (validación, 2026-11-11)

La cabecera de cada panel tenía un solo botón que mostraba la presentación actual («Tiles») pero, al tocarlo, pasaba a la siguiente: lo que se leía no era lo que se obtenía, y para llegar a «Lista» había que tocar varias veces sabiendo el orden. Además medía 40×36, chico para el dedo en una tablet de 8 pulgadas.

- **Ahora son botones pegados, uno por presentación,** con la activa en azul. Se ven todas y un toque basta para cualquiera. Los paneles del principal tienen 3 (Tiles, Pestañas, Lista); los de otros monitores suman Pantalla completa.
- **Con iconos** (▦ Tiles, ⧉ Pestañas, ☰ Lista, ⛶ Pantalla completa) y el nombre en el tooltip. Con texto no cabrían cuatro junto al nombre del panel.
- **Todos los botones de la cabecera miden 48×48,** el tamaño táctil del resto del deck, incluidos ◀ ▶ de las pestañas. La cabecera crece 12 px, que sale del sitio que dejaron libre las apps cerradas sin anclar. Si falta ancho, se recorta el nombre del panel, nunca los botones.

### Deck: botones de modo frente a botones de acción (validación, 2026-11-11)

Mover, Anclar y Cerrar no actúan al tocarlos: se quedan encendidos esperando el siguiente toque, como Shift. Anterior, Al panel y Ratón actúan al momento. Antes, esa diferencia sólo se veía después de tocarlos. Ahora se ve de antemano con tres señales:

- **«…» al final del texto** («Mover…», «Anclar…», «Cerrar…»), la convención de Windows para «este botón te pedirá algo más». Encendidos, el texto ya acaba en «…» («Moviendo…»).
- **Una barra abajo en el color de su modo:** amarilla en Mover, índigo en Anclar y roja tenue en Cerrar. Sigue la regla de color del deck: es el color que tomarán las ventanas tocables en ese modo. Encendido, el botón se rellena con ese color y la barra desaparece.
- **Un hueco entre los dos grupos:** en el deck ancho, entre sus dos filas; en el estrecho, tras «Ratón» en la fila de seis.

No se usa una flechita (▾), porque promete un menú, ni un aspecto de interruptor, porque ocupa sitio que el deck no tiene.

### Deck: la disposición del principal, resumida (validación, 2026-11-11)

Los botones de «Vista» (dónde va el foco y su ancho) casi no se cambian, pero ocupaban dos filas fijas. Además, «Vista» no decía qué controla: no toca los paneles, sino dónde va el foco en el principal y qué ancho tiene.

- **Nuevo nombre:** «Disposición del principal» («Main screen layout»). En la documentación se sigue llamando «vista» (ADR 0012).
- **Resumen de un vistazo**, que se toca para cambiarla. Muestra el icono de la vista y el ancho (o «A medida» si no es un preset). En el deck ancho es una fila en la tarjeta Disposición («▣ Centro · Normal ⚙»), porque la cabecera del foco no tiene sitio sin recortar el nombre de la app. En el estrecho va en la cabecera («▣ Normal»).
- **Tocarlo abre los botones aquí mismo,** como Energía, y «✓ Listo» los cierra. No se usa una ventana aparte, porque el deck va siempre encima y no toma el foco. En el deck estrecho, mientras están abiertos, se ocultan las escenas para que todo quepa sin scroll.
- Las escenas ganan una fila en el deck ancho.

### Deck: el ratón al foco (validación, 2026-11-11)

Al tocar la tablet, Windows lleva el cursor al punto tocado, así que después de usar el deck el ratón queda en la tablet. «🖱 Ratón» (en la tarjeta Foco, junto a Anterior y Al panel) lo lleva al centro de la app del foco, que es donde se va a trabajar. Si no hay app en el foco, o está minimizada, lo lleva al centro del monitor principal. Sólo mueve el cursor: no cambia qué ventana está al frente.

### Deck: indicador de la ventana al frente (validación, 2026-11-11)

El usuario a veces hace clic con el ratón en apps que no están en el foco, sobre todo en las del segundo monitor, para hacer algo rápido sin cambiar el foco. Con el clic al foco desactivado, esa app recibe el teclado sin entrar en el foco. El deck seguía marcando sólo la del foco y no decía dónde iba lo que se escribía.

- **Sólo si la ventana al frente no es la del foco.** En el caso normal no se marca nada, así que el indicador, cuando sale, significa «el teclado no está en el foco».
- **Borde fino blanco** en esa app, sea de un panel o libre. Queda fuera de los colores de los modos (amarillo, rojo e índigo). Mientras hay un modo encendido mandan sus bordes y no se marca.
- **La pestaña de su monitor** lleva el mismo borde si la app está en otro monitor y esa pestaña no se está viendo.
- Si la ventana al frente no está en el deck (el escritorio, la barra de tareas, una excluida), no se marca nada. Tocar el deck no lo cambia, porque el deck no se pone al frente.
- **Al momento:** se usa el aviso de cambio de primer plano que ya tenían las listas. Sólo se redibuja si cambia qué se marca, para no perder un toque a mitad. El repaso de cada 3 s lee además la ventana al frente real, por si algún aviso no llega (p. ej., volver a la misma ventana desde Alt+Tab).

Probado con `--deck-preview`: una libre al frente, y una app del segundo monitor al frente con la pestaña marcada.

### Deck: volver a la app del foco cuando una libre flota encima (validación, 2026-11-11)

Encontrado en uso. Con una app de panel en el foco, tocar una libre la trae al frente y la deja libre, que es lo correcto. Pero después, tocar en el deck la app del foco no hacía nada: el código ignoraba el toque porque la app ya estaba en el foco. Para volver a ella había que tocar otra app del panel y luego la del foco. Con el ratón bastaba un clic.

Se compararon tres opciones:

- Dejarlo como estaba.
- Que la libre pase a ser la del foco y la del panel vuelva a su panel. Movía ventanas sin pedirlo, rompía la regla de «una libre sigue libre» y dejaba el foco vacío al cerrarse la ventana flotante.
- Que tocar la app del foco la traiga al frente. Es la elegida.

Ahora tocar la app del foco la trae al frente con el cursor encima, como un clic del ratón. La libre queda detrás, libre y en su sitio, sin cambiar el layout. El azul dice qué app ocupa el foco, no cuál está encima. Para eso está el indicador «al frente» (sección siguiente).

### Deck v2: tarjetas, mover entre monitores, energía y anclar (validación, 2026-11-11)

Objetivo: que el deck sea la forma normal de trabajar y se use mucho menos el ratón. Antes de tocar nada se analizó con el usuario; lo que se decidió y cómo quedó:

**LIBRES y ESCENAS sin nada fuera de la vista.**

- LIBRES crece con la cantidad de apps libres, hasta un tercio del alto, y siempre deja al menos una fila entera. Si aun así no caben, la última celda es «+N más».
- Las escenas (y la fila de abajo en el deck estrecho) no tenían «+N más»: con cuatro escenas en la tablet sólo se veía la primera, sin aviso. Ahora el último hueco es «+N más» y abre un menú con las que faltan.

**Columna izquierda en tres tarjetas,** con más aire entre tarjetas que entre botones:

- **Foco:** la app, «Anterior», «Al panel» y, en una fila de tres, «Mover», «Anclar» y «Cerrar».
- **Disposición:** vista, ancho y escenas.
- **Sistema**, abajo: pantalla encendida, energía, «Liberar todas» y, si procede, «Reordenar todas».
- Las pestañas de los monitores pasan a una barra encima de los paneles: son de los paneles, no del foco.
- El botón «Enviar a» de la cabecera cambia su icono de 📌 a ⇢: el 📌 es ahora de las apps ancladas.

**Regla de color del deck:** el borde dice que algo se puede tocar en este modo; el color dice el modo. Amarillo es mover, rojo es cerrar e índigo (#5A78C8) es anclar. El índigo es distinto del azul de lo activo.

**Mover.**

- Al entrar en el modo, las apps que se pueden mover llevan borde amarillo.
- Al elegir una, sólo ella conserva el borde (ya no se pinta de azul) hasta que se mueve o se cancela. Tocarla otra vez la suelta.
- Los destinos se ponen amarillos: los paneles, LIBRES y las pestañas de los otros monitores. Tocar una pestaña cambia de monitor sin salir del modo, y ahí se elige el panel. Así se mueve de pantalla a pantalla sin liberar la app antes.

**Energía** (⏻ en Sistema): abre sus botones grandes en lugar de los del sistema.

- Bloquear y Suspender actúan con un toque.
- Reiniciar, Apagar y Cerrar sesión piden un segundo toque («Apagar: ¿seguro?», en rojo, 4 s).
- Ninguna libera las ventanas. Antes de reiniciar, apagar o cerrar sesión se guarda la sesión, y al volver FocusWShell pone cada ventana en su panel.
- Apagar usa `shutdown /s /hybrid`, como el menú Inicio (respeta el inicio rápido). Bloquear usa `LockWorkStation`; suspender, `SetSuspendState`.

**«Restaurar todas» pasa a «Liberar todas»** (en inglés, «Free all»): dice lo que hace, dejar las ventanas libres. En inglés, «Release» pasa a «Free» en todo: Free, Free here, FREE.

**«Reordenar todas»** deshace el último «Liberar todas»:

- Antes de cada «Liberar todas» se guarda cómo estaba todo: qué ventana en qué panel, en el foco o estacionada, y la vista. Pasa desde el deck, la bandeja, la ventana principal y el atajo de emergencia.
- El botón sólo se ve si hay esa foto, y la foto se borra al usarla. Es un solo nivel, sin historial: el siguiente «Liberar todas» la reemplaza.
- Vuelve a poner las ventanas que siguen abiertas. Se guarda en memoria, por ventana: tras reiniciar FocusWShell, la sesión ya hace ese trabajo.
- Al salir de FocusWShell también se liberan las ventanas, pero eso no guarda foto.
- No se llama «Recuperar» porque ese nombre ya lo usa sacar ventanas del estacionamiento.

**Anclar (📌).**

- En los paneles ya no salen todas las apps que alguna vez estuvieron en ellos. Una app cerrada sólo sale (apagada, para abrirla con un toque) si está anclada.
- El modo «Anclar» es el tercer modo del deck, junto a mover y cerrar. Las ancladas llevan borde índigo sólido; las demás, borde índigo tenue. Mientras dura el modo salen también las cerradas sin anclar, para poder anclarlas. Tocar ancla o desancla, y el modo sigue encendido hasta tocar otra vez el botón.
- Las libres no se anclan, porque no tienen panel.
- Fuera del modo, las ancladas llevan un 📌 pequeño en la esquina. El menú de cada app (clic derecho o pulsación larga) también ancla y desancla.
- Se guarda en el catálogo (`apps.json`, versión 2, `Pinned`). Al leer un catálogo anterior se anclan las apps de la última sesión, que son las de uso continuo.
- Pospuesto: las apps de la bandeja del sistema (casi nunca muestran ventana) y abrir las ancladas al iniciar.

**Probado:**

- `--deck-preview` en los tres tamaños y en los dos idiomas. Muestra los modos mover (elegir app y elegir destino), cerrar, anclar y energía (también con la confirmación).
- 307 pruebas.

### Español e inglés (2026-11-11)

La app estaba sólo en español. Ahora sus textos están en recursos `.resx`: `Strings.resx` en español, que es el idioma base, y `Strings.en.resx` en inglés. Añadir otro idioma es añadir otro archivo. Hay dos juegos:

- **`FocusWShell.Core/Strings.resx`** (97 textos): los rechazos del modelo, las notas del layout, los nombres de los atajos y de las teclas, las exclusiones y los motivos de «no gestionable». Win32 los usa también: errores al abrir apps, el resumen de restaurar, los errores al mover. La clase la genera MSBuild.
- **`FocusWShell.App/Strings.resx`** (341 textos): la ventana principal (XAML con `{x:Static local:Strings.X}`), el deck, la bandeja, las tiras, las listas, la ventana de atajos y los mensajes de estado. `Strings.Designer.cs` lo escribe `build	extos.ps1` junto al `.resx`, porque el compilador de XAML necesita la clase antes de que MSBuild genere recursos (si se genera en `obj`, falla con MC3050).
- **Los textos con valores** usan huecos `{0}` y `Text.F(Strings.X, a, b)`. Las frases con «izquierda» o «derecha» van enteras, porque el orden de las palabras cambia de un idioma a otro.
- **No se traducen los identificadores:** la región «Centro» del layout, los títulos internos de las ventanas propias (las pruebas los buscan), los ids de los paneles y los archivos. Las presets de ancho sí se traducen al mostrarse, porque se guardan los pesos, no el nombre.

**Añadir un texto.** `powershell -File build	extos.ps1 -Clave Deck_Hola -Es "Hola, {0}" -En "Hello, {0}"` lo añade en los dos idiomas (con `-Proyecto Core`, en el núcleo) y regenera la clase. En el código se usa `Strings.Deck_Hola`, o `Core.Text.F(Strings.Deck_Hola, nombre)` si tiene huecos. Si se edita el `.resx` a mano, basta con `powershell -File build	extos.ps1` para regenerar la clase.

**Elegir el idioma.** En la ventana principal, «Idioma: Automático (el de Windows) / Español / English», guardado en `settings.json` (`Language`).

- Se aplica al arrancar, en el constructor estático de `App`, antes de crear ningún texto. Cambiarlo ofrece reiniciar: la instancia nueva (`--reinicio`) espera a que la vieja se cierre, y la sesión devuelve las ventanas a sus paneles.
- «Automático» da español si Windows está en español, e inglés en otro caso.
- Una configuración de antes de los idiomas, sin `Language`, sigue en español: con Windows en inglés, «automático» la habría cambiado sin que nadie lo pidiera.

**Pruebas.**

- Fijan la cultura en español (`TestCulture`, un `ModuleInitializer`), porque comprueban mensajes.
- `TranslationTests` comprueba que el inglés tiene las mismas claves que el español y los mismos huecos en cada texto: un hueco de menos rompería al usar ese texto.
- `--deck-preview <carpeta> <es|en>` dibuja el deck en cada idioma.
- Revisado en inglés: el deck (con los modos mover y cerrar, y un monitor sin paneles) y la ventana principal (Ventanas y Paneles). Los guiones de prueba buscan los textos en español, así que se ejecutan con el idioma en español.

### Ventanas libres que siguen libres (validación, 2026-11-11)

Encontrado en uso con una llamada de Zoom:

- **Tocar una app libre en el deck la adoptaba en Trabajo y la llevaba al foco.** Hay ventanas que conviene dejar flotando: las de Zoom, un diálogo. Ahora tocar una libre sólo la trae al frente, con el cursor encima, y sigue libre. Para ponerla en un panel o en el foco está «Mover».
- **Al terminar la llamada, sus ventanas seguían en LIBRES.** FocusWShell guarda la última foto de cada ventana vista, y sólo olvidaba las gestionadas que se cerraban. Ahora, al refrescar, también se olvidan las libres que ya no aparecen.

Comprobado con toques inyectados y una ventana de prueba libre: al tocarla quedó en primer plano y libre; al cerrarla, salió de LIBRES en menos de 5 s.

### La ventana de configuración quedaba detrás (validación, 2026-11-11)

Encontrado en uso: la ventana principal no se podía poner al frente. Causa: WPF la centra en el monitor del cursor (`CenterScreen`), y usando el deck el cursor está en la tablet. La ventana se abría ahí, debajo del deck, que va siempre encima.

Arreglo:

- Al mostrarla, si cae en el monitor donde está fijado el deck (según la configuración, porque al arrancar el deck aún no se ve), va al centro del principal.
- Se pone al frente con la activación robusta y un instante «siempre encima».
- Al arrancar, tras restaurar la sesión, que activa la app del foco, se vuelve a poner delante.
- Abrir `FocusWShell.exe` con uno ya abierto muestra su ventana en lugar del aviso «ya se está ejecutando».

Comprobado arrancando con el cursor en la tablet: la ventana quedó en el principal, encima y en primer plano. Tapada por otra ventana, abrir el ejecutable otra vez la trajo al frente.

### Iconos que faltaban (validación, 2026-11-10)

Encontrado en uso: faltaban iconos en el deck y en la ventana principal. `focus-probe icons` muestra de dónde sale el de cada app. Había tres causas:

| Causa | Apps | Arreglo |
| --- | --- | --- |
| La ruta del catálogo quedó vieja: la app se actualizó y su carpeta de versión desapareció. El catálogo sólo rellenaba la ruta la primera vez. Abrir la app cerrada desde una lista también fallaba | Claude (`Claude_4.23118…` → `Claude_5.1231…`), Google Drive (`133.0.2.0` → `134.0.1.0`), Notepad | El catálogo guarda siempre la última ruta vista. Si la app está cerrada, `ExecutablePaths.Resolve` busca la versión más nueva junto a la vieja (para el icono y para abrirla). En `WindowsApps`, que no se puede listar, se deduce el AUMID de la carpeta (`Nombre_Editor!App`), sólo para el icono |
| Apps de la Store alojadas en `ApplicationFrameHost` | Calculadora, Configuración | El icono se pide al shell por su AUMID (`IShellItemImageFactory`, `ShellIcon`), como lo muestra el Explorador |
| La caché guardaba «sin icono» toda la sesión | Todas | La clave incluye la ruta y el AUMID: si cambian, se vuelve a intentar |

Comprobado con `focus-probe icons`: Calculadora y Configuración dan su propio icono; Google Drive lo saca de `134.0.1.0`; Notepad, del AUMID deducido. Claude lo recupera cuando el catálogo la ve abierta (su id de app no es «App», así que deducir no basta).

### Ventanas de tamaño fijo (validación, 2026-11-10)

Encontrado en uso: NetBird no declara borde redimensionable (`WS_THICKFRAME`) ni botón de maximizar, pero el layout la estiraba a su hueco (943×689, cuando la suya es de unos 366×653) y se veía mal. Ya se leía `IsResizable` al enumerar; el layout no lo usaba.

Ahora (`Core.Layout.FixedSize`, con pruebas):

- **Automático:** una ventana sin borde redimensionable se mueve, nunca se redimensiona.
- **Por app** (en el catálogo, `SizeMode`): «Según la ventana», «Tamaño fijo: sólo moverla» o «Redimensionarla siempre». Se elige con clic derecho en la lista principal. Las gestionadas de tamaño fijo dicen «· tamaño fijo» en su estado.
- **Su tamaño propio** sale del diario (el rectángulo de antes de que FocusWShell la tocara, menos los bordes invisibles). Así se corrige una que ya se estiró. Entre monitores se escala por DPI.
- **En el layout:** el motor le reserva al menos ese tamaño, como un mínimo. Después se coloca en su hueco:
  - en un panel, arriba y centrada en horizontal;
  - maximizada (template Fill o pantalla completa), centrada en el monitor y sin maximizar;
  - si no cabe, se queda con su tamaño y una nota lo explica.

Probado con NetBird en Trabajo (columna de 929 px):

| Caso | Resultado |
| --- | --- |
| Automático | Conserva sus 380×660, arriba y centrada en la columna. El estado dice «En Trabajo · tamaño fijo» |
| «Redimensionarla siempre» | Se estira a 943×1371, como antes |
| Vuelta a «Según la ventana» | Vuelve a 380×660, centrada |
| Liberarla | Vuelve a su sitio original con su tamaño |

### Lista de exclusión (M2, 2026-11-10)

Reglas del usuario (`Core.Windows.ExclusionRule`, con pruebas) que se aplican encima de las de elegibilidad. Hay tres tipos:

- **Por ejecutable:** `NetBird.exe` o `netbird`, sin distinguir mayúsculas.
- **Por AUMID:** para apps empaquetadas.
- **Por clase de ventana:** excluye un tipo de ventana, no la app entera.

Se guardan en `settings.json` (`"Exclusions": [{ "Kind": "Executable", "Value": "…" }]`). Una ventana excluida no se gestiona ni sale en la lista. Si estaba en un panel, se suelta y vuelve a su sitio original.

En la ventana principal:

- **Clic derecho en una ventana:** «Excluir la app…» (el AUMID si es empaquetada; si no, el ejecutable) o «Excluir sólo este tipo de ventana».
- **Sección «Excluidas»:** las reglas, cada una con «Quitar», y un campo para añadir una a mano.

| Caso | Resultado |
| --- | --- |
| Excluir `msedge.exe` con Edge en Trabajo | Edge se suelta y vuelve a su sitio original. Sale de la lista y de la tira, y la regla queda en `settings.json` |
| Añadir `MSEDGE` otra vez | «Ya estaba excluida» |
| Quitar la regla | Edge vuelve a la lista, libre |
| Clic derecho en NetBird (el caso pendiente de S1) | Ofrece excluir `netbird-ui.exe` o la clase `WailsWebviewWindow`. Excluida, desaparece de la lista |

Encontrado aquí y ya corregido: en el menú, el «_» de una clase (`Chrome_WidgetWin_1`) se tomaba como tecla de acceso y desaparecía.

### Ventanas no gestionables (M2, 2026-11-10)

Una ventana cuyo proceso tiene más integridad que FocusWShell (una app como administrador, o del sistema) no se puede mover: UIPI lo impide. Antes se quitaba de la lista sin más. Ahora:

- **En la lista principal sale al final**, con el estado «No gestionable: corre como administrador» («es del sistema», o «no se pudo leer su nivel» si no se pudo saber). El selector de panel está desactivado. El motivo lo da `Core.Windows.Unmanageable`, con pruebas.
- **El atajo «Al foco»** sobre una de ellas dice por qué no puede, en lugar de «no está gestionada».

Probado con el Administrador de tareas, que se eleva solo: sale en la lista como «No gestionable: corre como administrador», con el selector desactivado.

No se pudo probar el atajo: con una ventana elevada en primer plano, Windows descarta las teclas inyectadas desde un proceso normal (UIPI). Hay que probarlo con el teclado de verdad.

### Suspensión y pérdida del monitor principal (M2, 2026-11-10)

**Suspensión.** La ventana oculta que ya escuchaba los cambios de monitores (S6) escucha también `WM_POWERBROADCAST`:

- Al suspender empieza un cambio de monitores: el observador calla, como en S6.
- Al reanudar (`PBT_APMRESUMEAUTOMATIC` o `PBT_APMRESUMESUSPEND`) espera 4 s más, porque los monitores llegan tarde y a ráfagas (y una tablet por wifi, aún más). Cada aviso de monitores reinicia la espera.
- Después hace lo mismo que S6: refresca las ventanas, olvida las que se cerraron, reconcilia monitores y reaplica el layout. Los avisos de monitores que llegan mientras está dormido se ignoran.

Probado sin suspender de verdad: se mandó el mensaje de energía a la ventana oculta, con Edge en Trabajo.

| Caso | Resultado |
| --- | --- |
| Suspender y mover Edge a (60,60) | No se interpreta como un movimiento del usuario: Edge se queda donde la dejaron |
| 2 s después de reanudar | Sigue esperando |
| 7 s después de reanudar | Edge vuelve a su sitio en Trabajo. El estado dice «Volvió de la suspensión» |
| Cerrar Edge mientras está dormido | Al reanudar se da por cerrada y sale de la lista |

**Pérdida del monitor principal.** El principal es el de Windows. Si se desconecta, Windows nombra otro y ese pasa a tener el Centro. La comparación de monitores sale a `Core.Displays.DisplayReconcile`, con pruebas:

- Si el nuevo principal era un auxiliar con paneles, sus paneles cuentan como desconectados y sus ventanas se juntan con las del principal.
- Cuando vuelve el de antes, cada uno recupera lo suyo.
- El aviso es uno solo («Se desconectó el principal… hace de principal»), en lugar de un «se desconectó» engañoso del monitor que en realidad pasó a principal.

Pendiente, con el usuario:

- una suspensión real con apps en paneles;
- desconectar de verdad el monitor principal;
- un monitor que vuelve, pero Windows no lo nombra principal otra vez: se quedaría como un auxiliar sin paneles hasta que el usuario cambie el principal en Windows.

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
