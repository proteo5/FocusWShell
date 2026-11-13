---
layout: default
lang: es
ref: concepts
title: Cómo funciona
permalink: /es/conceptos/
---

# Cómo funciona

## Otra forma de usar tu PC

Sin FocusWShell, cada ventana flota encima de las demás: abres, encimas, minimizas y buscas con Alt+Tab la que necesitas. FocusWShell le da a cada ventana un **sitio** y un **papel**:

![El monitor principal con el foco en medio y los paneles Contexto y Trabajo; otro monitor con un panel; una tablet con el deck; y las ventanas libres aparte]({{ '/assets/img/es/concept.svg' | relative_url }})

- **Una app en el foco**, grande, en medio de tu monitor principal. Es en la que trabajas ahora.
- **Las demás en paneles**, alrededor del foco y en tus otros monitores, siempre visibles y siempre en el mismo sitio.
- **El deck**, en una tablet o en una esquina, para cambiar entre ellas con un toque.
- **Las ventanas libres**, que FocusWShell no toca.

Lo que cambia en el día a día:

| Antes… | Con FocusWShell |
| --- | --- |
| Alt+Tab entre un montón de ventanas | Alt+Tab (o un toque en el deck) lleva la app al foco, y la anterior vuelve a su panel |
| Maximizar la ventana en la que trabajas | Ya es grande: está en el foco |
| Minimizar lo que estorba | Nada estorba: lo demás está en su panel |
| Arrastrar y redimensionar para ver dos a la vez | Cada panel muestra sus apps lado a lado |
| Perder el chat detrás del navegador | El chat está siempre en su panel, en el mismo sitio |

Los primeros días se siente raro no acomodar las ventanas tú. La idea es justamente dejar de hacerlo.

## Foco, paneles y ventanas libres

| | Qué es | Dónde va |
| --- | --- | --- |
| **Foco** | La app en la que trabajas. Sólo hay una. | La zona grande del monitor principal. |
| **Paneles** | Grupos con nombre de las apps que tienes a mano: *Contexto*, *Trabajo*, *Chat*… | Columnas junto al foco, y tus otros monitores. |
| **Libres** | Ventanas que FocusWShell no organiza. | Donde las dejes. |

Cuando llevas una app al foco, la que estaba **vuelve a su panel**. Una ventana nunca cambia de lugar por sí sola: un mensaje del chat, una notificación o un clic no la mueven.

![Tocas Correo: va al foco, y el Editor vuelve a su panel]({{ '/assets/img/es/swap.svg' | relative_url }})

Las apps de un panel también pueden estar:

- **Estacionadas:** abiertas pero apartadas (minimizadas). Vuelven desde su panel.
- **Fuera de layout:** moviste o redimensionaste una a mano; FocusWShell la deja donde la pusiste hasta que la devuelvas.

## La disposición del principal

Tu monitor principal tiene dos columnas de paneles, una a cada lado del foco: **Contexto** a la izquierda y **Trabajo** a la derecha. Prende o apaga cada una, y el foco ocupa el resto:

| Columnas prendidas | Pantalla |
| --- | --- |
| Las dos | Contexto · **foco** · Trabajo |
| Sólo Contexto | Contexto · **foco** |
| Sólo Trabajo | **foco** · Trabajo |
| Ninguna | **foco** en toda la pantalla (solo foco) |

Con una columna apagada, sus apps se minimizan y esperan en su panel; tocar una la lleva al foco.

En un monitor **ultrawide** (21:9 o más ancho) hay dos más: con un lado apagado, un botón extra trae la otra columna a ese lado, así los dos paneles quedan juntos y el foco se va al borde: *Contexto · Trabajo · foco*, o *foco · Trabajo · Contexto*.

Cada disposición recuerda sus anchos. Arrastra el borde entre dos ventanas para cambiarlos; **Restablecer ancho**, en el menú de los botones de la disposición, vuelve al de por defecto.

## Presentaciones de un panel

Cada panel muestra sus apps de una de estas formas. Se cambia en la cabecera del panel, en el deck.

- **▦ Tiles:** todas las apps a la vista, una junto a otra. Las que no caben pasan a filas que se recorren desde la tira del panel.
- **⧉ Pestañas:** una app a la vez, las demás detrás. Se cambia con la tira o con ◀ ▶ en el deck.
- **☰ Lista:** una lista con miniaturas, como el Explorador. También muestra las apps cerradas ancladas a ese panel.
- **⛶ Pantalla completa:** en los monitores que no son el principal, la app del panel ocupa el monitor.

## Tiras

Encima de cada panel hay una **tira** fina con una pestaña por app:

- **Clic:** la muestra en el panel (o la recupera si estaba estacionada).
- **Doble clic:** la lleva al foco.
- **Clic derecho:** todo lo demás.

## Varios monitores

El monitor principal tiene el foco y sus dos columnas. Cada uno de los demás puede tener sus propios paneles, hasta dos por lado. Si un monitor se desconecta (una tablet por wifi, una laptop que sale de su base), sus apps quedan a salvo; cuando vuelve, sus paneles vuelven con él.

## Siempre reversible

Antes de mover cualquier ventana, FocusWShell apunta dónde estaba. **Salir**, el atajo de emergencia <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Mayús</kbd>+<kbd>Retroceso</kbd> y volver a abrirlo tras una caída devuelven todas las ventanas.

## Sorpresas frecuentes

**«Mi ventana se movió cuando la elegí con Alt+Tab.»** Es la idea: elegir una ventana que está en un panel la lleva al foco. Para quitarlo en Alt+Tab, la Vista de tareas o la barra de tareas, ve a **Atajos y gestos** en la ventana principal.

**«Arrastré una ventana y volvió.»** Las ventanas de un panel conservan su sitio. Suéltala sobre el foco o sobre otro panel para llevarla ahí; para acomodarla tú, libérala primero.

**«Desapareció una app.»** Puede que su columna esté apagada (sus apps esperan minimizadas en su panel), que esté estacionada o que esté detrás de otra en un panel con pestañas. Búscala en el deck o en **Ventanas**: tocarla la trae al foco.

**«Una ventana no sale en la lista.»** Las apps que se ejecutan como administrador no se pueden mover y salen al final con el motivo. Las apps excluidas no salen: ve a **Excluidas** en la ventana principal.

**«Todo está revuelto.»** <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Mayús</kbd>+<kbd>Retroceso</kbd> devuelve todas las ventanas a su sitio. **Reordenar todas**, en **Inicio** o en el deck, las vuelve a poner en sus paneles.
