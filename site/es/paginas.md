---
layout: default
lang: es
ref: pages
title: Páginas de los navegadores
permalink: /es/paginas/
---

# Páginas de los navegadores

Cuando buscas una página, no deberías tener que saber en qué ventana ni en qué navegador está. **🗂 Páginas**, a la derecha de las pestañas de monitores en el deck, reúne todas las pestañas de todas las ventanas de Chrome y Edge.

Sólo aparece mientras hay Chrome o Edge abiertos. El número es cuántas páginas había la última vez que se leyeron; se leen al abrir la lista y después de cerrar una.

## Dos pasos: sitios, y luego páginas

![Páginas agrupadas por sitio]({{ '/assets/img/es/deck-pages.png' | relative_url }})

1. **Sitios y páginas sueltas.**
   - **Sitios:** los que tienen varias páginas, de más a menos («Jira · 9 ›»). Toca uno para ver sus páginas.
   - **Páginas sueltas:** las demás, por título. Toca una para abrirla directo; su **✕** la cierra.
2. **Las páginas de un sitio.**
   - Cada página sin el nombre del sitio, y la ventana en la que está («Google Chrome · en el foco»).
   - La página visible en su ventana lleva una barra azul.
   - **✕** cierra una página. **Cerrar todas** cierra todas las del sitio, tras un segundo toque.

![Las páginas de un sitio]({{ '/assets/img/es/deck-pages-site.png' | relative_url }})

Abrir una página hace lo mismo que tocar su app: si su ventana está en un panel, va al foco; si es libre, se trae al frente. Después se muestra esa pestaña.

Cerrar una página no cambia la pestaña visible de su ventana ni mueve el ratón. ¿Cerraste una por error? <kbd>Ctrl</kbd>+<kbd>Mayús</kbd>+<kbd>T</kbd> en ese navegador la recupera.

Arrastra con el dedo para desplazarte. No hay buscador a propósito: escribir obligaría al deck a tomar el teclado.

## Cómo se reconocen los sitios

Los navegadores no le dicen a otros programas la dirección de cada pestaña, sólo su título. FocusWShell deduce el sitio de los títulos: un trozo del título cuenta como sitio cuando se repite en varias páginas, casi siempre al final («… - Jira», «… | Cloudflare»). También reconoce algunos patrones comunes (repositorios de GitHub, Gmail).

Cuando no lo sabe, la página sale como suelta, con su propio título. Nunca mezcla dos sitios a propósito.

## Privacidad

Los títulos se leen sólo mientras la lista está a la vista, y nunca se guardan ni se envían a ningún lado.
