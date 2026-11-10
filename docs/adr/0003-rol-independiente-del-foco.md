# ADR 0003 — El rol de una ventana es independiente del foco de Windows

- **Estado:** Aceptada; enmendada el 2026-11-10 (gestos de cambio de ventana)
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

## Enmienda (2026-11-10): los gestos de cambio de ventana cuentan como comando

Alt+Tab, la vista de tareas y la barra de tareas son gestos deliberados: el usuario elige una ventana concreta para trabajar en ella. Tratarlos como comando explícito no rompe la intención de este ADR, que es que nada se mueva **solo**.

- Si el usuario elige una ventana de un panel con **Alt+Tab**, la **vista de tareas** (Win+Tab) o su botón en la **barra de tareas**, la ventana pasa al foco (`Promote`). Cada gesto tiene su casilla en la ventana principal; los tres vienen activados.
- El **clic en la propia ventana** también se puede activar, pero viene **desactivado**: con él, cada clic en un panel movería ventanas bajo el cursor. Desactivado, el teclado se queda en la ventana del panel y nada se mueve.
- Siguen sin mover nada: una app que se activa sola, una notificación, un diálogo, el propio gestor y volver con Esc a la ventana que ya estaba (CA7).
- Una ventana libre (sin panel) o fuera de layout nunca se mueve por un gesto.

Cómo se reconoce cada gesto en Windows 11 está en [04 — Riesgos y spikes](../04-riesgos-y-spikes.md#gestos-de-cambio-de-ventana-m1b-2026-11-10).
