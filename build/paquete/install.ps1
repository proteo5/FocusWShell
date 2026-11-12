# FocusWShell installer (alpha).
# Copies the app to %LOCALAPPDATA%\Programs\FocusWShell, adds a Start menu shortcut and opens it.
# No administrator rights needed. Your settings live in %LOCALAPPDATA%\FocusWShell and are kept.
#
# Instalador de FocusWShell (alfa). Copia la app a %LOCALAPPDATA%\Programs\FocusWShell, crea un acceso
# directo en el menú Inicio y la abre. No pide permisos de administrador. Tu configuración se conserva.
param(
    [string]$Target = (Join-Path $env:LOCALAPPDATA 'Programs\FocusWShell'),
    [switch]$NoStart
)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'app'
$exe = Join-Path $Target 'FocusWShell.exe'

if (-not (Test-Path (Join-Path $source 'FocusWShell.exe'))) {
    throw "FocusWShell.exe not found next to this script. Extract the whole zip first. / Descomprime todo el zip primero."
}

# If it is running, ask it to quit: it puts every window back where it was before closing.
$running = @(Get-Process FocusWShell -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host "Closing FocusWShell (it puts your windows back) / Cerrando FocusWShell..."
    & $running[0].Path --salir
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Process FocusWShell -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
    }

    if (Get-Process FocusWShell -ErrorAction SilentlyContinue) {
        throw "FocusWShell did not close. Use Exit in its tray icon and try again. / Ciérralo desde la bandeja y vuelve a intentarlo."
    }
}

Write-Host "Installing to $Target / Instalando en $Target ..."
if (Test-Path $exe) {
    Remove-Item $Target -Recurse -Force # an older version: replace it completely
}

New-Item -ItemType Directory -Force $Target | Out-Null
Copy-Item (Join-Path $source '*') $Target -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') $Target -Force

$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'FocusWShell.lnk'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $Target
$link.Description = 'FocusWShell'
$link.Save()

Write-Host ""
Write-Host "Installed. / Instalado."
Write-Host "  Emergency shortcut, puts every window back: Ctrl+Alt+Shift+Backspace"
Write-Host "  Atajo de emergencia, devuelve todas las ventanas: Ctrl+Alt+Mayús+Retroceso"
Write-Host "  Uninstall / Desinstalar: $Target\uninstall.ps1"

if (-not $NoStart) {
    Start-Process $exe
}
