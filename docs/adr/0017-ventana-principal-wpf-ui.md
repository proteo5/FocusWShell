# ADR 0017 — La ventana principal con WPF-UI: configurar y ver el estado

- **Estado:** Aceptada (2026-11-12)
- **Fecha:** 2026-11-12

## Contexto

La ventana principal se hizo en M0, cuando era la única forma de manejar FocusWShell, con los controles de WPF por defecto. Con el deck como superficie del día a día ([ADR 0013](0013-deck-superficie-de-control.md)) quedó así:

- **Una fila de controles sin relación entre sí** que se doblaba: la vista, el deck, los atajos, las escenas y el idioma.
- **Un encabezado con datos de depuración:** rectángulos de área de trabajo y DPI.
- **Una barra de diez botones** que actuaban sobre la fila elegida, y «Aplicar» después de cambiar un panel.
- **Una consola** con los mensajes en Consolas.

El usuario la describió como «una app de los 90». Para la alfa, quien la abre por primera vez necesita entender qué es y cómo está todo.

## Decisión

1. **La ventana principal es para configurar y ver el estado**, no para manejar ventanas una por una: eso es el deck.
2. **Navegación lateral, como la Configuración de Windows 11:** Inicio, Ventanas, Paneles, Apps, Atajos y gestos, Escenas, Excluidas y General. Una sección a la vez.
3. **Inicio** reúne lo que más se mira: un mapa de los monitores (las columnas del principal en el orden y el ancho de la disposición, lo que hay en el foco, cuántas apps tiene cada panel), los interruptores de la disposición, acciones rápidas y el atajo de emergencia.
4. **Elegir el panel de una ventana se aplica al momento.** Las demás acciones van en el menú «…» de cada fila (el mismo del clic derecho).
5. **Los avisos van en una franja arriba que se cierra sola**, y sólo dicen lo que no se pudo hacer, las notas y lo que no quedó en su sitio; sin el detalle en píxeles.
6. **Con [WPF-UI](https://github.com/lepoco/wpfui)** (MIT), en vez de estilos propios: controles de Windows 11 (barra de título, fondo Mica, interruptores, tarjetas, franjas de aviso, iconos Fluent) con mucho menos código. La ventana principal no necesita la precisión táctil del deck.
7. **El tema** es el de Windows (y lo sigue) o, si se elige en General, claro u oscuro. Se guarda en `settings.json` (`Theme`).

### Cómo se carga WPF-UI sin cambiar el resto

- **Los estilos de los controles (`ControlsDictionary`) sólo en la ventana principal.** El deck, las tiras y las listas tipo Explorador se dibujan a mano con sus colores (la regla de color del deck); un estilo global les cambiaría el aspecto.
- **A nivel de aplicación, sólo valores:** los colores del tema (`ThemesDictionary`), la fuente de los iconos (`Fonts.xaml`) y los tamaños (`Variables.xaml`). Los controles de WPF-UI los buscan en los recursos de la aplicación: sin ellos, crear un `SymbolIcon` falla.
- **El foco en el mapa de monitores** usa el azul del deck (#2878DC) con texto blanco, no el acento del tema: así es igual en los dos temas y coincide con el deck.

## Consecuencias

- **A favor:** la ventana se ve y se usa como una app de Windows 11; un usuario nuevo encuentra cada cosa en su sección; menos código de estilos propio.
- **En contra:** una dependencia externa que hay que mantener al día. Si WPF-UI cambia mucho o se abandona, la ventana principal tendría que volver a estilos propios; el deck no depende de ella.
- La ventana «Cambiar atajos…» y los cuadros de confirmación siguen con el aspecto de Windows clásico.
- `FocusWShell.exe --main-preview <carpeta> [es|en] [dark|light]` dibuja cada sección a un PNG, con los datos reales y sin mover nada, para revisar el diseño. Las secciones Ventanas y Apps muestran títulos y apps reales: no se publican.
