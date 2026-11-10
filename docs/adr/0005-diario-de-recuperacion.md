# ADR 0005 — Diario de recuperación escrito antes de mover cualquier ventana

- **Estado:** Aceptada (validada en S5)
- **Fecha:** 2026-11-09

## Contexto

La confianza en el gestor depende de que siempre se pueda volver al estado anterior (CA6). El proceso puede terminar de forma inesperada: excepción, `taskkill`, cuelgue, reinicio de Windows. Restaurar sólo en el cierre normal no basta.

## Decisión

- Antes de modificar por primera vez una ventana, el gestor escribe su `OriginalState` (`WINDOWPLACEMENT`, display, DPI, visibilidad) en `%LOCALAPPDATA%\FocusWShell\journal.json`.
- El diario también guarda el estado lógico actual (hogares, ubicaciones, escena activa) y una marca "limpio/sucio".
- Cada escritura es atómica: archivo temporal + `File.Replace`.
- Al arrancar, si el diario está "sucio", el gestor ofrece restaurar (opción por defecto) o retomar la escena.
- El atajo de emergencia aplica el diario directamente, sin pasar por el motor de layout.
- Las ventanas **nunca** se mueven fuera de las áreas visibles, para que una caída no deje nada perdido.

## Consecuencias

- Hay una escritura a disco por comando. El archivo es pequeño (decenas de ventanas), así que el coste es despreciable.
- Para reencontrar ventanas después de reiniciar el gestor se necesita una identidad más rica que el `hwnd` (proceso + hora de inicio). Ver [03 — Arquitectura](../03-arquitectura.md#identidad-de-ventanas).
- Tras un reinicio de Windows el diario no tiene ventanas que restaurar; se descarta y se marca limpio.
- Una entrada sale del diario cuando su ventana vuelve a su sitio (`RestoreOriginal`), cuando queda libre donde está (`ForgetOriginal`, p. ej. al borrar su panel) o cuando se cierra (`WindowClosed` emite `ForgetOriginal`). Hasta el 2026-11-10 las dos últimas no la quitaban.
