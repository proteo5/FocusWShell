# Changelog

All notable changes to FocusWShell. Each release has its notes in [`.github/release-notes/`](.github/release-notes/).

Todos los cambios importantes de FocusWShell. Cada versión tiene sus notas en [`.github/release-notes/`](.github/release-notes/).

## Unreleased / Sin publicar

- New: the deck on the network. FocusWShell can serve the deck as a web page on your local network, to use it from a tablet or a phone without Apollo (Android and iPad, nothing to install). Off by default, it only listens on networks Windows marks as private, and each device is paired once with a QR code and can be removed at any time. / Nuevo: el deck en la red. FocusWShell puede servir el deck como página web en tu red local, para usarlo desde una tablet o un teléfono sin Apollo (Android y iPad, sin instalar nada). Apagado por defecto, sólo escucha en redes que Windows marca como privadas, y cada dispositivo se empareja una vez con un código QR y se puede quitar cuando quieras.

## 0.6.0-alpha

The deck shows every monitor at once, and apps that FocusWShell could not see now show up. / El deck muestra todos los monitores a la vez, y aparecen apps que FocusWShell no veía.

- The deck shows every monitor at once, one above the other, without tabs: every panel is one tap away, and when moving an app all panels are targets. Pages moved to the right of Free. To go back to one tab per monitor, turn off "Deck: all monitors at once" in General. / El deck muestra todos los monitores a la vez, uno arriba del otro, sin pestañas: todos los paneles están a un toque, y al mover una app todos los paneles son destino. Páginas pasó a la derecha de Libres. Para volver a una pestaña por monitor, apaga «Deck: todos los monitores a la vez» en General.
- Fixed: Store apps whose window does not give its app ID (WhatsApp) showed an internal name ("WhatsApp.Root") and a generic icon. They now take the name and icon of the Start menu. / Corregido: las apps de la Store cuya ventana no da su id de app (WhatsApp) salían con un nombre interno («WhatsApp.Root») y un icono genérico. Ahora toman el nombre y el icono del menú Inicio.
- The deck now shows the apps that run as administrator (SADP, for example), in Free, dimmed and with 🛡: FocusWShell can't move them, but tapping one brings it to the front. Borderless apps with a minimize button (Qt) are no longer ignored. / El deck ahora muestra las apps que corren como administrador (SADP, por ejemplo), en Libres, atenuadas y con 🛡: FocusWShell no puede moverlas, pero tocarlas las trae al frente. Las apps sin marco con botón de minimizar (Qt) ya no se ignoran.

Notes / Notas: [v0.6.0-alpha](.github/release-notes/v0.6.0-alpha.md)

## 0.5.0-alpha

Sites in Activity, private windows kept private, and apps that go back to their panel however you open them. / Sitios en Actividad, ventanas privadas que se quedan privadas, y apps que vuelven a su panel las abras como las abras.

- Fixed: after restarting, apps you opened yourself more than 3 minutes later stayed free, even pinned ones. Now a pinned app goes to its panel however you open it, and the apps of the last session go back to their panel the first time they appear. A window you free yourself is not moved. / Corregido: tras reiniciar, las apps que abrías tú más de 3 minutos después quedaban libres, aunque estuvieran ancladas. Ahora una app anclada va a su panel la abras como la abras, y las apps de la última sesión vuelven a su panel la primera vez que aparecen. Una ventana que tú liberas no se mueve.
- Activity can also measure sites: under Chrome and Edge, the time of each site (github.com). It is a separate switch, off by default; only the site is saved, never the page or its title. / La actividad puede medir también los sitios: debajo de Chrome y Edge, el tiempo de cada sitio (github.com). Es un interruptor aparte, apagado por defecto; sólo se guarda el sitio, nunca la página ni su título.
- Private windows (incognito, InPrivate) count as "Private browsing", with no site, and 🗂 Pages no longer lists their tabs: it shows "Private window · N tabs". / Las ventanas privadas (incógnito, InPrivate) cuentan como «Navegación privada», sin sitio, y 🗂 Páginas ya no lista sus pestañas: muestra «Ventana privada · N pestañas».

