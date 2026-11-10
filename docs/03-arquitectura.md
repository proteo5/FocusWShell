# 03 — Arquitectura

## Visión general

FocusWShell es un **orquestador de ventanas top-level** ([ADR 0001](adr/0001-orquestar-ventanas-top-level.md)). Las aplicaciones ajenas siguen siendo ventanas independientes; el gestor las observa, decide dónde va cada una y les aplica geometría y visibilidad desde fuera. Su propia interfaz (tiras de pestañas, microbarras, bandeja) son ventanas suyas que se colocan *junto a* las ventanas gestionadas, nunca encima del Centro.

```text
┌──────────────────────────── FocusWShell.App (WPF) ─────────────────────────────┐
│  Bandeja / menú     Overlays de panel (tiras, microbarras)     Atajos globales  │
└───────────────┬─────────────────────────────────────────────────────┬──────────┘
                │ comandos                                    estado  │
┌───────────────▼─────────────────────────────────────────────────────┴──────────┐
│                         FocusWShell.Core (sin Win32)                            │
│  Modelo de escena · Motor de comandos · Motor de layout → LayoutPlan            │
│  Interfaces: IWindowSystem, IDisplaySystem, IJournal, ISceneStore               │
└───────────────┬─────────────────────────────────────────────────────▲──────────┘
                │ LayoutPlan                                eventos   │
┌───────────────▼─────────────────────────────────────────────────────┴──────────┐
│                         FocusWShell.Win32                                       │
│  Enumeración y filtro · WinEventHook · Aplicador (SetWindowPos asíncrono)       │
│  Monitores/DPI · Diario en disco · Identidad de ventanas                        │
└────────────────────────────────────────────────────────────────────────────────┘
```

### Estructura del repositorio (propuesta)

```text
src/
  FocusWShell.Core/        Dominio, comandos, layout. Sin dependencias de Windows.
  FocusWShell.Win32/       Interop (CsWin32): enumeración, colocación, atajo de emergencia.
    Persistence/           Diario de recuperación, restaurador y catálogo de mínimos en disco.
    Scenes/                SceneApplier: motor de layout + diario + movimiento + aprendizaje de mínimos.
  FocusWShell.App/         WPF: icono de bandeja, ventana principal, overlays (M1), configuración.
tests/
  FocusWShell.Core.Tests/  Pruebas unitarias del dominio con un IWindowSystem falso.
  FocusWShell.Win32.Tests/ Pruebas de integración manuales o semiautomáticas con ventanas de prueba.
tools/
  FocusWShell.Probe/       `focus-probe`: consola de diagnóstico para los spikes (listar, colocar, restaurar).
  FocusWShell.TestWindows/ Ventanas "maniquí" en estado controlado (normal, maximizada, minimizada).
```

Los ejecutables comparten `build/app.manifest` (PerMonitorV2, `asInvoker`) desde `Directory.Build.targets`. Va en `.targets` y no en `.props` porque necesita `OutputType`, que cada proyecto define después de importar el `.props`. Hasta el 2026-11-09 estuvo en el `.props` y no se aplicaba; con un solo monitor al 100 % no se notaba. `TestWindows` se excluye con `UseSharedManifest=false` porque WinForms declara el DPI por código. Los proyectos Windows apuntan a `net10.0-windows10.0.19041.0` (Windows 10 2004 o posterior).

La regla de dependencias es estricta: `Core` no referencia a `Win32` ni a `App`. Ver [ADR 0004](adr/0004-stack-dotnet-wpf-cswin32.md).

## Modelo de hilos

- **Hilo de UI (WPF dispatcher):** es dueño del modelo. Todos los comandos y eventos se procesan aquí, en orden, uno a la vez. No hay locks en el dominio.
- **Hooks de eventos:** `SetWinEventHook` con `WINEVENT_OUTOFCONTEXT` entrega los callbacks en el hilo que registró el hook, siempre que tenga un bucle de mensajes. Se registran en el hilo de UI. El callback sólo encola un evento ligero; el procesamiento ocurre después, con *debounce* (unos 50–100 ms) para agrupar ráfagas.
- **Atajos globales:** un hilo propio con su cola de mensajes registra los atajos con `RegisterHotKey` sin ventana. Ejecuta los comandos desde ese hilo, así que el de emergencia responde aunque la UI esté ocupada, y `Promote` puede dar el foco porque el proceso acaba de recibir input.
- **En M0**, `ShellController` es dueño del modelo (`Workspace`) y serializa con un lock los comandos y sus efectos, vengan de la UI o del hilo de atajos. Cuando lleguen los hooks de eventos (M1), el modelo pasará al hilo de UI como se describe arriba y los atajos le encolarán los comandos; el de emergencia seguirá restaurando directamente desde el diario.
- **Escritura del diario:** síncrona y antes de aplicar el plan. Es un archivo pequeño; no compensa hacerla asíncrona y perder la garantía.

