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
$refs = @('mscorlib.dll','System.dll','System.Core.dll','System.Xml.dll','System.Management.dll','System.Data.dll') |
        ForEach-Object { '/r:' + (Join-Path $fw $_) }

$files = Get-ChildItem $src -Filter '*.cs' | ForEach-Object { $_.FullName }
$exe   = Join-Path $out 'cmdwatch.exe'

Write-Host "compilador : $csc"
Write-Host "fontes     : $($files.Count) arquivo(s)"

& $csc /nologo /optimize+ /platform:anycpu /target:exe /out:"$exe" $refs $files
if ($LASTEXITCODE -ne 0) { Write-Host "FALHA na compilacao" -ForegroundColor Red; exit 1 }

Write-Host "OK -> $exe" -ForegroundColor Green
Write-Host "tamanho: $((Get-Item $exe).Length) bytes"
