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
3. Sólo escucha en redes que Windows marca como **privadas** (o de dominio). Si el wifi de tu casa está marcado como público, la sección lo dice y tiene un botón a los ajustes de red de Windows: abre tu red y elige **Red privada**. El deck empieza a escuchar solo.
4. Toca **Emparejar**. Aparecen un código QR y un código de 6 cifras; sirven una vez y caducan en 5 minutos.
5. En la tablet (en el mismo wifi), escanea el QR con la cámara, o abre la dirección que aparece y escribe el código. Ponle un nombre al dispositivo y toca **Emparejar**.
6. Se abre el deck. Agrégalo a la pantalla de inicio desde el menú del navegador, para que la próxima vez entre directo.

La tablet queda emparejada. Para quitarla, toca **Quitar** junto a ella en **Deck en la red**: se desconecta al momento.

## Qué hace

Lo mismo que el deck del PC, con todos los monitores a la vez:

- **A la izquierda:** la app del foco (y, si escribes en otra, esa app a su lado, con borde blanco), **Anterior**, **Al panel**, **Liberar todas** (pide un segundo toque), y los modos **Mover…**, **Anclar…**, **Minimizar…** y **Cerrar…**. Debajo, la **disposición del principal** (toca una columna para prenderla o apagarla; déjala apretada para restablecer el ancho) y **Sistema**: mantener encendida la pantalla del PC, el ratón a la app del foco y, tras un Liberar todas, **Reordenar todas**.
- **A la derecha:** cada monitor con sus paneles y, abajo, **Libres** y **🗂 Páginas**. La cabecera de cada panel tiene su presentación (▦ ⧉ ☰, y ⛶ fuera del principal) y, en pestañas, ◀ ▶.
- **Toca una app** para llevarla al foco. En un panel en pestañas, tocar una pestaña oculta la muestra en su panel; una app anclada y cerrada se abre en su panel; una libre se trae al frente.
- **Mover…:** toca la app y luego el panel donde va, el foco, o **Libres** para liberarla.
- **Anclar…:** toca apps para anclarlas o desanclarlas en su panel; toca **Anclar…** otra vez para terminar.
- **Minimizar…** y **Cerrar…:** toca una app; el modo se apaga solo. Una minimizada se queda en su panel, y minimizarla otra vez la devuelve. Cerrar funciona como su ✕: si tiene algo sin guardar, pregunta en el PC.
- **⛶** entra y sale de pantalla completa.

En horizontal usa dos columnas, como el deck del PC; en un teléfono o una tablet en vertical, una.

No están en la red: los botones de energía (bloquear, suspender, reiniciar, apagar) ni el menú de cada app con pulsación larga. Para eso, usa el deck del PC.

## Páginas de los navegadores

**🗂 Páginas** sólo aparece si prendes **Mostrar las páginas de los navegadores** en **Deck en la red**. Viene apagado porque los títulos de las páginas viajan por tu red sin cifrar. Las ventanas privadas nunca muestran sus títulos.

## Mantener la pantalla encendida

Por ahora la página no puede mantener encendida la pantalla de la tablet (los navegadores sólo lo permiten con una conexión cifrada). Sube el tiempo de pantalla en los ajustes de la tablet.

## Si la tablet no conecta

- **¿La red es privada?** La sección **Deck en la red** lo dice si Windows la marca como pública.
- **El Firewall de Windows** tiene que permitir FocusWShell en redes **privadas**. Si lo prendiste por primera vez con la red marcada como pública, Windows lo permitió sólo para redes públicas, y no vuelve a preguntar. Arréglalo en **Panel de control → Firewall de Windows Defender → Permitir una aplicación o una característica…**: toca **Cambiar la configuración**, busca **focuswshell.exe** y marca **Privada** (desmarca **Pública**). O, en PowerShell como administrador:

  ```powershell
  Get-NetFirewallRule -DisplayName 'focuswshell.exe' | Set-NetFirewallRule -Profile Private
  ```
- **El mismo wifi:** la tablet tiene que estar en la misma red que el PC.

## Privacidad y seguridad

- FocusWShell no se conecta a internet. Con el deck en la red prendido, escucha en tu red local, en el puerto 8765, y sólo para los dispositivos que emparejaste.
- Sólo escucha en redes que Windows marca como privadas o de dominio, nunca en una pública (un café, un aeropuerto), y sólo responde a direcciones de la red local.
- Tras 5 códigos de emparejamiento equivocados, el código se anula, y esa dirección tiene que esperar 10 minutos.
- Sólo acepta peticiones de su propia página: otra web abierta en la tablet no puede hablarle a tu PC.
- Cada dispositivo tiene su propia llave, que sólo guarda el dispositivo. Quitarlo lo corta al momento.
- La conexión no va cifrada (HTTP). Úsalo en una red de confianza, como el wifi de tu casa.
