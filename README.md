# FocusWShell

Gestor de atención visual para Windows. Organiza las ventanas según el papel que cumplen en tu atención, no sólo como rectángulos en una cuadrícula.

Pensado para trabajar con un monitor ultrawide y un monitor estándar con muchas ventanas abiertas:

```text
┌──────────────┬──────────────────────────────┬────────────────┐   ┌──────────────┐
│ CONTEXTO     │ CENTRO                       │ TRABAJO        │   │ CONTEXTO     │
│ chat, avisos │ la tarea principal           │ tiles / pila   │   │ correo, cal. │
└──────────────┴──────────────────────────────┴────────────────┘   └──────────────┘
                          ultrawide                                  monitor estándar
```

- **Centro:** la tarea de concentración, en una región amplia y protegida.
- **Trabajo:** herramientas de apoyo, en tiles o en una pila con pestañas.
- **Contexto:** correo, chat, calendario y estados, a la vista pero sin dominar.
- **Estacionada:** abierta pero apartada; se recupera desde su panel.

Una ventana sólo cambia de lugar cuando tú lo decides. Hacer clic en el chat o recibir una notificación no la lleva al centro.

## Estado

Hito M0 (núcleo): primera versión usable con un layout fijo Contexto | Centro | Trabajo en el monitor más ancho. Funciona con varios monitores y escalas distintas, pero la escena vive en un solo monitor. Todavía sin escenas guardadas ni templates por panel.

```bash
dotnet run --project src/FocusWShell.App
```

- La ventana lista las apps que se pueden gestionar. Elige un rol y un monitor para cada una y pulsa **Aplicar roles**.
  - El Centro está siempre en el monitor principal.
  - Los demás monitores alojan paneles auxiliares.
  - Con el template **Fill**, la primera ventana de un panel auxiliar ocupa ese monitor, maximizada.
- Cerrarla no detiene el gestor: sigue en el área de notificación. Desde su menú, **Salir** devuelve todas las ventanas a su sitio.
- Atajos globales con `Ctrl+Alt+Mayús`:

  | Tecla | Acción |
  | --- | --- |
  | ↑ | Lleva al Centro la ventana activa, si está gestionada. La que estaba vuelve a su panel. |
  | ← | Vuelve a poner en el Centro la ventana anterior. Repetirlo alterna entre las dos. |
  | ↓ | Devuelve la ventana del Centro a su panel. |
  | P | Mantener la pantalla encendida (activa o desactiva; se recuerda). |
  | Retroceso | **Emergencia:** restaura todas las ventanas en cualquier momento. |
- Si el gestor muere sin cerrarse, al volver a abrirlo ofrece restaurar las ventanas que dejó movidas.

`tools/FocusWShell.Probe` (`focus-probe`) es la consola de diagnóstico de los spikes.

La documentación de ingeniería está en [docs/](docs/README.md):

- [Visión y alcance](docs/01-vision.md)
- [Modelo de dominio](docs/02-modelo-de-dominio.md)
- [Arquitectura](docs/03-arquitectura.md)
- [Riesgos y spikes](docs/04-riesgos-y-spikes.md)
- [Hoja de ruta](docs/05-hoja-de-ruta.md)
- [Decisiones (ADR)](docs/adr/)

## Principios

- Convive con Explorer y la barra de tareas; no reemplaza el shell.
- Las apps siguen siendo ventanas normales de Windows; el gestor no las incrusta.
- Siempre hay salida: un atajo global devuelve todas las ventanas a como estaban, incluso después de una caída.
- No pide permisos de administrador, no envía telemetría.

## Licencia

[Apache 2.0](LICENSE)
