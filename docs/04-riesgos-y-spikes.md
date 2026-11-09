# 04 — Riesgos y spikes

Antes de construir escenas y UI hay que comprobar que la base técnica aguanta. Cada riesgo tiene un spike: un experimento corto, con un resultado observable que lo confirma o lo descarta.

## Riesgos

| # | Riesgo | Impacto | Probabilidad | Mitigación |
| --- | --- | --- | --- | --- |
| R1 | Huecos o solapes entre ventanas por los bordes invisibles y el DPI mixto | Alto: el layout se ve roto | Alta | Medir con `DWMWA_EXTENDED_FRAME_BOUNDS`, compensar por ventana y verificar tras mover. |
| R2 | Una app se redimensiona por su cuenta después de moverla (`WM_DPICHANGED`, tamaño mínimo, Electron) | Medio | Media | Verificar el rectángulo final y reintentar una vez; si persiste, aceptar el tamaño de la app y centrarla en su región. |
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

_(Pendiente.)_