Notes / Notas: [v0.5.0-alpha](.github/release-notes/v0.5.0-alpha.md)

## 0.4.0-alpha

Activity: how long each app spends in focus and in use, and the deck shows the app you type in. / Actividad: cuánto pasa cada app en el foco y en uso, y el deck muestra la app en la que escribes.

- New Activity section in the main window: how long each app spends in focus and in use, and the time side by side (one app in focus, the keyboard in another). It is off until you turn it on, and it stays on your PC. / Nueva sección Actividad en la ventana principal: cuánto pasa cada app en el foco y en uso, y el tiempo en paralelo (una app en el foco, el teclado en otra). Está apagada hasta que la prendas, y se queda en tu equipo.
- In the deck, the focus tile also shows the app in use when it is another one, and no longer shows the window title. / En el deck, el tile del foco muestra también la app en uso cuando es otra, y ya no muestra el título de la ventana.
- In the deck, in a panel in tabs (or Fill), tapping a hidden tab shows it in its panel and gives it the keyboard, without moving it to the focus; tapping the tab you see (underlined) brings it to the focus. The icons keep their place. / En el deck, en un panel en pestañas (o Llenar), tocar una pestaña oculta la muestra en su panel y le da el teclado, sin moverla al foco; tocar la que se ve (subrayada) la lleva al foco. Los iconos no cambian de lugar.
- The app that leaves the focus comes back to a panel in tabs as the tab you see, instead of sometimes hiding behind another. / La app que sale del foco vuelve a un panel en pestañas como la pestaña que se ve, en lugar de quedar a veces oculta detrás de otra.

Notes / Notas: [v0.4.0-alpha](.github/release-notes/v0.4.0-alpha.md)

## 0.3.1-alpha

Your apps come back to their panels after restarting Windows. / Tus apps vuelven a sus paneles después de reiniciar Windows.

- Fixed: after restarting Windows, apps didn't go back to their panels. The session is now saved when shutdown starts, and not again while Windows closes the apps; it also keeps the apps that take a while to open, the ones in the tray, and the ones that swap their window at startup (Docker Desktop). / Corregido: tras reiniciar Windows, las apps no volvían a sus paneles. La sesión se guarda al empezar el apagado, y no otra vez mientras Windows cierra las apps; además conserva las apps que tardan en abrirse, las de la bandeja y las que cambian de ventana al arrancar (Docker Desktop).
- Fixed: the panels of a tablet connected with Apollo disappeared, because it gets a new ID on every connection. A monitor of the same model that comes back with another ID is now recognized. / Corregido: los paneles de una tablet conectada con Apollo desaparecían, porque estrena identificador en cada conexión. Ahora se reconoce un monitor del mismo modelo que vuelve con otro.
- Fixed: after a restart, FocusWShell no longer says it wasn't closed properly. / Corregido: tras un reinicio, FocusWShell ya no dice que no se cerró adecuadamente.
- Web apps installed from Chrome or Edge (Google Chat) show with their own name and open themselves, not the browser. / Las apps web instaladas desde Chrome o Edge (Google Chat) salen con su nombre y se abren ellas, no el navegador.
- The deck: panel borders easier to see, and bigger panel and app names. / El deck: bordes de los paneles más visibles, y nombres de paneles y apps más grandes.

Notes / Notas: [v0.3.1-alpha](.github/release-notes/v0.3.1-alpha.md)

## 0.3.0-alpha

A deck that puts the focus first, apps in the tray, and a step-by-step manual. / Un deck que pone el foco primero, apps en la bandeja y un manual paso a paso.