## Flujo de un comando

```text
Atajo / menú
   │
   ▼
Command ──▶ Core valida precondiciones
   │
   ▼
Nuevo estado del modelo ──▶ Motor de layout ──▶ LayoutPlan
   │                                              │  lista de (ventana, rect destino,
   │                                              │  show/minimize, z-order)
   ▼                                              ▼
Diario: escribe OriginalState de ventanas nuevas  Aplicador Win32
y el estado lógico de la escena                   (SWP_ASYNCWINDOWPOS…)
                                                  │
                                                  ▼
                                    Registra "geometrías esperadas"
                                    para ignorar sus propios eventos
```

## Detalles Win32 que hay que resolver bien

### DPI

- El proceso es **Per-Monitor Aware v2** (declarado en el manifiesto). Todas las coordenadas del núcleo están en píxeles físicos del escritorio virtual.
- Al mover una ventana entre monitores con escala distinta, la propia app recibe `WM_DPICHANGED` y puede redimensionarse por su cuenta *después* de nuestro `SetWindowPos`. El aplicador verifica el rectángulo final y, si no coincide, reintenta una vez.
- Las apps que no son conscientes de DPI se virtualizan; su rectángulo visible sigue siendo correcto si se mide con `DWMWA_EXTENDED_FRAME_BOUNDS`.

### Bordes invisibles

En Windows 10/11, `GetWindowRect` incluye un borde de redimensionado invisible (unos 7 px a 100 %). Si se usa tal cual, quedan huecos entre tiles. El aplicador:

1. Lee `GetWindowRect` y `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`.
2. Calcula los márgenes invisibles por lado.
3. Expande el rectángulo destino con esos márgenes antes de llamar a `SetWindowPos`.

Los márgenes se recalculan por ventana y por DPI; no se usa una constante.

### Ventanas maximizadas y minimizadas

- Una ventana maximizada ignora parcialmente `SetWindowPos`. Antes de colocarla se restaura con `ShowWindow(SW_SHOWNOACTIVATE)` o con `SetWindowPlacement`.
- Las restauraciones usan variantes **sin activación** (`SW_SHOWNOACTIVATE`, `SWP_NOACTIVATE`) para no robar el foco. Sólo `Promote` activa la ventana a propósito.
- Para devolver el `OriginalState` se usa `SetWindowPlacement`, que respeta el rectángulo "normal" de una ventana que estaba maximizada o minimizada.

### Movimiento agrupado

S3 midió que `BeginDeferWindowPos`/`DeferWindowPos`/`EndDeferWindowPos` **no agrupa** ventanas de otros procesos. Windows entrega cada movimiento al hilo dueño de forma síncrona, así que el lote tarda lo mismo que moverlas una a una: ~145 ms para 6 apps, que se ven llegar en cascada. El aplicador mueve así:

1. Excluye de antemano las ventanas no gestionables, cerradas o colgadas (`IsHungAppWindow`).
2. Minimiza las ocultas de las pilas.
3. Normaliza sin activar (`SW_SHOWNOACTIVATE`) las que estén maximizadas o minimizadas y mide los bordes invisibles de todas.
4. Pide todos los movimientos con `SetWindowPos(… SWP_ASYNCWINDOWPOS)`. Cada app los recibe en su cola y se mueven en paralelo: la escena llega en ~13 ms.
5. Espera a que cada ventana alcance el rectángulo pedido. Si una se queda 150 ms quieta en otro tamaño, la app impuso su mínimo: se da por llegada y alimenta el catálogo de mínimos.
6. Si una ventana cambia a un monitor con otro DPI, espera a que lleve 100 ms quieta: la app se reescala por su cuenta (`WM_DPICHANGED`) *después* de que el movimiento asíncrono se diera por llegado. S4 lo encontró.
7. Corrige individualmente las que no quedaron exactas.
8. Maximiza las ventanas en `Fill` de los monitores auxiliares (ADR 0008):
   - Si ya están maximizadas en ese monitor, no se tocan.
   - Si no, el paso 4 ya las llevó a su monitor en modo normal (así cruzan de DPI sin activarse) y aquí se maximizan.
   - Windows no tiene un "maximizar sin activar": si el foco cambia, se devuelve a la ventana que lo tenía.
   - Se mide cuando termina la animación.

