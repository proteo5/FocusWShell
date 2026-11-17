# ADR 0019 — Actividad: tiempo en el foco y en uso

- **Estado:** Aceptada (fase 1 en 0.4.0-alpha)
- **Fecha:** 2026-11-17

## Contexto

Con FocusWShell aparece una forma de trabajar que antes no existía. Una app queda en el foco, a la vista en el centro, mientras se escribe en otra que está en su panel: por ejemplo, el foco en un documento y el teclado en el chat. Es cómodo, y medirlo sirve para dos cosas:

- **Para quien lo usa:** entender en qué se le va el día y con qué apps trabaja a la vez.
- **Para el proyecto:** es la evidencia que pide la [validación de uso](../06-validacion.md): qué se promueve al foco y con qué frecuencia, y cuándo falta el contexto. Hoy eso se anota a mano.

FocusWShell ya sabe las dos cosas en cada momento. Sabe qué app está en el foco, porque es su modelo, y qué app tiene el teclado, porque es la que marca con borde blanco en el deck. Solo falta llevar la cuenta.

En inglés de Windows, *focus* significa «la ventana con teclado», y en FocusWShell «foco» es el lugar central. Hace falta otro nombre para la app con teclado.

## Decisión

### Términos

| Español | English | Qué es |
| --- | --- | --- |
| **En el foco** | **In focus** | La app en el lugar central del principal. |
| **En uso** | **In use** | La app que recibe el teclado y el ratón (la ventana en primer plano para Windows). |
| **En paralelo** | **Side by side** | Tiempo en que la app en uso no es la del foco. |

«En uso» reemplaza a «al frente (recibe el teclado)» en el deck y en el manual.

### En el deck

El tile del foco muestra también la app en uso cuando no es la misma: un segundo icono, más pequeño, con su nombre y «en uso». Así se ve de un vistazo que se trabaja en paralelo. Cuando son la misma, el tile queda como hoy.

La app en uso conserva el borde blanco en su panel, pero su tooltip dice «en uso».

### Medición

- **Tramos, no totales.** Se guarda cada tramo con su inicio, su fin, la app en el foco y la app en uso. Por ejemplo: «10:02–10:17, foco Docs, uso Chat». Con tramos se pueden calcular después los totales, los pares, la línea del día y las tendencias; con totales, no.
- **Solo tiempo presente.** El tramo se corta tras unos minutos sin entrada (`GetLastInputInfo`; 3 min por defecto) y al bloquear, suspender o apagar.
- **Por app, no por ventana.** Se guarda la clave de la app (la misma de paneles y escenas), su nombre y, aparte, el panel y el monitor. Nunca el título de la ventana. Las apps web (`._crx_`) cuentan como apps propias.
- **La lógica en Core.** Un `ActivityTracker` sin Win32, que recibe «cambió el foco», «cambió la app en uso», «inactivo» y «activo» con un reloj inyectable, y emite tramos. Se prueba con xUnit. La capa App solo le pasa los eventos y escribe los tramos.
- **Almacenamiento:** un archivo JSON Lines por día en `%LOCALAPPDATA%\FocusWShell\actividad\AAAA-MM-DD.jsonl`, unas cientos de líneas al día. El tramo abierto se escribe al cerrarse, y también al congelar la sesión al apagar.

### Privacidad

- **Un interruptor** en la sección Actividad, apagado por defecto. Al prenderlo se explica qué se guarda y qué no.
- **Solo en este equipo.** Nunca se envía nada, y el diagnóstico y los reportes no lo incluyen.
- **90 días** por defecto, configurable. Hay un botón para borrar todo el historial.

### Dónde se ve

Hay una sección nueva, **Actividad**, en la ventana principal. El deck no lleva estadística: es para actuar, no para consultar. Las features se dan por fases:

1. **MVP:** la medición y el día: una barra por app con su tiempo en el foco y en uso, y el total en paralelo.
2. **Pares** (en el foco ← en uso) y **línea del día**, con la racha más larga y los cambios de foco por hora.
3. **Por panel y monitor**, la **semana**, el uso del **deck frente a atajos y ratón**, y **exportar** a CSV o JSON (opcionalmente, en el formato del diario de validación).

Las gráficas se dibujan con formas de WPF, sin librería.

### Lo que no hace

- Metas, límites ni avisos («llevas 2 h en X»): eso es otro producto y convertiría la actividad en vigilancia.
- Contar por página dentro de los navegadores. Si se agrega, será una opción aparte y apagada.

## Consecuencias

- Es la primera vez que FocusWShell guarda un historial de lo que se hace. Por eso es opcional, local y se puede borrar.
- La app en uso puede ser una ventana que FocusWShell no organiza (libre, excluida o del sistema). Cuenta con su nombre, sin panel.
- Con apps elevadas, el nombre puede no leerse; cuentan como «Elevada».
- El cambio de app en uso se detecta con `EVENT_SYSTEM_FOREGROUND` (`SetWinEventHook`), sin sondeo.
