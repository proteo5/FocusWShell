# ADR 0008 — El Centro sólo existe en el monitor principal; los demás son auxiliares

- **Estado:** Aceptada
- **Fecha:** 2026-11-09

## Contexto

El modelo original repartía la escena entre el ultrawide y el monitor estándar con "un `SceneLayout` por huella". No decía qué monitor aloja el Centro: en M0 se tomó el más ancho. El usuario lo ha precisado:

- Sólo hay **un lugar de foco**: el Centro del monitor principal.
- Los demás monitores sirven para **paneles auxiliares**: monitoreo, depuración, chat o documentación. Ahí es normal querer una ventana maximizada ocupando todo el monitor, sin que eso la convierta en "lo importante".
- Esos monitores pueden ser tablets u otros equipos conectados por wifi que extienden el escritorio ([ADR 0009](0009-tablets-como-monitores-auxiliares.md)). Aparecen y desaparecen más a menudo que un monitor por cable.

## Decisión

1. **Dos tipos de monitor:**
   - **Principal:** por defecto, el monitor principal de Windows; más adelante será configurable. Aloja el Centro y los paneles laterales Contexto | Centro | Trabajo, como en M0.
   - **Auxiliar:** cualquier otro monitor. Es un contenedor de paneles. **Nunca tiene Centro.**
2. **Paneles en un monitor auxiliar:**
   - Cada uno tiene un hogar (`Work` o `Context`), como los paneles laterales del principal.
   - Admite los templates de siempre (`Tiles`, `Stack`) más uno nuevo, **`Fill`**: una sola ventana ocupa el monitor entero, maximizada.
   - Maximizar una ventana en un auxiliar es un estado válido del panel (`Fill`), no "fuera de layout".
3. **`Promote` siempre apunta al Centro del principal.** Una ventana de un panel auxiliar promovida viaja al Centro y, al volver, regresa a su panel auxiliar. Los atajos de foco (↑ ← ↓) no cambian de significado.
4. **Si se desconecta un auxiliar**, sus ventanas vuelven a los paneles de su mismo hogar en el principal, en pila si no caben. **Nunca caen sobre el Centro.** Windows, por su cuenta, las movería al monitor principal tal cual, encima de lo que haya: el gestor tiene que reaccionar antes de que el usuario lo vea (S6).
5. **Al reconectarse**, se reconoce el monitor por su identidad estable (`monitorDevicePath`, no `\\.\DISPLAYn`) y se reaplica su layout.

## Consecuencias

- La escena ya no es "un layout de tres columnas por huella", sino un layout del principal y un conjunto de paneles por monitor auxiliar conocido. `SceneLayout` se describe así en el modelo de dominio.
- CA1 se reinterpreta: el monitor estándar aloja paneles auxiliares, no parte del Centro.
- El motor de layout actual (`SceneLayoutEngine`) sigue sirviendo para el principal. Los auxiliares necesitan un motor más simple: reparto de paneles en un monitor, sin Centro que proteger.
- Una ventana en `Fill` en un auxiliar no compite con el Centro: el gestor nunca la promueve por sí sola, aunque reciba foco (invariante 4 y [ADR 0003](0003-rol-independiente-del-foco.md)).
- En M0, la app y el probe pasan a colocar la escena en el monitor principal de Windows en vez de en el más ancho. En esta configuración, el ultrawide es el principal y no cambia nada.
