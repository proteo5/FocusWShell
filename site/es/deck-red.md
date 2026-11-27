---
layout: default
lang: es
ref: deck-network
title: Deck en la red
permalink: /es/deck-red/
---

# Deck en la red

Usa el deck desde una tablet o un teléfono en tu red local, sin Apollo. FocusWShell sirve el deck como página web: la abres en el navegador de la tablet y tocas, y FocusWShell hace lo mismo que con el deck del PC. Funciona igual en Android y en iPad, sin instalar nada en la tablet.

Está **apagado** hasta que lo prendas, y sólo responde a los dispositivos que emparejes.

## Prenderlo y emparejar una tablet

1. En la ventana principal, abre **Deck en la red** y prende **Servir el deck en la red local**.
2. La primera vez, Windows puede pedir permiso en el Firewall. Acepta sólo **redes privadas**.
3. Toca **Emparejar**. Aparecen un código QR y un código de 6 cifras; sirven una vez y caducan en 5 minutos.
4. En la tablet (en el mismo wifi), escanea el QR con la cámara, o abre la dirección que aparece y escribe el código. Ponle un nombre al dispositivo y toca **Emparejar**.
5. Se abre el deck. Agrégalo a la pantalla de inicio desde el menú del navegador, para que la próxima vez entre directo.

La tablet queda emparejada. Para quitarla, toca **Quitar** junto a ella en **Deck en la red**: se desconecta al momento.

## Qué hace

Lo mismo que el deck del PC, con todos los monitores a la vez:

- **A la izquierda:** la app del foco, **Anterior**, **Al panel**, **Liberar todas** y **Mover…**.
- **A la derecha:** cada monitor con sus paneles y, abajo, **Libres** y **🗂 Páginas**.
- **Toca una app** para llevarla al foco. En un panel en pestañas, tocar una pestaña oculta la muestra en su panel; una app anclada y cerrada se abre en su panel; una libre se trae al frente.
- **Mover…:** toca la app y luego el panel donde va, el foco, o **Libres** para liberarla.
- **⛶** entra y sale de pantalla completa.

En horizontal usa dos columnas, como el deck del PC; en un teléfono o una tablet en vertical, una.

Todavía no tiene: los modos Anclar, Minimizar y Cerrar, la disposición del principal ni los botones de energía. Para eso, usa el deck del PC.

## Páginas de los navegadores

**🗂 Páginas** sólo aparece si prendes **Mostrar las páginas de los navegadores** en **Deck en la red**. Viene apagado porque los títulos de las páginas viajan por tu red sin cifrar. Las ventanas privadas nunca muestran sus títulos.

## Mantener la pantalla encendida

Por ahora la página no puede mantener encendida la pantalla de la tablet (los navegadores sólo lo permiten con una conexión cifrada). Sube el tiempo de pantalla en los ajustes de la tablet.

## Privacidad y seguridad

- FocusWShell no se conecta a internet. Con el deck en la red prendido, escucha en tu red local, en el puerto 8765, y sólo para los dispositivos que emparejaste.
- Sólo responde a direcciones de la red local (como 192.168.…), nunca de internet. En un wifi público (un café, un aeropuerto) cualquiera en esa red podría ver lo que viaja: apágalo ahí.
- Cada dispositivo tiene su propia llave, que sólo guarda el dispositivo. Quitarlo lo corta al momento.
- La conexión no va cifrada (HTTP). Úsalo en una red de confianza, como el wifi de tu casa.