- Manual: a picture of how it works, a step-by-step getting started from the main window, step-by-step guides, and screenshots of every section of the main window. / Manual: un dibujo de cómo funciona, primeros pasos paso a paso desde la ventana principal, guías paso a paso y capturas de cada sección de la ventana principal.
- Fixed: in the dark theme, the list in Windows was white with white text. / Corregido: en el tema oscuro, la lista de Ventanas salía blanca con el texto blanco.
- Apps that close to the tray (Codex, Discord, Teams…) show dimmed with ▴ in their panel, and tapping them opens the app again, which shows its window. Before, FocusWShell showed the hidden window itself, and some apps stayed frozen. When the app shows its window again, it goes back to its panel and to the focus. / Las apps que se cierran a la bandeja (Codex, Discord, Teams…) salen atenuadas con ▴ en su panel, y tocarlas abre la app otra vez, que muestra su ventana. Antes FocusWShell mostraba la ventana oculta por su cuenta, y algunas apps quedaban congeladas. Cuando la app vuelve a mostrar su ventana, regresa a su panel y al foco.
- The deck gives the focus app its own place: on a whole tablet, a big tile at the top, above the buttons; in its panel, a blue border. / El deck le da a la app del foco su lugar propio: en la tablet entera, un tile grande arriba, encima de los botones; en su panel, un borde azul.
- New Minimize… mode in the deck (teal), and the four modes in a 2×2 grid. Free all moves up with Previous and To panel; Mouse to focus moves to System. / Nuevo modo Minimizar… en el deck (verde azulado), y los cuatro modos en una cuadrícula de 2×2. Liberar todas sube junto a Anterior y Al panel; Ratón al foco pasa a Sistema.
- "Park" is now called "Minimize" everywhere. / «Estacionar» ahora se llama «Minimizar» en todos lados.
- The main monitor is called "main" in English, and the Spanish buttons say "foco" instead of "Centro". / El monitor principal se llama «main» en inglés, y los botones en español dicen «foco» en lugar de «Centro».

Notes / Notas: [v0.3.0-alpha](.github/release-notes/v0.3.0-alpha.md)

## 0.2.0-alpha

A modern main window. / Una ventana principal moderna.

- Redesigned with WPF-UI, in the style of Windows 11: side navigation, Home with a map of your monitors, the layout toggles and quick actions. / Rediseñada con WPF-UI, al estilo de Windows 11: navegación lateral, Inicio con un mapa de tus monitores, los interruptores de la disposición y acciones rápidas.
- Choosing a window's panel applies right away; its other actions are in «…». / Elegir el panel de una ventana se aplica al momento; sus demás acciones están en «…».
- Light or dark theme, or the one of Windows. / Tema claro u oscuro, o el de Windows.
- General: language, theme, start with Windows, keep the screen on, and an About page with links and licenses. / General: idioma, tema, iniciar con Windows, pantalla encendida y «Acerca de» con enlaces y licencias.
- Short messages in a strip that closes by itself, instead of a console. / Avisos cortos en una franja que se cierra sola, en lugar de una consola.

Notes / Notas: [v0.2.0-alpha](.github/release-notes/v0.2.0-alpha.md)

## 0.1.0-alpha

First release, for a small group of testers. / Primera versión, para un grupo pequeño de testers.

- Focus, panels and free windows, on several monitors. / Foco, paneles y ventanas libres, en varios monitores.
- Main screen layout: panel columns that turn on and off, with focus only. / Disposición del principal: columnas de paneles que se prenden y apagan, con solo foco.
- The deck, for a tablet, a touch screen or a corner of the screen. / El deck, para una tablet, una pantalla táctil o una esquina de la pantalla.
- Browser pages: Chrome and Edge tabs grouped by site. / Páginas de los navegadores: pestañas de Chrome y Edge agrupadas por sitio.
- Recovery journal and emergency shortcut. / Diario de recuperación y atajo de emergencia.
- English and Spanish. / Inglés y español.

Notes / Notas: [v0.1.0-alpha](.github/release-notes/v0.1.0-alpha.md)
