# Genera el icono de FocusWShell (src\FocusWShell.App\Assets\FocusWShell.ico): un cuadrado oscuro con tres
# columnas, Contexto | Foco | Trabajo, como el icono de la bandeja. Varios tamaños en un solo .ico (PNG).
#
#   powershell -File build\icono.ps1
param([string]$Salida = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\FocusWShell.App\Assets\FocusWShell.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 32.0

    # Fondo: el color del deck, con esquinas redondeadas.
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x14, 0x17, 0x1E))
    $g.FillPath($bg, (RoundedRect 0 0 $size $size (6 * $s)))

    # Columnas: los paneles a los lados, más bajos; el foco en medio, azul y más alto.
    $side = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 150, 160, 175))
    $focus = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 40, 120, 220))
    $g.FillPath($side, (RoundedRect (4 * $s) (8 * $s) (5.5 * $s) (16 * $s) (1.5 * $s)))
    $g.FillPath($focus, (RoundedRect (11 * $s) (5 * $s) (10 * $s) (22 * $s) (1.8 * $s)))
    $g.FillPath($side, (RoundedRect (22.5 * $s) (8 * $s) (5.5 * $s) (16 * $s) (1.5 * $s)))
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = $sizes | ForEach-Object { , (Frame $_) }

# Formato ICO: cabecera, una entrada por tamaño y, detrás, cada imagen en PNG.
New-Item -ItemType Directory -Force (Split-Path $Salida) | Out-Null
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $n = $sizes[$i]; $bytes = $frames[$i]
    $w.Write([byte]($(if ($n -ge 256) { 0 } else { $n })))
    $w.Write([byte]($(if ($n -ge 256) { 0 } else { $n })))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$bytes.Length); $w.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($bytes in $frames) { $w.Write($bytes) }
$w.Flush()
[System.IO.File]::WriteAllBytes($Salida, $out.ToArray())
Write-Host "Icono: $Salida ($($sizes -join ', ') px)"
