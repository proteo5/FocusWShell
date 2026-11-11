# FocusWShell

Gestor de atención visual para Windows. Organiza las ventanas según el papel que cumplen en tu atención, no sólo como rectángulos en una cuadrícula.

Pensado para trabajar con un monitor ultrawide y un monitor estándar con muchas ventanas abiertas:

```text
┌──────────────┬──────────────────────────────┬────────────────┐   ┌──────────────┐
│ CONTEXTO     │ CENTRO                       │ TRABAJO        │   │ CONTEXTO     │
│ chat, avisos │ la tarea principal           │ tiles / pila   │   │ correo, cal. │
└──────────────┴──────────────────────────────┴────────────────┘   └──────────────┘
                          ultrawide                                  monitor estándar
```

- **Centro:** la tarea de concentración, en una región amplia y protegida.
- **Trabajo:** herramientas de apoyo, en tiles o en una pila con pestañas.
- **Contexto:** correo, chat, calendario y estados, a la vista pero sin dominar.
- **Estacionada:** abierta pero apartada; se recupera desde su panel.

Una ventana sólo cambia de lugar cuando tú lo decides. Hacer clic en el chat o recibir una notificación no la lleva al centro.

## Estado

Hitos M0, M1, M1b y M2 hechos en código: paneles propios en varios monitores, vistas, escenas, tiles y lista, deck (también en una tablet), y robustez (pantalla completa de las apps, suspensión, monitores que van y vienen, ventanas elevadas, lista de exclusión). Ahora toca usarlo a diario y validar ([06-validacion](docs/06-validacion.md)). El estado de cada hito está en la [hoja de ruta](docs/05-hoja-de-ruta.md).

**Uso diario:** instala una copia estable, fuera del repositorio:

```powershell
powershell -File build\instalar.ps1 -Abrir
```

- Se instala en `%LOCALAPPDATA%\Programs\FocusWShell`.
- Al volver a ejecutarlo, actualiza la copia: cierra la que esté abierta devolviendo las ventanas a su sitio, y la vuelve a abrir.
- En la bandeja, **Iniciar con Windows** hace que se abra al iniciar sesión.
- `FocusWShell.exe --salir` cierra la instancia abierta como lo haría **Salir**.
- En español o en inglés: «Idioma» en la ventana principal (automático: el de Windows).

Para desarrollar:

```bash
dotnet run --project src/FocusWShell.App
```

Los textos de la interfaz están en `Strings.resx` (español) y `Strings.en.resx` (inglés). Para añadir uno en los dos idiomas:

```powershell
powershell -File build	extos.ps1 -Clave Deck_Hola -Es "Hola, {0}" -En "Hello, {0}"
```

Lo que sigue describe lo básico; la ventana principal y el deck tienen más.

- La ventana lista las apps que se pueden gestionar. Elige un rol y un monitor para cada una y pulsa **Aplicar roles**.
  - El Centro está siempre en el monitor principal.
  - Los demás monitores alojan paneles auxiliares.
  - Con el template **Fill**, la primera ventana de un panel auxiliar ocupa ese monitor, maximizada.
- Encima de cada panel aparece una **tira de pestañas**, una por ventana del panel:
  - **Clic:** la muestra, la recupera si está estacionada (`⏸`) o la devuelve si está en el Centro (`↑`).
  - **Doble clic:** la lleva al Centro.
  - **Clic derecho:** el resto de acciones.
- **Escenas:**
  - **Guardar:** escribe un nombre y pulsa "Guardar escena actual". Se guarda qué ventanas están gestionadas, en qué panel y monitor, y los templates.
  - **Aplicar:** desde la ventana, desde el submenú de la bandeja o con `Ctrl+Alt+Mayús+1…9`. Las ventanas que no son de la escena vuelven a su sitio original.
- Cerrarla no detiene el gestor: sigue en el área de notificación. Desde su menú, **Salir** devuelve todas las ventanas a su sitio.
- Atajos globales con `Ctrl+Alt+Mayús`:

  | Tecla | Acción |
  | --- | --- |
  | ↑ | Lleva al Centro la ventana activa, si está gestionada. La que estaba vuelve a su panel. |
  | ← | Vuelve a poner en el Centro la ventana anterior. Repetirlo alterna entre las dos. |
  | ↓ | Devuelve la ventana del Centro a su panel. |
  | P | Mantener la pantalla encendida (activa o desactiva; se recuerda). |
  | Retroceso | **Emergencia:** restaura todas las ventanas en cualquier momento. |
- Si el gestor muere sin cerrarse, al volver a abrirlo ofrece restaurar las ventanas que dejó movidas.

`tools/FocusWShell.Probe` (`focus-probe`) es la consola de diagnóstico de los spikes.

La documentación de ingeniería está en [docs/](docs/README.md):

- [Visión y alcance](docs/01-vision.md)
- [Modelo de dominio](docs/02-modelo-de-dominio.md)
- [Arquitectura](docs/03-arquitectura.md)
- [Riesgos y spikes](docs/04-riesgos-y-spikes.md)
- [Hoja de ruta](docs/05-hoja-de-ruta.md)
- [Decisiones (ADR)](docs/adr/)

## Principios

- Convive con Explorer y la barra de tareas; no reemplaza el shell.
- Las apps siguen siendo ventanas normales de Windows; el gestor no las incrusta.
- Siempre hay salida: un atajo global devuelve todas las ventanas a como estaban, incluso después de una caída.
- No pide permisos de administrador, no envía telemetría.

## Licencia

[Apache 2.0](LICENSE)
