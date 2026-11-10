# ADR 0003 — El rol de una ventana es independiente del foco de Windows

- **Estado:** Aceptada
- **Fecha:** 2026-11-09

## Contexto

Muchos gestores de tiling reorganizan las ventanas cuando cambia el foco. Para este proyecto eso es justo lo que se quiere evitar: responder un chat o recibir una notificación no debe mover nada al Centro. Los saltos visuales rompen la continuidad de atención, y el usuario pierde la referencia de dónde está cada cosa.

## Decisión

- El hogar (`Home`) y la ubicación (`Placement`) de una ventana sólo cambian por un **comando explícito** del usuario: atajo, menú o clic en la UI del gestor.
- Los eventos de foco y de notificación sólo pueden actualizar indicadores visuales.
- Una ventana que se cierra deja el Centro vacío; no se promueve otra de forma automática.
- Cualquier automatización futura (reglas por app) será opt-in, tendrá modo de prueba, explicará cada acción y se podrá deshacer.

## Consecuencias

- El comportamiento es predecible y fácil de probar: es un invariante del núcleo (ver [02 — Modelo de dominio](../02-modelo-de-dominio.md#invariantes)).
- El usuario necesita atajos rápidos para promover y volver; sin ellos, el modelo resulta lento. Por eso CA2 exige menos de dos acciones.
