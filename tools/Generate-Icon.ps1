# Gera o icone do produto (assets\cmdwatch.ico) a partir de formas desenhadas
# em GDI+ - nao depende de fonte nem de imagem externa.
#
#   .\tools\Generate-Icon.ps1
#
# Saida: assets\cmdwatch.ico com 16, 24, 32, 48, 64, 128 e 256 px.

param(
    [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\cmdwatch.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$CYAN  = [System.Drawing.Color]::FromArgb(255, 0x3C, 0xE4, 0xEC)
$GREEN = [System.Drawing.Color]::FromArgb(255, 0x3A, 0xD4, 0x6E)
$TOP   = [System.Drawing.Color]::FromArgb(255, 0x2C, 0x2C, 0x33)
$BOT   = [System.Drawing.Color]::FromArgb(255, 0x13, 0x13, 0x17)

function New-RoundRect([System.Drawing.Rectangle]$r, [int]$radius) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    if ($d -gt $r.Width) { $d = $r.Width }
    if ($d -gt $r.Height) { $d = $r.Height }
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [math]::Max(1, [int]($s * 0.055))
    $r = New-Object System.Drawing.Rectangle $pad, $pad, ($s - 2 * $pad), ($s - 2 * $pad)
    $radius = [int]($s * 0.22)

    # fundo do quadrado com gradiente
    $path = New-RoundRect $r $radius
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $r, $TOP, $BOT, 90.0
    $g.FillPath($grad, $path)

    # borda ciano
    $bw = [math]::Max(1.0, $s * 0.030)
    $bpen = New-Object System.Drawing.Pen $CYAN, $bw
    $g.DrawPath($bpen, $path)

    # chevron ">" do prompt
    $x0 = $s * 0.28; $x1 = $s * 0.54
    $yT = $s * 0.29; $yM = $s * 0.50; $yB = $s * 0.67
    $cw = [math]::Max(2.0, $s * 0.105)
    $cpen = New-Object System.Drawing.Pen $CYAN, $cw
    $cpen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cpen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cpen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    if ($s -ge 32) {
        # halo suave do chevron
        $glowPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, $CYAN)), ($cw * 2.1)
        $glowPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $glowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLines($glowPen, @(
            (New-Object System.Drawing.PointF $x0, $yT),
            (New-Object System.Drawing.PointF $x1, $yM),
            (New-Object System.Drawing.PointF $x0, $yB)))
    }
    $g.DrawLines($cpen, @(
        (New-Object System.Drawing.PointF $x0, $yT),
        (New-Object System.Drawing.PointF $x1, $yM),
        (New-Object System.Drawing.PointF $x0, $yB)))

    # cursor "_" verde
    $uw = [math]::Max(2.0, $s * 0.085)
    $upen = New-Object System.Drawing.Pen $GREEN, $uw
    $upen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $upen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($upen, $s * 0.60, $yB, $s * 0.80, $yB)

    # ponto de "monitorando" (apenas a partir de 32px)
    if ($s -ge 32) {
        $cx = $s * 0.76; $cy = $s * 0.27; $rad = $s * 0.065
        $halo = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(80, $GREEN))
        $g.FillEllipse($halo, $cx - $rad * 2.2, $cy - $rad * 2.2, $rad * 4.4, $rad * 4.4)
        $dot = New-Object System.Drawing.SolidBrush $GREEN
        $g.FillEllipse($dot, $cx - $rad, $cy - $rad, $rad * 2, $rad * 2)
    }

    $g.Dispose()
    return $bmp
}

