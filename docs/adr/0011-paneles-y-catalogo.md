# ADR 0011 — Paneles: entidades con nombre, ubicación y presentación; ventanas libres; catálogo por detección

- **Estado:** Aceptada (2026-11-10)
- **Fecha:** 2026-11-10

## Contexto

Hasta ahora una ventana gestionada pertenece a uno de dos hogares fijos (`Work` o `Context`) y a un monitor, y cada panel tiene un template (Tiles, Pila o Fill). Para el uso real hace falta más:

- crear paneles con el nombre que se quiera, renombrarlos y llevarlos a otros monitores, incluida una tablet;
- presentar cada panel de forma distinta: tiles, una vista tipo Explorador, pestañas, colapsables, una app a pantalla completa en el panel, el deck;
- reconocer y abrir apps sin escanear todo lo instalado.

## Decisión

### 1. Los paneles son entidades (crear, renombrar, mover, borrar)

Cada panel tiene tres propiedades independientes:

| Propiedad | Valores |
| --- | --- |
| **Nombre** | Libre y renombrable. |
| **Ubicación** | Monitor principal, en una columna a un lado del foco (la vista decide cuál, [ADR 0012](0012-vistas-posicion-del-foco.md)); un monitor auxiliar ([ADR 0008](0008-centro-solo-en-monitor-principal.md)); una tablet o monitor táctil ([ADR 0009](0009-tablets-como-monitores-auxiliares.md)). |
| **Presentación** | Ver el punto 2. |

- Por defecto existen **dos paneles**, uno a cada lado del foco. Se crean con los nombres de hoy, "Contexto" y "Trabajo", y se pueden renombrar.
- **Como máximo dos paneles por columna**, uno encima del otro. Tres ya queda forzado.
- Cada panel tiene su **proporción** dentro de su columna o monitor: presets más ajuste arrastrando el borde, que se guarda (como en ADR 0012).
- Borrar un panel deja sus ventanas **libres** (punto 3) y no las mueve.

### 2. Presentaciones

| Presentación | Qué hace | Estado |
| --- | --- | --- |
| **Tiles** | Las ventanas se reparten el panel. Cada una puede tener un **tamaño de tile** (pequeño, mediano, ancho o grande) y se colocan solas según el ancho disponible, como un `flex-wrap`. Si no caben, un "scroll" del panel cambia qué ventanas se ven; las demás quedan minimizadas y se alcanzan desde la tira. | Hoy existe como filas. |
| **Lista** (vista tipo Explorador) | El panel muestra una vista dibujada por FocusWShell con sus apps, con modos como los del Explorador: iconos grandes con miniatura en vivo, lista o detalles. Elegir una la muestra en el panel. | Nueva. |
| **Pestañas** | Una ventana visible y las demás detrás, con su tira de pestañas. | Hoy, "Pila". |
| **Colapsables** | Una ventana expandida y las demás como encabezados de una línea. | Prevista. |
| **Pantalla completa en el panel** | Una app ocupa el panel o el monitor entero, por ejemplo Spotify en una tablet. Con pestañas si hay más. | Hoy, "Fill". |
| **Mixto** | Composición de las anteriores; por ejemplo, una ventana fija arriba y pestañas debajo. | Prevista. |
| **Deck** | El panel aloja el deck ([ADR 0013](0013-deck-superficie-de-control.md)) y no coloca ventanas. | Nueva. |

Los tiles adaptables y la lista tipo Explorador se concretan en el [ADR 0015](0015-tiles-adaptables-y-lista.md) (propuesta).

### 3. Las ventanas empiezan libres

- Una ventana que FocusWShell no gestiona es **libre**: el gestor no la mueve. Es el estado de partida de toda ventana.
- Se asigna a un panel cuando el usuario lo decide (desde la UI, una tira o el deck) y se puede volver a dejar libre (`Release`).
- No hay un panel "General": la lista de apps la da el catálogo (punto 4).
- **Más adelante:** "fijar encima", una ventana libre pequeña y siempre visible (tipo picture-in-picture, para una videollamada o un temporizador), como acción opcional sobre una ventana libre.

### 4. Escenas y paneles

- Aplicar una escena **suma** sus paneles a los del usuario; no borra ninguno.
  - Un panel que ya existe (mismo identificador) conserva su nombre y su ubicación, y toma la presentación de la escena.
  - Un panel nuevo se añade. Si va a una columna del principal que ya tiene panel, sus ventanas van a ese panel.
- Una app de la escena cuyo panel no se puede resolver queda **libre**: flota sin gestión.
- Al arrancar, los paneles guardados en `panels.json` **sustituyen** a los de por defecto (`ReplacePanels`).

### 5. Catálogo de apps por detección suave

- Cada vez que FocusWShell ve una ventana elegible de una app que no conocía, la añade al catálogo (`apps.json`). Por cada app guarda:
  - la clave (`AppKey`, el AUMID o el ejecutable);
  - el nombre y el icono;
  - **cómo abrirla**: la ruta del ejecutable o el AUMID;
  - el último panel en el que estuvo.
- No guarda títulos de ventanas. No se escanea lo instalado.
- Desde el catálogo se puede abrir una app cerrada. Su ventana, al aparecer, se asigna al panel elegido.
- **Al aplicar una escena se abren las apps que faltan** (casilla "Abrir las que falten", activada por defecto). La escena se aplica en el momento con lo que está abierto; las que faltan se abren en segundo plano y cada ventana nueva va a su panel, o al foco o estacionada si así estaba en la escena. Se espera hasta 30 s. Si su panel no se resuelve, la ventana queda libre.
- En la ventana principal, cada app del catálogo muestra su **icono** (sacado del ejecutable, sin guardarlo), su nombre se puede **editar** y se puede **ocultar** (sigue registrada, así que no vuelve a aparecer como nueva; "Ver ocultas" las muestra).
- Asignar sola cada ventana nueva de una app a "su" panel es una **regla opcional** para más adelante (M4). Por defecto nada se mueve sin una acción del usuario (CA7).

## Consecuencias

- **El modelo cambia:** `Home` (`Work`/`Context`) pasa a ser una referencia a un panel. `Display` y los templates pasan a ser propiedades del panel. Hay que migrar las escenas guardadas, que hoy guardan Contexto o Trabajo y un monitor, a los paneles por defecto.
- Los motores de layout reciben paneles con ubicación y presentación en lugar de "izquierda/derecha":
  - el principal, columnas según la vista, hasta dos paneles por columna;
  - los auxiliares, paneles por monitor.
- La presentación "Lista" y el deck son ventanas propias de FocusWShell (sin foco, como las tiras).
- Abrir apps tiene casos difíciles: apps que tardan en mostrar ventana, pantallas de bienvenida, o una sola instancia ya abierta en otro escritorio. Se espera a la primera ventana elegible de ese proceso o AUMID, con un plazo.
- **Privacidad:** el catálogo guarda nombres de apps y rutas de ejecutables, no contenido. Es local, en `%LOCALAPPDATA%`.
