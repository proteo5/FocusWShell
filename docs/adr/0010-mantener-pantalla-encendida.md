# ADR 0010 — FocusWShell incluye "mantener la pantalla encendida"

- **Estado:** Aceptada
- **Fecha:** 2026-11-09

## Contexto

El usuario mantiene [Prevent-Screen-Saver](https://github.com/proteo5/Prevent-Screen-Saver), una app de bandeja que impide que se active el protector de pantalla mientras está activa. Es útil cuando se lee, se supervisa o se revisa sin tocar el teclado ni el ratón. Se usa justo en los mismos escenarios que los monitores auxiliares de monitoreo ([ADR 0008](0008-centro-solo-en-monitor-principal.md)). Tener dos apps de bandeja para la misma sesión de trabajo sobra.

Prevent-Screen-Saver usa `SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED)`. Esa llamada vale mientras viva el hilo que la hizo y no deja rastro de quién mantiene la pantalla encendida.

## Decisión

- FocusWShell ofrece **"Mantener la pantalla encendida"**:
  - un interruptor en el menú de la bandeja;
  - el atajo `Ctrl+Alt+Mayús+P`;
  - un icono que cambia mientras está activo.
- Se implementa con **`PowerCreateRequest` + `PowerSetRequest(PowerRequestDisplayRequired)`**:
  - No depende de un hilo concreto.
  - La petición aparece en `powercfg /requests` con el motivo "FocusWShell: mantener la pantalla encendida", así el usuario puede ver quién mantiene la pantalla encendida.
  - Al desactivarlo se llama a `PowerClearRequest`; si el proceso muere, Windows retira la petición solo.
- El estado se recuerda entre sesiones en `settings.json`.
- No simula teclado ni ratón: el comportamiento es explícito y reversible, como en la app original.

## Consecuencias

- Prevent-Screen-Saver puede seguir existiendo como herramienta independiente. Quien use FocusWShell no la necesita.
- No impide la suspensión del equipo si el usuario la fuerza (cerrar la tapa, menú de apagado): sólo evita que la pantalla se apague y el protector de pantalla salte por inactividad.
- Más adelante una escena podrá activarlo sola (por ejemplo, una escena de "monitoreo"). Será opcional y se explicará, como toda automatización.
