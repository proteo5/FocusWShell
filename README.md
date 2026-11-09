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

En diseño. Todavía no hay código. La documentación de ingeniería está en [docs/](docs/README.md):

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