# ------------------------------------------------- container ICO (DIB, sem PNG)
function Save-Ico([string]$path, $list) {
    $dir = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $dir
    $bw.Write([UInt16]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]$list.Count)
    $offset = 6 + 16 * $list.Count
    foreach ($e in $list) {
        $bw.Write([Byte]$(if ($e.Size -ge 256) { 0 } else { $e.Size }))
        $bw.Write([Byte]$(if ($e.Size -ge 256) { 0 } else { $e.Size }))
        $bw.Write([Byte]0)
        $bw.Write([Byte]0)
        $bw.Write([UInt16]1)
        $bw.Write([UInt16]32)
        $bw.Write([UInt32]$e.Data.Length)
        $bw.Write([UInt32]$offset)
        $offset += $e.Data.Length
    }
    foreach ($e in $list) { $bw.Write($e.Data) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($path, $dir.ToArray())
    $bw.Close(); $dir.Close()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$entries = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $w = $bmp.Width; $h = $bmp.Height

    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $bmp.UnlockBits($data)
    $bmp.Dispose()

    # converte para bottom-up (ordem do DIB)
    $row = $w * 4
    $flip = New-Object byte[] $bytes.Length
    for ($y = 0; $y -lt $h; $y++) {
        [Array]::Copy($bytes, $y * $row, $flip, ($h - 1 - $y) * $row, $row)
    }

    $maskStride = [int]([math]::Ceiling($w / 32.0) * 4)
    $mask = New-Object byte[] ($maskStride * $h)   # zeros = totalmente opaco

    $hdr = New-Object byte[] 40
    [Array]::Copy([System.BitConverter]::GetBytes([int]40), 0, $hdr, 0, 4)
    [Array]::Copy([System.BitConverter]::GetBytes([int]$w), 0, $hdr, 4, 4)
    [Array]::Copy([System.BitConverter]::GetBytes([int]($h * 2)), 0, $hdr, 8, 4)
    [Array]::Copy([System.BitConverter]::GetBytes([int]1), 0, $hdr, 12, 4)
    [Array]::Copy([System.BitConverter]::GetBytes([int]32), 0, $hdr, 14, 4)
    [Array]::Copy([System.BitConverter]::GetBytes([int]0), 0, $hdr, 16, 4)
    [Array]::Copy([System.BitConverter]::GetBytes([int]($flip.Length + $mask.Length)), 0, $hdr, 20, 4)

    $payload = New-Object byte[] ($hdr.Length + $flip.Length + $mask.Length)
    [Array]::Copy($hdr, 0, $payload, 0, $hdr.Length)
    [Array]::Copy($flip, 0, $payload, $hdr.Length, $flip.Length)
    [Array]::Copy($mask, 0, $payload, $hdr.Length + $flip.Length, $mask.Length)

    $entries += , ([pscustomobject]@{ Size = $s; Data = $payload })
}

$dirOut = Split-Path -Parent $Out
if (-not (Test-Path $dirOut)) { New-Item -ItemType Directory -Force -Path $dirOut | Out-Null }

Save-Ico $Out $entries
Write-Host ("icone gerado: " + $Out + " (" + (Get-Item $Out).Length + " bytes, " + $entries.Count + " resolucoes)")

# ------------------------------------------------- ico compacto (multi-res)
# O executavel ja carrega o .ico completo nos recursos do PE (/win32icon),
# entao o recurso gerenciado usa apenas os quadros pequenos - evita embutir
# o arquivo inteiro duas vezes e o dobrar do tamanho do exe. Mantem 16/24/32/48
# para que a janela e a bandeja usem o quadro exato do tamanho pedido.
$small = @($entries | Where-Object { $_.Size -le 48 })
$smallPath = Join-Path $dirOut 'cmdwatch-32.ico'
Save-Ico $smallPath $small
Write-Host ("icone compacto: " + $smallPath + " (" + (Get-Item $smallPath).Length + " bytes, " + $small.Count + " resolucoes)")

# ------------------------------------------------- previa para conferencia
$prev = Join-Path $dirOut 'icon-preview.png'
$scale = 4
$sizes2 = @(16, 24, 32, 48, 64, 128, 256)
$cellW = 256; $cellH = 256
$pb = New-Object System.Drawing.Bitmap (($cellW + 20) * $sizes2.Count), ($cellH + 46)
$pg = [System.Drawing.Graphics]::FromImage($pb)
$pg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$pg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$pg.Clear([System.Drawing.Color]::FromArgb(255, 0x1B, 0x1B, 0x1E))
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xD8, 0xD8, 0xDC))
$font = New-Object System.Drawing.Font 'Consolas', 11
for ($i = 0; $i -lt $sizes2.Count; $i++) {
    $s = $sizes2[$i]
    $bmp = New-IconBitmap $s
    $x = 10 + $i * ($cellW + 20)
    $y = 12 + [int](($cellH - $s) / 2)
    $pg.DrawImage($bmp, $x, $y, $s, $s)
    $bmp.Dispose()
    $pg.DrawString("$s x $s", $font, $brush, $x, $cellH + 16)
}
$pg.Dispose()
$pb.Save($prev, [System.Drawing.Imaging.ImageFormat]::Png)
$pb.Dispose()
Write-Host ("previa: " + $prev)
