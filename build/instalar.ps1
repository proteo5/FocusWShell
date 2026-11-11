# Instala o actualiza la copia de uso diario de FocusWShell (Release) en una carpeta estable, fuera del
# repositorio, para usarla mientras se sigue desarrollando: compilar en Debug ya no choca con ella.
#
#   powershell -File build\instalar.ps1            instala o actualiza; si había una abierta, la vuelve a abrir
#   powershell -File build\instalar.ps1 -Abrir     y la abre aunque no hubiera ninguna abierta
#
# Si FocusWShell está abierto (esta copia o la de desarrollo), se le pide que salga con --salir:
# devuelve las ventanas a su sitio antes de cerrarse. "Iniciar con Windows" se marca desde la bandeja.
param(
    [string]$Destino = (Join-Path $env:LOCALAPPDATA 'Programs\FocusWShell'),
    [switch]$Abrir
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $Destino 'FocusWShell.exe'

$abierto = @(Get-Process FocusWShell -ErrorAction SilentlyContinue)
if ($abierto.Count -gt 0) {
    Write-Host "Cerrando FocusWShell (devuelve las ventanas a su sitio)..."
    & $abierto[0].Path --salir
    $limite = (Get-Date).AddSeconds(15)
    while ((Get-Process FocusWShell -ErrorAction SilentlyContinue) -and (Get-Date) -lt $limite) {
        Start-Sleep -Milliseconds 300
    }
    if (Get-Process FocusWShell -ErrorAction SilentlyContinue) {
        throw "FocusWShell no se cerró. Ciérralo desde la bandeja (Salir) y vuelve a intentarlo."
    }
}

Write-Host "Publicando en $Destino ..."
dotnet publish (Join-Path $repo 'src\FocusWShell.App\FocusWShell.App.csproj') -c Release -o $Destino --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló ($LASTEXITCODE)." }

# Qué versión quedó instalada: el commit, y si había cambios sin confirmar.
$commit = git -C $repo describe --always --dirty
Set-Content -Path (Join-Path $Destino 'version.txt') -Value "$commit $(Get-Date -Format s)" -Encoding UTF8
Write-Host "Instalado: $commit"

if ($abierto.Count -gt 0 -or $Abrir) {
    Start-Process $exe
    Write-Host "Abierto: $exe"
}
