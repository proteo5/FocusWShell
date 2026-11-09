# 06 — Validación de uso

El proyecto sólo se justifica si el modelo de roles y escenas mejora de forma perceptible el trabajo diario frente a **FancyZones + DisplayFusion**. Esta guía define cómo comprobarlo sin instrumentación remota.

## Protocolo

1. **Línea base (1 semana, antes de M0 o en paralelo):** trabajar con FancyZones (y DisplayFusion si está disponible) en la configuración ultrawide + estándar. Anotar fricciones con la plantilla de abajo.
2. **Uso del prototipo (desde M0):** trabajar con FocusWShell en tareas de código, investigación, documentos y comunicación. Mismo registro.
3. **Una variable a la vez:** si se cambia el ancho del Centro, el lado del panel de Trabajo o la ubicación del chat, se anota y no se cambia nada más ese día.
4. **Revisión semanal:** releer el diario, contar fricciones por tipo y decidir un ajuste.

## Qué registrar

- Qué ventanas se promueven al Centro y con qué frecuencia.
- Qué panel y qué template se usan de verdad.
- Cuándo el contexto distrae y cuándo falta.
- Qué falla al reconectar monitores o al volver de suspensión.
- Cuántas veces se usa el atajo de emergencia, y por qué.
- Momentos de fricción: buscar una ventana, perder contexto, leer algo demasiado lejos, girar el cuello, notificación disruptiva.

## Plantilla diaria

Archivo sugerido: `validacion/AAAA-MM-DD.md` (fuera del repositorio o en una rama propia si contiene datos personales).

```markdown
# AAAA-MM-DD

Herramienta: FocusWShell vX / FancyZones / otra
Monitores: ultrawide + estándar | sólo ultrawide | …
Actividades: código, investigación, …

## Fricciones
| Hora | Tipo | Qué pasó | Qué esperaba |
| --- | --- | --- | --- |
|  | buscar ventana / distracción / fallo / ergonomía |  |  |

## Promociones al Centro
- App → motivo

## Ajuste del día (una variable)
-

## Impresión general (1–5)
- Continuidad de atención:
- Esfuerzo para encontrar ventanas:
- Confianza en el gestor:
```

## Decisión de continuidad

Después de M2 y al menos tres semanas de uso:

| Resultado | Decisión |
| --- | --- |
| Menos fricciones que en la línea base y la impresión de continuidad mejora claramente | Continuar a M3. |
| Mejora marginal | Revisar qué parte del modelo aporta (¿pila?, ¿escenas?, ¿restauración?) y recortar el alcance a eso. |
| Sin mejora o peor | Detener el proyecto o reorientarlo; documentar por qué. |
