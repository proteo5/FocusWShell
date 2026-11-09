# ADR 0001 — Orquestar ventanas top-level; no usar `SetParent` como mecanismo base

- **Estado:** Aceptada
- **Fecha:** 2026-11-09

## Contexto

Para mostrar apps ajenas como tiles, pestañas o paneles hay dos caminos:

1. **Reparenting:** tomar el `HWND` de otra app, llamar a `SetParent`, cambiar su estilo a `WS_CHILD` y dibujarla dentro de una ventana del gestor.
2. **Orquestación:** dejar cada app como ventana top-level y controlar desde fuera su posición, tamaño, visibilidad y orden.

El reparenting entre procesos es frágil: `SetParent` no ajusta estilos solo, mezclar procesos con distinta conciencia de DPI puede fallar o forzar reinicios de DPI, y diálogos, menús, Electron, Office, navegadores y apps UWP se comportan de forma distinta cada uno. Si el gestor cae, las ventanas pueden quedar sin marco o atrapadas en una jerarquía rota. UIPI además impide controlar ventanas elevadas.

## Decisión

FocusWShell orquesta ventanas top-level con `EnumWindows`, `SetWinEventHook` y `DeferWindowPos`. Las pestañas, paneles y microbarras son ventanas del propio gestor, colocadas junto a las ventanas gestionadas.

El reparenting y los modos sin bordes sólo podrán existir como adaptadores opcionales, por aplicación concreta, con restauración garantizada, después del MVP.

## Consecuencias

- Funciona con cualquier app que Windows permita mover, sin código específico.
- El aspecto de "pestañas" es una simulación: las ventanas no visibles de la pila se minimizan. No es tan pulido como una integración real.
- Una caída del gestor deja las ventanas en posiciones válidas.
- El gestor no puede impedir que el usuario o la app muevan una ventana; tiene que detectarlo y adaptarse.
