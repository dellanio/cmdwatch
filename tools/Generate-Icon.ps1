# Gera os icones do produto a partir da arte mestre (assets\icon-master.png,
# 512x512, glifo "CW" em amarelo acido #E2FF26 sobre fundo #171717).
# Usa so GDI+ (nao depende de Python, fonte ou imagem externa).
#
#   .\tools\Generate-Icon.ps1
#   .\tools\Generate-Icon.ps1 -Master caminho\outra-arte.png
#
# Saidas (sobrescreve):
#   assets\cmdwatch.ico     16, 24, 32, 48, 64, 128 e 256 px  (icone do .exe)
#   assets\cmdwatch-32.ico  16, 24, 32 e 48 px                (janela e bandeja,
#                                                              embutido em runtime)
#   assets\icon-preview.png copia da arte mestre (usada no README)
#
# Os quadros sao gravados como PNG dentro do .ico (aceito pelo Windows Vista+
# e pelo System.Drawing.Icon do .NET Framework 4.8).

param(
    [string]$Master = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\icon-master.png'),
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $Master)) { throw "arte mestre nao encontrada: $Master" }
$src = [System.Drawing.Image]::FromFile((Resolve-Path $Master).Path)

function New-PngFrame([System.Drawing.Image]$img, [int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($img, 0, 0, $s, $s)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

function Write-Ico([string]$path, [int[]]$sizes) {
    $frames = @(); foreach ($s in $sizes) { $frames += ,@($s, (New-PngFrame $src $s)) }
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$frames.Count)   # ICONDIR
    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) {
        $s = $f[0]; $data = $f[1]
        $dim = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
        $offset += $data.Length
    }
    foreach ($f in $frames) { $bw.Write([byte[]]$f[1]) }
    $bw.Dispose(); $fs.Dispose()
    "{0}  {1:N0} bytes  ({2})" -f $path, (Get-Item $path).Length, ($sizes -join ',')
}

Write-Ico (Join-Path $OutDir 'cmdwatch.ico')    @(16, 24, 32, 48, 64, 128, 256)
Write-Ico (Join-Path $OutDir 'cmdwatch-32.ico') @(16, 24, 32, 48)

$prev = Join-Path $OutDir 'icon-preview.png'
if ((Resolve-Path $Master).Path -ne $prev) { Copy-Item -Force $Master $prev }
$src.Dispose()
