# Gera o pacote MSIX do CMDWatcher para a Microsoft Store.
#
#   .\tools\gerar-msix.ps1                       # identidade provisoria (so teste local)
#   .\tools\gerar-msix.ps1 -IdentityName '<Package/Identity/Name>' `
#       -Publisher '<Package/Identity/Publisher>' -PublisherDisplayName '<nome>'
#
# Use os tres valores de "Product identity" do Partner Center para o pacote
# que vai ser enviado. Sem eles, o pacote usa valores provisorios e a Store
# o recusa. O pacote sai SEM assinatura: a Store assina na publicacao.
#
# Requer: out\cmdwatch.exe (build.ps1) e o Windows SDK (makeappx.exe).

param(
    [string]$IdentityName         = 'Dellanio.CMDWatcher',
    [string]$Publisher            = 'CN=Dellanio',
    [string]$PublisherDisplayName = 'Dellanio',
    [string]$Exe = '',
    [string]$OutDir = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
if (-not $Exe)    { $Exe = Join-Path $root 'out\cmdwatch.exe' }
if (-not $OutDir) { $OutDir = Join-Path $root 'out\msix' }
if (-not (Test-Path $Exe)) { throw "exe nao encontrado: $Exe (rode build.ps1)" }

$ver = (Get-Item $Exe).VersionInfo.FileVersion        # ex.: 1.3.2.0
if ($ver -notmatch '^\d+\.\d+\.\d+\.0$') { throw "versao '$ver' invalida para a Store (precisa x.y.z.0)" }

$mk = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
      Sort-Object FullName | Select-Object -Last 1
if (-not $mk) { throw 'makeappx.exe nao encontrado (instale o Windows SDK)' }

$stage = Join-Path $OutDir 'layout'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path "$stage\Assets" | Out-Null
Copy-Item $Exe "$stage\cmdwatch.exe"

# logos a partir da arte mestre (fundo #171717)
$master = [System.Drawing.Image]::FromFile((Join-Path $root 'assets\icon-master.png'))
$bg = [System.Drawing.ColorTranslator]::FromHtml('#171717')
function New-Logo([string]$nome, [int]$w, [int]$h, [double]$fill) {
    $b = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.Clear($bg)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $s = [int]([Math]::Min($w, $h) * $fill)
    $g.DrawImage($master, [int](($w - $s) / 2), [int](($h - $s) / 2), $s, $s)
    $g.Dispose(); $b.Save("$stage\Assets\$nome.png", [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
}
New-Logo 'StoreLogo'        50  50  1.0
New-Logo 'Square44x44Logo'  44  44  1.0
New-Logo 'Square71x71Logo'  71  71  1.0
New-Logo 'Square150x150Logo' 150 150 1.0
New-Logo 'Square310x310Logo' 310 310 1.0
New-Logo 'Wide310x150Logo'  310 150 1.0
New-Logo 'SplashScreen'     620 300 0.9
$master.Dispose()

$m = Get-Content (Join-Path $root 'packaging\msix\AppxManifest.template.xml') -Raw
$m = $m.Replace('@@NAME@@', $IdentityName).Replace('@@PUBLISHER@@', $Publisher).Replace('@@PUBDISPLAY@@', $PublisherDisplayName).Replace('@@VERSION@@', $ver)
[IO.File]::WriteAllText("$stage\AppxManifest.xml", $m, (New-Object Text.UTF8Encoding $false))

$pkg = Join-Path $OutDir ("cmdwatch_{0}_x64.msix" -f $ver)
if (Test-Path $pkg) { Remove-Item $pkg -Force }
& $mk.FullName pack /d $stage /p $pkg /o | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { throw 'makeappx falhou' }
"{0}  {1:N0} bytes  identidade={2}" -f $pkg, (Get-Item $pkg).Length, $IdentityName
