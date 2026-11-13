---
layout: default
lang: es
ref: install
title: Instalar
permalink: /es/instalar/
---

# Instalar

## Requisitos

- **Windows 10 (versión 2004) o Windows 11, de 64 bits.** Se ha probado en Windows 11.
- Sin permisos de administrador. Todo se instala sólo para tu usuario.
- Nada más: .NET va incluido en la descarga.
- Opcional: una tablet para usarla como deck (ver [Usar una tablet]({{ '/es/tablet/' | relative_url }})).

## Instalar

1. Descarga `FocusWShell-<versión>-win-x64.zip` desde [Releases](https://github.com/proteo5/FocusWShell/releases).
2. Descomprime **todo** el zip en una carpeta. Usa una carpeta nueva para cada versión.
3. Doble clic en **install.cmd**.

El instalador copia FocusWShell a `%LOCALAPPDATA%\Programs\FocusWShell`, lo añade al menú Inicio y lo abre. Aparece como un icono en la bandeja, junto al reloj.

### «Windows protegió tu PC»

Los archivos todavía no están firmados, así que Windows puede avisarte la primera vez, porque vienen de internet. Elige **Más información → Ejecutar de todas formas**. Puedes comprobar la descarga con el archivo `.sha256` que se publica junto al zip:

```powershell
Get-FileHash .\FocusWShell-0.2.0-alpha-win-x64.zip -Algorithm SHA256
```

## Iniciar con Windows

Activa **Iniciar con Windows** en **General**, en la ventana principal, o elígelo con clic derecho en el icono de la bandeja. FocusWShell se abrirá al iniciar sesión.

## Actualizar

Instala la versión nueva igual: descomprime su zip en una carpeta nueva y ejecuta su `install.cmd`. El instalador cierra antes FocusWShell (devolviendo tus ventanas) y conserva tu configuración.

## Desinstalar

1. Abre `%LOCALAPPDATA%\Programs\FocusWShell`.
2. Clic derecho en **uninstall.ps1 → Ejecutar con PowerShell**.

Cierra FocusWShell, devolviendo todas las ventanas, y quita *Iniciar con Windows*, el acceso directo del menú Inicio y la app. Tu configuración se queda en `%LOCALAPPDATA%\FocusWShell`, por si vuelves. Para borrarla también, ejecútalo desde PowerShell con `-RemoveSettings`:

```powershell
& "$env:LOCALAPPDATA\Programs\FocusWShell\uninstall.ps1" -RemoveSettings
```

## Tu configuración

Todo lo que FocusWShell recuerda vive en `%LOCALAPPDATA%\FocusWShell`: tus paneles, las apps que ha visto, tus atajos y preferencias. Son archivos JSON normales. `errors.log` aparece ahí sólo si algo falla.
