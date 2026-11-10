# ADR 0014 — Mapa de controles de la app del foco en el deck

- **Estado:** Propuesta (idea registrada; pendiente del deck y del spike S9)
- **Fecha:** 2026-11-10

## Contexto

Con el deck ([ADR 0013](0013-deck-superficie-de-control.md)) la mano está en la tablet o en el monitor táctil, y volver al ratón para llegar a una caja de texto o a un checkbox de la app del foco cuesta movimientos. La idea es mostrar en el deck un **esqueleto** de la app del foco (al estilo de un *skeleton screen*): sólo las formas de sus controles, sin el contenido. Un toque sobre una forma actúa sobre ese control.

## Decisión (propuesta)

1. **Fuente:** UI Automation sobre la ventana del foco. Da los controles interactivos (`Edit`, `CheckBox`, `Button`, `ComboBox`, `Hyperlink`, `ListItem`…) con su rectángulo y su nombre. Sólo se leen los que están a la vista.
2. **Acción al tocar:** se usan los patrones de UI Automation en lugar de mover el ratón:
   - a una caja de texto se le da el foco del teclado (`SetFocus`);
   - un checkbox se marca o desmarca (`Toggle`);
   - un botón se pulsa (`Invoke`);
   - un combo se abre (`ExpandCollapse`).

   Mover el cursor al control queda como modo opcional.
3. **Presentación:**
   - un **mapa** a escala con las zonas táctiles agrandadas;
   - una **lista por tipo** ("campos de texto", "casillas", "botones"), para cuando el mapa queda demasiado pequeño.
4. **Privacidad:** se muestran formas y etiquetas, nunca valores. Los campos de contraseña (`IsPassword`) se omiten. Nada se guarda en disco.

## Consecuencias

- Funciona bien con apps nativas (Win32, WinForms, WPF, WinUI).
- Con Chromium y Electron funciona, pero su árbol es muy grande y se construye bajo demanda, con coste de rendimiento.
- No funciona con apps dibujadas en lienzo ni con juegos.
- Las apps elevadas no se pueden leer, por la misma razón que no se pueden mover (R5).
- Hay que limitar y guardar en caché la lectura del árbol y actualizarla con los avisos de cambio de la app.
- Precedente: *Hunt and Peck* (Windows, código abierto) etiqueta con UI Automation todo lo que se puede pulsar en la ventana activa.
- **Spike S9:** medir cuánto tarda leer los controles visibles de las apps reales del usuario (Edge, Slack, SSMS, VS Code) y si los toques llegan al control correcto.
