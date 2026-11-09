# ADR 0006 — Las ventanas no visibles de una pila se minimizan

- **Estado:** Propuesta (a confirmar en el spike S7)
- **Fecha:** 2026-11-09

## Contexto

En un panel con template `Stack`, sólo una ventana se ve; las demás deben quitarse de la vista sin cerrarse. Opciones:

| Opción | Problema |
| --- | --- |
| Moverlas fuera de pantalla | Si el gestor cae, quedan invisibles e inaccesibles para el usuario. |
| `ShowWindow(SW_HIDE)` | Desaparecen también de la barra de tareas y de Alt+Tab; si el gestor cae, el usuario no las encuentra. Algunas apps reaccionan mal. |
| *Cloak* con DWM | `DWMWA_CLOAK` sólo funciona con ventanas del propio proceso. |
| Dejarlas detrás de la activa | No ocupan otra región, pero asoman si la activa es más pequeña y aparecen en Alt+Tab en un orden confuso. |
| **Minimizarlas** (`SW_SHOWMINNOACTIVE`) | Animación de minimizar (depende de la configuración del sistema); algunas apps pausan contenido al minimizarse. |

## Decisión (propuesta)

Minimizar las ventanas no visibles de una pila. Es la única opción que, si el gestor cae, deja todas las ventanas en un estado que el usuario sabe recuperar con las herramientas normales de Windows.

## Consecuencias

- Una ventana minimizada por la pila y una minimizada por el usuario se ven igual para Windows. El modelo distingue `InPanel` en pila de `Parked`; los eventos `MINIMIZEEND` sobre una ventana en pila se interpretan como "cambiar de pestaña" y no como `Restore`.
- Si S7 muestra que la animación o las pausas molestan, la alternativa es "dejar detrás de la activa" para apps concretas.