Todos los monitores se mueven en el mismo lote: `SceneApplier` combina el plan del principal (`SceneLayoutEngine`) y el de cada auxiliar (`AuxLayoutEngine`) en un `ScenePlan`.

`SWP_ASYNCWINDOWPOS` además evita que una app lenta bloquee el hilo del aplicador. `PlaceBatch` conserva el modo con `DeferWindowPos` y, si el lote falla, vuelve a `SetWindowPos` ventana por ventana; queda sólo para comparar.

### Foco

`SetForegroundWindow` sólo funciona si el proceso tiene derecho a cambiar el primer plano. Al procesar un atajo registrado con `RegisterHotKey`, el gestor acaba de recibir input y lo tiene; por eso `Promote` se dispara siempre desde un atajo o un clic en la UI del gestor, nunca desde un temporizador.

### Ventanas ocultas de una pila

Las ventanas no visibles de un panel en modo pila se **minimizan** (`SW_SHOWMINNOACTIVE`). Moverlas fuera de pantalla las dejaría perdidas si el gestor cae, y `DWMWA_CLOAK` sólo funciona con ventanas propias. Ver [ADR 0006](adr/0006-ocultar-con-minimizar.md).

## Elegibilidad de ventanas

Una ventana es candidata si cumple todo esto:

- Es visible (`IsWindowVisible`) y no está *cloaked* (`DWMWA_CLOAKED` = 0). Así se descartan las ventanas de otros escritorios virtuales y las apps UWP suspendidas.
- No tiene `WS_EX_TOOLWINDOW` ni `WS_EX_NOACTIVATE`.
- No tiene dueño (`GetWindow(GW_OWNER)` nulo), salvo que tenga `WS_EX_APPWINDOW`. Así se excluyen diálogos y ventanas auxiliares.
- Tiene título y un tamaño mínimo razonable.
- Su clase no está en la lista del sistema: `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`, `Progman`, `WorkerW`, `Windows.UI.Core.CoreWindow` (cuando no tiene marco), ventanas del propio gestor, entre otras.
- No pertenece a un proceso con integridad mayor que la del gestor. Si `OpenProcess`/`GetTokenInformation` no permite comprobarlo, se intenta un movimiento nulo y se marca como "no gestionable" si falla con acceso denegado.
- No es una app en pantalla completa exclusiva o "fullscreen" propia (ocupa todo el monitor sin marco). Mientras dure, el gestor deja ese monitor en paz.

Las apps UWP/empaquetadas viven dentro de `ApplicationFrameWindow` (proceso `ApplicationFrameHost.exe`). Para identificarlas se usa el AUMID de la ventana (`SHGetPropertyStoreForWindow` → `PKEY_AppUserModel_ID`) en lugar del ejecutable.

La lista de exclusión del usuario (por ejecutable, AUMID o clase) se aplica encima de estas reglas.

## Eventos que se escuchan

| Evento | Uso |
| --- | --- |
| `EVENT_OBJECT_CREATE` / `EVENT_OBJECT_SHOW` | Nueva ventana candidata (aparece en "sin gestionar"). |
| `EVENT_OBJECT_DESTROY` / `EVENT_OBJECT_HIDE` | La ventana desaparece del modelo. |
| `EVENT_SYSTEM_MINIMIZESTART` / `EVENT_SYSTEM_MINIMIZEEND` | Minimizar/restaurar hecho por el usuario → `Park`/`Restore`. |
| `EVENT_SYSTEM_MOVESIZESTART` / `EVENT_SYSTEM_MOVESIZEEND` | El usuario arrastró o redimensionó → la ventana queda fuera de layout. |
| `EVENT_OBJECT_LOCATIONCHANGE` | Detectar maximizar desde el botón de Windows. Es muy ruidoso: se filtra por `OBJID_WINDOW` y por ventanas gestionadas. |
| `EVENT_OBJECT_CLOAKED` / `EVENT_OBJECT_UNCLOAKED` | Cambio de escritorio virtual o suspensión UWP. |
| `EVENT_OBJECT_NAMECHANGE` | Actualizar el título en la tira de pestañas. |
| `EVENT_SYSTEM_FOREGROUND` | Sólo para indicadores visuales. **Nunca** cambia ubicaciones. |

