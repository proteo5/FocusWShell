---
layout: default
lang: es
ref: tablet
title: Usar una tablet
permalink: /es/tablet/
---

# Usar una tablet como deck

El deck funciona en cualquier pantalla: una esquina del monitor principal, una pantalla táctil, un segundo monitor. Pero donde mejor queda es en una tablet junto al teclado, siempre a la vista y sin tapar tu trabajo.

Para eso, Windows tiene que ver la tablet como **un monitor más**. FocusWShell no transmite nada por sí mismo; lo hace una app de escritorio remoto.

## Probado: Apollo + Artemis (Android)

[Apollo](https://github.com/ClassicOldSong/Apollo) es un servidor de streaming para Windows. Cuando se conecta un dispositivo, crea un **monitor virtual** con la resolución de ese dispositivo, y Windows lo trata como un monitor extendido real. **Artemis** es su cliente para Android.

1. Instala **Apollo** en tu PC y pon un PIN o empareja como pida su configuración.
2. Instala **Artemis** en la tablet Android y conéctala a tu PC.
3. Inicia la transmisión del escritorio. Windows muestra ahora un monitor más, el de la tablet.
4. En el deck, toca **⇢** y elige la tablet: **una mitad** (sus paneles usan la otra) o **entera**.

Los toques en la tablet llegan como clics del ratón, y el deck está hecho para eso: botones de al menos 48 puntos y arrastrar para desplazar. No le quita el teclado a la app del foco.

## Deberían funcionar, sin probar todavía

- **Moonlight** en un iPad u otro dispositivo, conectado a Apollo.
- **Sunshine** en vez de Apollo, con un controlador de pantalla virtual para que la tablet extienda el escritorio en vez de duplicarlo.

Si pruebas alguna, cuéntanos cómo te fue en [Issues](https://github.com/proteo5/FocusWShell/issues).

## Cuando la tablet se desconecta

Si la tablet se va (el wifi, la suspensión), su monitor desaparece. FocusWShell guarda a salvo las apps de sus paneles y el deck vuelve a la esquina del monitor principal. Cuando la tablet vuelve, sus paneles y el deck vuelven también.
