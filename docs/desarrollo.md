# Desarrollo

Cómo compilar FocusWShell, instalar una copia de uso diario mientras se desarrolla y publicar una versión. La documentación de ingeniería está en el [índice de docs](README.md); el manual de uso, en el [sitio](https://proteo5.github.io/FocusWShell/) (fuente en `site/`).

## Compilar y ejecutar

Hace falta el SDK de .NET 10 en Windows.

```powershell
dotnet run --project src/FocusWShell.App
dotnet test FocusWShell.slnx
```

`TreatWarningsAsErrors` está activado: una advertencia del analizador rompe la compilación.

## Copia de uso diario

Para usar FocusWShell mientras se sigue desarrollando, se instala una copia estable fuera del repositorio:

```powershell
powershell -File build\instalar.ps1 -Abrir
```

- Se instala en `%LOCALAPPDATA%\Programs\FocusWShell`.
- Al volver a ejecutarlo, actualiza la copia: cierra la que esté abierta devolviendo las ventanas a su sitio, y la vuelve a abrir.
- La abre a través del Explorador: si el script corre desde una app empaquetada (como la terminal de Claude Desktop), FocusWShell heredaría su contenedor y su configuración quedaría en la carpeta privada del paquete.
- En la bandeja, **Iniciar con Windows** hace que se abra al iniciar sesión.
- `FocusWShell.exe --salir` cierra la instancia abierta como lo haría **Salir**.

## Textos de la interfaz

Están en `Strings.resx` (español) y `Strings.en.resx` (inglés), en `FocusWShell.App` y en `FocusWShell.Core`. Para añadir uno en los dos idiomas:

```powershell
powershell -File build\textos.ps1 -Clave Deck_Hola -Es "Hola, {0}" -En "Hello, {0}"
```

## Vista previa del deck

Dibuja el deck con datos de ejemplo en sus tres tamaños y en cada modo, sin tocar ventanas:

```powershell
FocusWShell.exe --deck-preview <carpeta> [es|en]
```

Las capturas del README y del manual (`site/assets/img/`) salen de aquí. Los datos de ejemplo son inventados y salen en el idioma pedido.

## Publicar una versión

1. Sube `<Version>` en `Directory.Build.props` y haz commit: un release se hace desde un commit, sin cambios pendientes.
2. Genera el paquete:

   ```powershell
   powershell -File build\release.ps1
   ```

   Deja en `artifacts\` el zip autocontenido para win-x64 (no necesita .NET instalado) y su SHA-256. El zip lleva la app, `install.cmd`/`install.ps1`, `uninstall.ps1` y un `README.txt` en inglés y español; las plantillas están en `build\paquete\`.
3. Escribe sus notas en `.github/release-notes/v<versión>.md` (inglés y español) y añade la versión a `CHANGELOG.md`.
4. Crea el release en GitHub con una etiqueta `v<versión>` y esas notas, adjunta el zip y su `.sha256`, y márcalo como *prerelease* mientras sea alfa o beta. Con `gh`:

   ```powershell
   gh release create v<versión> artifacts\FocusWShell-<versión>-win-x64.zip artifacts\FocusWShell-<versión>-win-x64.zip.sha256 --prerelease --title "FocusWShell <versión>" --notes-file .github\release-notes\v<versión>.md
   ```

## Diagnóstico

- `tools/FocusWShell.Probe` (`focus-probe`) es la consola de diagnóstico de los spikes.
- Los errores no controlados quedan en `%LOCALAPPDATA%\FocusWShell\errors.log`.

## Principios

- Convive con Explorer y la barra de tareas; no reemplaza el shell.
- Las apps siguen siendo ventanas normales de Windows; el gestor no las incrusta.
- Siempre hay salida: un atajo global devuelve todas las ventanas a como estaban, incluso después de una caída.
- No pide permisos de administrador, no se conecta a la red y no envía telemetría.