Todos los callbacks filtran `idObject == OBJID_WINDOW` e `idChild == CHILDID_SELF`.

**Eventos propios ([ADR 0007](adr/0007-cambios-externos-por-reconciliacion.md)):** no se intenta atribuir cada evento. `WindowEventWatcher` espera a que una ventana vigilada lleve 120 ms sin eventos (o a que termine un arrastre) y `ExpectationRegistry` compara su estado final con lo que el gestor dejó en ella. Mientras el gestor mueve ventanas (`OwnChange()`) no se evalúa nada; antes de terminar, registra como expectativa el rectángulo verificado de cada ventana.

## Monitores

- Identidad estable: `QueryDisplayConfig` + `DisplayConfigGetDeviceInfo` (`DISPLAYCONFIG_TARGET_DEVICE_NAME.monitorDevicePath`). `HMONITOR` y `\\.\DISPLAYn` cambian entre reconexiones y no sirven como clave.
- Señales de cambio: `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE` (área de trabajo), `WM_DPICHANGED` en la ventana oculta del gestor, más reanudación de suspensión (`WM_POWERBROADCAST`).
- Tras un cambio se espera a que la configuración se estabilice (unos 1,5 s sin más cambios) antes de recalcular. Las reconexiones suelen generar varios eventos seguidos.
- Las áreas de las regiones se calculan sobre `rcWork` (sin barra de tareas). La pantalla completa usa `rcMonitor`.

## Identidad de ventanas

| Situación | Cómo se reencuentra una ventana |
| --- | --- |
| Gestor en marcha | Por `hwnd`. |
| El gestor se reinicia (las apps siguen abiertas) | `hwnd` + id del proceso + hora de inicio del proceso. Si las tres coinciden, es la misma ventana. |
| Reinicio de Windows o la app se reabre | No hay ventana que restaurar. Las escenas pueden *sugerir* asignaciones por ejecutable/AUMID + clase + patrón de título, pero en el MVP no se adoptan solas. |

## Persistencia

Todo vive en `%LOCALAPPDATA%\FocusWShell\`:

| Archivo | Contenido | Escritura |
| --- | --- | --- |
| `journal.json` | Estado original de cada ventana gestionada, estado lógico actual y marca "limpio/sucio". | Antes de cada plan que toque ventanas nuevas y tras cada comando. Escritura atómica (temporal + `File.Replace`). |
| `scenes.json` | Escenas, layouts por huella de monitores y templates. | Al guardar una escena. |
| `settings.json` | Atajos, exclusiones y preferencias. | Al cambiar la configuración. |

Formato JSON con `System.Text.Json` y un campo `schemaVersion` en cada archivo.

## Recuperación ante fallos

1. **Arranque normal:** si `journal.json` está marcado "sucio", el gestor no terminó bien. Ofrece "Restaurar ventanas a su estado anterior" o "Retomar la escena". Por defecto, restaurar.
2. **Cierre normal:** `RestoreAll`, diario marcado "limpio".
3. **Atajo de emergencia:** registrado con `RegisterHotKey`; se procesa incluso si la UI del gestor está ocupada, porque no pasa por el motor de layout: aplica directamente los `OriginalState` del diario.
4. **Excepción no controlada:** el manejador global intenta `RestoreAll` antes de terminar.
5. **Proceso muerto sin aviso** (taskkill, cuelgue): las ventanas quedan donde estaban, que es una posición válida y visible porque nunca se mueven fuera de pantalla. Se recuperan en el siguiente arranque. Opcional después del MVP: un proceso guardián mínimo que vigila al principal y restaura desde el diario.

## Overlays del gestor

- Cada panel tiene una ventana overlay propia (WPF, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`) en una franja reservada del panel, por ejemplo 28 px arriba. Esa franja se descuenta del rectángulo que recibe la app, así que el overlay **nunca se superpone** a una ventana gestionada.
- El Centro no tiene overlay permanente. Su microbarra, si existe, aparece sólo al acercar el cursor al borde y se superpone al margen, no al contenido.
- En pantalla completa, los overlays de ese display se ocultan.
- Las miniaturas DWM (`DwmRegisterThumbnail`) son una mejora posterior para previsualizar pestañas; no forman parte del MVP.
