---
layout: default
lang: es
ref: deck
title: El deck
permalink: /es/deck/
---

# El deck

El deck es la superficie de control de FocusWShell: todo lo que puedes hacer, con botones grandes para el dedo. Nunca le quita el teclado a la app en la que trabajas, así que puedes usarlo mientras escribes.

¿Empiezas con FocusWShell? Configúralo primero con [Primeros pasos]({{ '/es/primeros-pasos/' | relative_url }}).

Ábrelo u ocúltalo con <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Mayús</kbd>+<kbd>D</kbd> o con **Deck** en el menú de la bandeja. En una esquina del principal se oculta solo cuando eliges algo; mandado a un monitor, se queda.

![El deck]({{ '/assets/img/es/deck.png' | relative_url }})

## Foco

La app del foco tiene su lugar propio. En la tablet entera es un tile grande arriba, bajo el nombre de FocusWShell y encima de los botones, con su icono, su nombre y el panel del que viene. Cuando escribes en otra app (está **en uso**), esa sale a su lado, más pequeña y con borde blanco. En el deck angosto está arriba. Tócala para abrir su menú; en un modo, funciona como cualquier otra app.

En su panel, la app del foco lleva un borde azul y **↑**.

### Acciones

- **Anterior:** trae de vuelta la app que estaba antes en el foco. Otra vez, y vuelves.
- **Al panel:** devuelve la app del foco a su panel y deja el foco vacío.
- **Liberar todas:** deja que todas las ventanas vuelvan a donde estaban. Pide un segundo toque. **Reordenar todas**, en **Sistema**, lo deshace.

### Botones de modo

**Mover…**, **Anclar…**, **Minimizar…** y **Cerrar…** no actúan al momento: esperan a que toques una app, como Mayús espera una tecla. Los «…», la barra de color debajo y su sitio los distinguen.

- **Mover…** (amarillo): toca una app y luego el panel donde va. Todos los paneles de todos los monitores son destino a la vez, y el foco también: lleva la app al foco.
- **Anclar…** (índigo): toca una app para anclarla a su panel. Una app anclada sigue en su panel aunque esté cerrada, atenuada, y tocarla la abre ahí.
- **Minimizar…** (verde azulado): toca una app para minimizarla en su panel. Si ya estaba minimizada, vuelve. El modo se apaga después de un toque, como **Cerrar…**.
- **Cerrar…** (rojo): toca una app para cerrarla, como con su ✕. Si tiene algo sin guardar, la app pregunta.

Toca otra vez el botón del modo para cancelar.

## Disposición del principal

Prende o apaga las columnas de **Contexto** y **Trabajo** del monitor principal (azul: prendida). En un ultrawide, aparece un **⇠** o **⇢** extra cuando un lado está apagado. Ver [la disposición del principal]({{ '/es/conceptos/#la-disposición-del-principal' | relative_url }}). Deja apretado un botón para **Restablecer ancho**.

![Trabajo apagado en un ultrawide: el botón extra lo trae a la izquierda]({{ '/assets/img/es/deck-layout.png' | relative_url }})

## Monitores y paneles

Se ven todos los monitores a la vez, uno arriba del otro: su nombre va en vertical a la izquierda, y sus paneles van lado a lado en su orden real (izquierda, derecha). Si prefieres lo de antes, una pestaña por monitor, apaga **Deck: todos los monitores a la vez** en General. Cada panel muestra sus apps:

- **Toca una app:** al foco. Una libre sólo se trae al frente, y sigue libre.
- **En un panel en pestañas (o Llenar):** la pestaña que se ve va subrayada. Toca una oculta para verla en su panel y escribir en ella, sin moverla al foco: trabajas en el foco y en ella a la vez. Toca la que se ve para llevarla al foco. Los iconos no cambian de lugar, y la app que sale del foco vuelve como la pestaña que se ve.
- **Déjala apretada (o clic derecho):** su menú: al foco, pantalla completa en un monitor, minimizar, mover a otro panel, liberarla, anclarla.
- **Arrastra** una app a otro panel para moverla, o a **Libres** para soltarla.
- La cabecera del panel tiene su [presentación]({{ '/es/conceptos/#presentaciones-de-un-panel' | relative_url }}) (▦ ⧉ ☰ ⛶) y, en pestañas, ◀ ▶.

Marcas en una app:

| Marca | Significa |
| --- | --- |
| Borde azul, ↑ | En el foco. |
| ⏸ | Minimizada. |
| ↗ | Fuera de layout. |
| Atenuada | Cerrada (anclada), o su columna está apagada. |
| ⧉ | Abierta en otro escritorio virtual; tocarla te lleva allí. |
| ▴ | En la bandeja: la app ocultó su ventana al cerrarla, pero sigue abierta. Tocarla abre la app otra vez, y la app muestra su ventana en el foco. |
| Borde blanco | En uso: tiene el teclado y el ratón, pero no es la del foco. |
| 🛡, atenuada (en Libres) | Corre como administrador: FocusWShell no puede moverla. Tocarla la trae al frente. |

**🗂 Páginas**, a la derecha de Libres, muestra las pestañas de tus navegadores. Ver [Páginas de los navegadores]({{ '/es/paginas/' | relative_url }}).

## Libres

Ventanas que FocusWShell no organiza. Toca una para traerla al frente; usa **Mover…** para ponerla en un panel.

## Sistema

- **Pantalla encendida:** evita que la pantalla se apague y que el PC se suspenda.
- **Energía:** bloquear, suspender, reiniciar, apagar o cerrar sesión. Reiniciar, apagar y cerrar sesión piden un segundo toque.
- **Ratón al foco:** lleva el puntero a la app del foco. Después de usar una tablet, el puntero se queda en ella; esto lo devuelve a donde trabajas.
- **Reordenar todas:** después de **Liberar todas**, vuelve a poner cada ventana en su panel.

## Dónde vive el deck

**⇢**, arriba en el deck, lo manda a:

- **una esquina del monitor principal** (se oculta solo), o
- **otro monitor**, a una mitad o entero. Con una mitad, los paneles de ese monitor usan la otra.

En un deck estrecho, los botones se reacomodan y los interruptores de la disposición pasan arriba.

![El deck en media tablet]({{ '/assets/img/es/deck-narrow.png' | relative_url }})
