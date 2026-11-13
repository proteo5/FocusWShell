# FocusWShell uninstaller (alpha).
# Closes FocusWShell (it puts every window back), removes "Start with Windows", the Start menu shortcut and
# the app. Your settings in %LOCALAPPDATA%\FocusWShell are kept unless you pass -RemoveSettings.
#
# Desinstalador de FocusWShell (alfa). Cierra FocusWShell (devuelve las ventanas a su sitio), quita «Iniciar
# con Windows», el acceso directo y la app. Tu configuración se conserva, salvo con -RemoveSettings.
param(
    [string]$Target = (Join-Path $env:LOCALAPPDATA 'Programs\FocusWShell'),
    [switch]$RemoveSettings
)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $Target 'FocusWShell.exe'

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

# "Start with Windows", only if it points to this copy.
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$value = (Get-ItemProperty $run -Name FocusWShell -ErrorAction SilentlyContinue).FocusWShell
if ($value -and $value.Trim('"') -ieq $exe) {
    Remove-ItemProperty $run -Name FocusWShell
}

$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'FocusWShell.lnk'
if (Test-Path $shortcut) {
    Remove-Item $shortcut -Force
}

if (Test-Path $exe) {
    # Its contents first: that works even if the folder is open in a console or an Explorer window
    # (it usually is: this script lives there). Then the folder, if Windows lets go of it.
    Set-Location $env:USERPROFILE
    Get-ChildItem $Target -Force | Remove-Item -Recurse -Force
    Remove-Item $Target -Force -ErrorAction SilentlyContinue
    if (Test-Path $Target) {
        Write-Host "The empty folder $Target is still open in a window; you can delete it later."
        Write-Host "La carpeta vacía $Target sigue abierta en una ventana; puedes borrarla después."
    }
}

$settings = Join-Path $env:LOCALAPPDATA 'FocusWShell'
if ($RemoveSettings -and (Test-Path $settings)) {
    Remove-Item $settings -Recurse -Force
    Write-Host "Settings removed. / Configuración borrada."
}

Write-Host "FocusWShell uninstalled. / FocusWShell desinstalado."
