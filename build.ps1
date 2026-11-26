# build.ps1 — compila o cmdwatch.exe a partir de src/
#
# Usa o compilador Roslyn (NuGet Microsoft.Net.Compilers.Toolset). Se nao estiver
# em .build\roslyn, baixa automaticamente (requer internet na primeira vez).
# O csc.exe do .NET Framework e C# 5 e NAO compila este codigo.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$out  = Join-Path $root 'out'
$fw   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$roslynDir = Join-Path $root '.build\roslyn'

New-Item -ItemType Directory -Force -Path $out | Out-Null

function Get-Roslyn {
    $csc = Get-ChildItem $roslynDir -Recurse -Filter 'csc.exe' -ErrorAction SilentlyContinue |
           Select-Object -First 1
    if ($csc) { return $csc.FullName }

    Write-Host "compilador Roslyn ausente - baixando (Microsoft.Net.Compilers.Toolset 4.8.0)..."
    New-Item -ItemType Directory -Force -Path $roslynDir | Out-Null
    $zip = Join-Path $root '.build\roslyn.zip'
    Invoke-WebRequest -Uri 'https://www.nuget.org/api/v2/package/Microsoft.Net.Compilers.Toolset/4.8.0' `
                       -OutFile $zip -UseBasicParsing -TimeoutSec 180
    Expand-Archive -Path $zip -DestinationPath $roslynDir -Force
    $csc = Get-ChildItem $roslynDir -Recurse -Filter 'csc.exe' | Select-Object -First 1
    if (-not $csc) { throw 'nao foi possivel obter o compilador Roslyn' }
    return $csc.FullName
}

$csc = Get-Roslyn
$refs = @('mscorlib.dll','System.dll','System.Core.dll','System.Xml.dll','System.Management.dll','System.Data.dll','System.Drawing.dll','System.Windows.Forms.dll') |
        ForEach-Object { '/r:' + (Join-Path $fw $_) }

$files = Get-ChildItem $src -Filter '*.cs' -Recurse | ForEach-Object { $_.FullName }
$exe   = Join-Path $out 'cmdwatch.exe'

# icone do produto: /win32icon define o icone do .exe (Explorer, barra de
# tarefas) e /resource embute o quadro compacto no assembly para a GUI
# carregar em runtime (janela e bandeja do Windows).
$icon     = Join-Path $root 'assets\cmdwatch.ico'
$iconSmall = Join-Path $root 'assets\cmdwatch-32.ico'
$extra = @()
if (Test-Path $icon) {
    $extra += '/win32icon:"' + $icon + '"'
    if (Test-Path $iconSmall) {
        $extra += '/resource:"' + $iconSmall + '",cmdwatch.ico'
    } else {
        $extra += '/resource:"' + $icon + '",cmdwatch.ico'
    }
} else {
    Write-Host "aviso: assets\cmdwatch.ico ausente - o exe sairia sem icone" -ForegroundColor Yellow
}

# --- metadata do arquivo (exigida pelo SignPath) -----------------------------
# A Fundacao SignPath assina apenas binarios que carregam ProductName e
# ProductVersion no resource de versao do PE (veja
# packaging/signpath/artifact-configuration.xml). A versao vem do proprio
# codigo-fonte (src\Program.cs) para nao existir duas fontes de verdade.
$programCs = Join-Path $src 'Program.cs'
$m = [regex]::Match([IO.File]::ReadAllText($programCs), 'internal\s+const\s+string\s+Version\s*=\s*"([^"]+)"')
if (-not $m.Success) { throw 'nao foi possivel ler a versao em src\Program.cs' }
$versao = $m.Groups[1].Value
$asmVer = ($versao -replace '-', '.') + '.0'

$asmInfo = Join-Path $root '.build\AssemblyInfo.cs'
$asmInfoTxt = @"
// gerado por build.ps1 - nao versionar
using System.Reflection;
[assembly: AssemblyTitle("cmdwatch")]
[assembly: AssemblyProduct("cmdwatch")]
[assembly: AssemblyCompany("Dellanio")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Dellanio")]
[assembly: AssemblyVersion("$asmVer")]
[assembly: AssemblyFileVersion("$asmVer")]
[assembly: AssemblyInformationalVersion("$versao")]
"@
[IO.File]::WriteAllText($asmInfo, $asmInfoTxt, (New-Object Text.UTF8Encoding($false)))

Write-Host "compilador : $csc"
Write-Host "fontes     : $($files.Count) arquivo(s) + metadata gerada (v$versao)"

# /target:winexe = aplicacao Windows: dar duplo clique no .exe NAO abre um
# console. Os modos de terminal (monitor/analyze/trace) anexam o console do
# terminal que os chamou - ver src/ConsoleHost.cs.
& $csc /nologo /optimize+ /platform:anycpu /target:winexe /out:"$exe" $refs $extra $files $asmInfo
if ($LASTEXITCODE -ne 0) { Write-Host "FALHA na compilacao" -ForegroundColor Red; exit 1 }

# o SignPath recusa binarios sem metadata de produto/versao: falha aqui, no
# build, e nao no momento de assinar a release.
$fvi = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
if ($fvi.ProductName -ne 'cmdwatch' -or $fvi.ProductVersion -ne $versao) {
    Write-Host "FALHA: metadata do exe invalida (ProductName='$($fvi.ProductName)' ProductVersion='$($fvi.ProductVersion)')" -ForegroundColor Red
    exit 1
}

Write-Host "OK -> $exe" -ForegroundColor Green
Write-Host "tamanho : $((Get-Item $exe).Length) bytes"
Write-Host "metadata: ProductName=$($fvi.ProductName) ProductVersion=$($fvi.ProductVersion) FileVersion=$($fvi.FileVersion)"
