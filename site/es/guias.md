---
layout: default
lang: es
ref: guides
title: Guías paso a paso
permalink: /es/guias/
---

# Guías paso a paso

Recetas cortas para lo que querrás hacer una vez que FocusWShell esté funcionando. Si apenas empiezas, haz primero [Primeros pasos]({{ '/es/primeros-pasos/' | relative_url }}).

- [Configurar un monitor normal](#monitor-normal)
- [Configurar un ultrawide](#ultrawide)
- [Poner paneles en otro monitor](#otro-monitor)
- [Usar una tablet como deck](#tablet)
- [Crear, renombrar o mover un panel](#paneles)
- [Cambiar cómo un panel muestra sus apps](#presentacion)
- [Dejar una app en su panel aunque esté cerrada](#anclar)
- [Guardar tu configuración como escena](#escenas)
- [Dejar una app fuera de FocusWShell](#excluir)
- [Cambiar un atajo](#atajo)
- [Elegir qué hacen Alt+Tab y la barra de tareas](#gestos)
- [Tomarte un descanso de FocusWShell](#descanso)
- [Reportar un problema](#reportar)

## Configurar un monitor normal {#monitor-normal}

En un monitor 16:9 caben el foco y un panel al lado.

1. Abre la ventana principal (doble clic en el icono de la bandeja) y ve a **Inicio**.
2. En **Disposición del principal**, deja prendida una columna, por ejemplo **Trabajo**, y apaga la otra.
3. Pon en ese panel las apps que más usas junto a tu trabajo (**Ventanas** → columna Panel).
4. Pon el chat y el correo en el otro panel. Su columna está apagada, así que esperan minimizados: tócalos en el deck, o ve a ellos con Alt+Tab, y vienen al foco.
5. Arrastra el borde entre el foco y el panel hasta que el foco tenga buen ancho. Se recuerda.

## Configurar un ultrawide {#ultrawide}

En un monitor 21:9 o más ancho, usa las dos columnas.

1. En **Inicio**, prende **Contexto** y **Trabajo**: Contexto · foco · Trabajo.
2. Arrastra los dos bordes para ajustar los anchos.
3. Si prefieres el foco en una orilla, apaga una columna. Aparece un botón extra (**⇠** o **⇢**): lleva la otra columna a ese lado, y los dos paneles quedan juntos: *Contexto · Trabajo · foco*.

## Poner paneles en otro monitor {#otro-monitor}

1. En la ventana principal, abre **Paneles**.
2. En la última fila, escribe un nombre (por ejemplo *Referencia*).
3. En la lista de sitios, elige el otro monitor, izquierda o derecha, arriba o abajo.
4. Pulsa **Crear panel**.
5. Abre **Ventanas** y elige el panel nuevo para las apps que van ahí.

Cada monitor tiene dos columnas, y cada columna se puede partir en dos (arriba y abajo): hasta cuatro paneles por monitor. En un monitor que no es el principal, un panel también puede ser de **Pantalla completa**: su app ocupa todo el monitor.

Si pones todos tus paneles en otros monitores, apaga las dos columnas en **Inicio** y el foco ocupa tu monitor principal.

## Usar una tablet como deck {#tablet}

1. Haz que la tablet sea un monitor extendido con una app de pantalla remota: ver [Usar una tablet]({{ '/es/tablet/' | relative_url }}) (probado con Apollo y Artemis).
2. Abre el deck (<kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Mayús</kbd>+<kbd>D</kbd>).
3. Toca **⇢** arriba en el deck y elige la tablet: **entera**, o **mitad** si quieres paneles en la otra mitad.

El deck se queda en la tablet. Si la tablet se desconecta, vuelve a la esquina del monitor principal, y regresa cuando la tablet vuelve.

## Crear, renombrar o mover un panel {#paneles}

Todo está en **Paneles**, en la ventana principal:

- **Crear:** escribe un nombre en la última fila, elige su sitio, **Crear panel**.
- **Renombrar:** haz clic en su nombre, escribe el nuevo y haz clic en otro lado.
- **Mover:** elige otro sitio en su lista. Si ese sitio está ocupado, la lista dice por cuál panel.
- **Borrar:** **Borrar**, y confirma. Sus ventanas quedan libres, donde están.

En el principal, *Contexto* y *Trabajo* se pueden partir arriba y abajo, por ejemplo *Chat* arriba y *Correo* abajo.

## Cambiar cómo un panel muestra sus apps {#presentacion}

Un panel puede mostrar sus apps de cuatro formas (ver [presentaciones]({{ '/es/conceptos/#presentaciones-de-un-panel' | relative_url }})):

- **En el deck:** toca ▦ (tiles), ⧉ (pestañas) o ☰ (lista) en el encabezado del panel. ⛶ (pantalla completa), sólo en los otros monitores.
- **En la ventana principal:** **Paneles**, la tercera lista de cada panel.

Tiles para paneles con dos o tres apps que quieres ver a la vez; pestañas para un panel con muchas apps que usas de una en una; lista para ver miniaturas y las apps cerradas de ese panel.

## Dejar una app en su panel aunque esté cerrada {#anclar}

Ánclala, y su lugar en el panel se queda aunque la cierres: atenuada, y un toque la abre ahí.

1. En el deck, toca **Anclar…** (índigo).
2. Toca la app.

Para desanclarla, haz lo mismo otra vez. También puedes anclar desde el menú de una app (mantén pulsado, o clic derecho).

## Guardar tu configuración como escena {#escenas}

Una escena recuerda qué apps están en cada panel y cuál está en el foco.

1. Acomoda tus apps como te gusta.
2. En la ventana principal, abre **Escenas**.
3. Escribe un nombre (por ejemplo *Código*) y pulsa **Guardar escena actual**.

Para aplicarla, elígela en la lista y pulsa **Aplicar escena**. Con **Abrir las que falten** prendido, las apps cerradas se abren en su panel. Las primeras nueve escenas también se aplican con <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Mayús</kbd>+<kbd>1</kbd>…<kbd>9</kbd>: el número junto a su nombre en la lista. Para actualizar una escena, guárdala otra vez con el mismo nombre.

## Dejar una app fuera de FocusWShell {#excluir}

Hay apps que es mejor dejar en paz: un lanzador de juegos, una videollamada, una herramienta que flota encima.

- **Desde la lista:** en **Ventanas**, abre **…** en su fila (o clic derecho) y elige excluir su app o su tipo de ventana.
- **A mano:** en **Excluidas**, elige *Ejecutable*, *AUMID* o *Clase de ventana*, escríbelo (por ejemplo `Zoom.exe`) y pulsa **Excluir**.

Las apps excluidas quedan libres al momento, y FocusWShell deja de mostrarlas. Quita la regla en **Excluidas** para que vuelvan.

## Cambiar un atajo {#atajo}

1. En la ventana principal, abre **Atajos y gestos**.
2. Pulsa **Cambiar atajos…**.
3. Haz clic en el atajo que quieres cambiar y pulsa la combinación nueva. Necesita <kbd>Ctrl</kbd>, <kbd>Alt</kbd> o <kbd>Win</kbd>.
4. Pulsa **Guardar**. **Por defecto** vuelve al original.

Funciona al momento. Mientras esa ventana está abierta, los atajos se pausan. Los de siempre están en [Atajos]({{ '/es/atajos/' | relative_url }}).

## Elegir qué hacen Alt+Tab y la barra de tareas {#gestos}

De entrada, elegir una ventana que está en un panel con **Alt+Tab**, la **Vista de tareas** o la **barra de tareas** la lleva al foco. Hacer clic en una ventana, no.

Para cambiarlo, abre **Atajos y gestos** y prende o apaga cada uno en *Gestos de Windows*.

![Atajos y gestos]({{ '/assets/img/es/main-shortcuts.png' | relative_url }})

## Tomarte un descanso de FocusWShell {#descanso}

- **Un momento:** **Liberar todas** (en **Inicio** o en *Sistema* en el deck) devuelve cada ventana a donde estaba. **Reordenar todas** las vuelve a poner en sus paneles.
- **Por esta sesión:** **Salir**, en el menú de la bandeja, devuelve todas las ventanas y cierra FocusWShell.
- **Algo va mal:** <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Mayús</kbd>+<kbd>Retroceso</kbd>, en cualquier momento.

## Reportar un problema {#reportar}

1. Anota la versión: está en la primera línea del menú de la bandeja, y en **General → Acerca de**.
2. Escribe qué hiciste, qué esperabas y qué pasó. Una foto de la pantalla ayuda.
3. Abre un [issue nuevo](https://github.com/proteo5/FocusWShell/issues/new/choose) y elige *Algo falló*.

Si FocusWShell falló, puede haber un `errors.log` en `%LOCALAPPDATA%\FocusWShell`: adjúntalo. Ver [Comentarios y privacidad]({{ '/es/comentarios/' | relative_url }}).
