---
layout: default
lang: es
ref: limitations
title: Limitaciones conocidas
permalink: /es/limitaciones/
---

# Limitaciones conocidas

FocusWShell es una alfa. Esto es lo que sabemos.

## Escritorios virtuales

FocusWShell conoce sólo en parte los escritorios virtuales de Windows:

- **Nunca abre una segunda copia** de una app que está en otro escritorio: te lleva a ella.
- En el deck, las apps de otro escritorio salen atenuadas con **⧉**.
- Pero todavía hay **un solo foco para todos los escritorios**, y las tiras de los paneles pueden mostrar pestañas de otro escritorio.

El soporte completo (un foco y unas apps por escritorio, y pestañas de escritorios en el deck) está planeado. Mientras tanto, funciona mejor en un solo escritorio.

## Apps que no se pueden organizar

- **Las apps que se ejecutan como administrador** no las puede mover FocusWShell, que corre sin elevación. Aparecen con el motivo y se quedan donde están.
- **Juegos y presentaciones en pantalla completa:** cuando una app se pone en pantalla completa por sí misma (F11, un juego), FocusWShell deja ese monitor en paz hasta que sale. Se ha probado con navegadores; con juegos y presentaciones, todavía poco.
- Algunas apps tienen **tamaño fijo** o un tamaño mínimo grande. FocusWShell lo aprende y les hace sitio, o las deja fuera del layout con una nota.

## Páginas de los navegadores

- Sólo **Chrome y Edge**. Firefox y los demás no.
- El sitio de cada página se deduce de su título, así que algunas pueden salir como sueltas.
- Probado con pestañas verticales. Las pestañas horizontales y los grupos plegados, todavía poco.

## Otras

- Probado en **Windows 11**. Windows 10 debería funcionar, pero no se ha probado.
- Probado sobre todo en una configuración: un ultrawide como principal, un segundo monitor y una tablet como deck. Otras configuraciones son justo para lo que es esta alfa.
- Los archivos **no están firmados**, así que Windows puede avisarte al instalar.

¿Encontraste algo más? [Cuéntanos]({{ '/es/comentarios/' | relative_url }}).
