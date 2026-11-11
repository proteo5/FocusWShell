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

## Pruebas pendientes con el usuario (M2)

Lo que no se pudo probar sin estar delante, o sin hardware que desconectar. Se marca al hacerlo, con la fecha y qué pasó; si algo falla, va al diario como fricción de tipo «fallo».

- [ ] **Suspensión real** con apps en paneles y el deck abierto: al volver, todo en su sitio en unos 4 s y el aviso «Volvió de la suspensión».
- [ ] **Suspensión con la tablet:** la tablet (Apollo/Artemis) tarda en volver; sus paneles pasan al principal y regresan cuando vuelve.
- [ ] **Desconectar el monitor principal:** el otro hace de principal, con el Centro y todas las ventanas. Al reconectarlo, cada uno recupera lo suyo. Anotar si Windows lo vuelve a nombrar principal.
- [ ] **Desconectar la tablet con el deck fijado en ella:** el deck vuelve a la esquina del principal; al reconectarla, vuelve a la tablet.
- [ ] **«Al foco» con el teclado** sobre una ventana elevada (p. ej. el Administrador de tareas): avisa de que corre como administrador.
- [ ] **Un juego en pantalla completa** y **una presentación de PowerPoint:** ese monitor queda en pausa (sin tiras ni deck) y todo vuelve al salir.
- [ ] **Arrastrar el borde de una ventana con el ratón** y **clic en la barra de tareas:** se comportan como en las pruebas automáticas.
- [ ] **Una semana sin el atajo de emergencia por un fallo** (criterio de salida de M2). Cada uso, con su motivo, al diario.

## Decisión de continuidad

Después de M2 y al menos tres semanas de uso:

| Resultado | Decisión |
| --- | --- |
| Menos fricciones que en la línea base y la impresión de continuidad mejora claramente | Continuar a M3. |
| Mejora marginal | Revisar qué parte del modelo aporta (¿pila?, ¿escenas?, ¿restauración?) y recortar el alcance a eso. |
| Sin mejora o peor | Detener el proyecto o reorientarlo; documentar por qué. |
