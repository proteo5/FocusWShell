# Documentación de FocusWShell

FocusWShell es un gestor de atención visual para Windows. Organiza las ventanas según el papel que cumplen en la atención de quien trabaja (tarea central, herramientas de apoyo, contexto periférico) y no sólo como rectángulos en una cuadrícula.

Esta carpeta es la documentación de ingeniería del proyecto. Parte de las notas de ideación originales (ver [Fuentes](#fuentes)) y las convierte en decisiones, un modelo de dominio y un plan de construcción.

## Índice

| Documento | Para qué sirve |
| --- | --- |
| [01 — Visión y alcance](01-vision.md) | Problema, principios, qué entra y qué no en el MVP, cómo sabremos si funciona. |
| [02 — Modelo de dominio](02-modelo-de-dominio.md) | Entidades, estados de una ventana, acciones e invariantes. Es el contrato del núcleo. |
| [03 — Arquitectura](03-arquitectura.md) | Componentes, hilos, flujo de eventos y detalles Win32 que hay que resolver bien. |
| [04 — Riesgos y spikes](04-riesgos-y-spikes.md) | Lo que puede salir mal técnicamente y cómo comprobarlo pronto. |
| [05 — Hoja de ruta](05-hoja-de-ruta.md) | Hitos, entregables y criterios de salida. |
| [06 — Validación de uso](06-validacion.md) | Protocolo y plantilla para el diario de uso personal. |
| [Desarrollo](desarrollo.md) | Compilar, instalar una copia de uso diario, textos, vista previa del deck y publicar una versión. |
| [Glosario](glosario.md) | Términos en español y su nombre en el código. |
| [Decisiones (ADR)](adr/) | Registro de decisiones de arquitectura. |

## Decisiones registradas

| ADR | Decisión | Estado |
| --- | --- | --- |
| [0001](adr/0001-orquestar-ventanas-top-level.md) | Orquestar ventanas top-level; no usar `SetParent` como mecanismo base | Aceptada |
| [0002](adr/0002-no-reemplazar-shell.md) | Convivir con Explorer; no reemplazar el shell en la primera etapa | Aceptada |
| [0003](adr/0003-rol-independiente-del-foco.md) | El rol de una ventana es independiente del foco de Windows | Aceptada |
| [0004](adr/0004-stack-dotnet-wpf-cswin32.md) | .NET 10 + C# + CsWin32 + WPF | Aceptada |
| [0005](adr/0005-diario-de-recuperacion.md) | Diario de recuperación escrito antes de mover cualquier ventana | Aceptada |
| [0006](adr/0006-ocultar-con-minimizar.md) | Las ventanas no visibles de una pila se minimizan, no se mueven fuera de pantalla | Propuesta |
| [0007](adr/0007-cambios-externos-por-reconciliacion.md) | Los cambios externos se detectan reconciliando el estado final, no atribuyendo eventos | Aceptada |
| [0008](adr/0008-centro-solo-en-monitor-principal.md) | El Centro sólo existe en el monitor principal; los demás son auxiliares | Aceptada |
| [0009](adr/0009-tablets-como-monitores-auxiliares.md) | Las tablets que extienden el escritorio son monitores auxiliares; sin drivers ni clientes propios (se sugieren Sunshine + Moonlight, Apollo + Artemis y otros) | Aceptada |
| [0010](adr/0010-mantener-pantalla-encendida.md) | FocusWShell incluye "mantener la pantalla encendida" | Aceptada |
| [0011](adr/0011-paneles-y-catalogo.md) | Paneles con nombre, ubicación y presentación; ventanas libres; catálogo por detección | Aceptada |
| [0012](adr/0012-vistas-posicion-del-foco.md) | Vistas: un solo foco, a la izquierda, al centro o a la derecha | Aceptada |
| [0013](adr/0013-deck-superficie-de-control.md) | El deck es una superficie de control opcional, en el monitor que se elija | Propuesta |
| [0014](adr/0014-mapa-de-controles-en-el-deck.md) | Apuntar en la app del foco desde el deck: imagen en vivo, cuadrícula y controles detectados | Propuesta |
| [0015](adr/0015-tiles-adaptables-y-lista.md) | Presentaciones: tiles adaptables y lista tipo Explorador | Aceptada |
| [0016](adr/0016-escritorios-virtuales.md) | Seguir los escritorios virtuales de Windows: un foco y unas apps por escritorio | Propuesta (fase 1 hecha) |
| [0017](adr/0017-ventana-principal-wpf-ui.md) | La ventana principal con WPF-UI: configurar y ver el estado | Aceptada |

## Fuentes

Las notas de ideación originales viven fuera del repositorio, en la carpeta personal de ideas del autor (`administrador-ventanas-multimonitor/`, documentos `00` a `05`). Esta documentación las resume y las reinterpreta; donde hay diferencias, manda lo que está aquí, porque refleja decisiones de ingeniería posteriores. Las referencias bibliográficas sobre percepción y ergonomía se conservan en [01 — Visión y alcance](01-vision.md#fundamentos).
