# Genera el paquete de una versión para publicarla (alfa en adelante): un zip autocontenido para win-x64,
# que no necesita .NET instalado, con el instalador y el desinstalador, y su suma SHA-256.
#
#   powershell -File build\release.ps1               deja artifacts\FocusWShell-<versión>-win-x64.zip
#   powershell -File build\release.ps1 -AllowDirty   aunque haya cambios sin confirmar (sólo para probar)
#
# La versión sale de Directory.Build.props. El zip lleva:
#   app\           la app publicada (Release, autocontenida)
#   install.cmd    doble clic para instalar (llama a install.ps1)
#   install.ps1    copia a %LOCALAPPDATA%\Programs\FocusWShell, acceso directo en Inicio, y la abre
#   uninstall.ps1  la cierra (devuelve las ventanas), quita «Iniciar con Windows», el acceso y la app
#   README.txt     cómo instalar y el atajo de emergencia, en inglés y español
param([switch]$AllowDirty)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

$version = ([xml](Get-Content (Join-Path $repo 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No hay <Version> en Directory.Build.props." }

$dirty = git -C $repo status --porcelain
if ($dirty -and -not $AllowDirty) {
    throw "Hay cambios sin confirmar: un release se hace desde un commit. Usa -AllowDirty sólo para probar."
}

$name = "FocusWShell-$version-win-x64"
$artifacts = Join-Path $repo 'artifacts'
$staging = Join-Path $artifacts $name
$zip = Join-Path $artifacts "$name.zip"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Force $staging | Out-Null

Write-Host "Publicando $version (autocontenida, win-x64)..."
dotnet publish (Join-Path $repo 'src\FocusWShell.App\FocusWShell.App.csproj') -c Release -r win-x64 --self-contained true `
    -o (Join-Path $staging 'app') --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló ($LASTEXITCODE)." }

$paquete = Join-Path $PSScriptRoot 'paquete'
Copy-Item (Join-Path $paquete 'install.cmd'), (Join-Path $paquete 'install.ps1'), (Join-Path $paquete 'uninstall.ps1') $staging
(Get-Content (Join-Path $paquete 'README.txt') -Raw -Encoding UTF8).Replace('{VERSION}', $version) |
    Set-Content (Join-Path $staging 'README.txt') -Encoding UTF8
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $staging 'LICENSE.txt')

$commit = git -C $repo describe --always --dirty
Set-Content -Path (Join-Path $staging 'app\version.txt') -Value "$version $commit" -Encoding UTF8

Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $name.zip" -Encoding ASCII

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Listo: $zip ($size MB)"
Write-Host "SHA-256: $hash"
