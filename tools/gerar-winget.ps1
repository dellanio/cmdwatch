# gerar-winget.ps1 - gera os manifestos do cmdwatch para o winget-pkgs
#
# Uso:
#   powershell -ExecutionPolicy Bypass -File .\tools\gerar-winget.ps1 -Versao 1.3.1 -Hash 80E4...
#
# O script escreve os quatro arquivos do esquema 1.28.0 em
# packaging\winget\<versao>\ e, com -CopiarPara <repo>, tambem no caminho que o
# winget-pkgs espera (manifests\d\dellanio\cmdwatch\<versao>\).
param(
    [Parameter(Mandatory = $true)][string]$Versao,
    [Parameter(Mandatory = $true)][string]$Hash,
    [string]$ReleaseDate = (Get-Date -Format 'yyyy-MM-dd'),
    [string]$Destino = "",
    [string]$CopiarPara = ""
)
$ErrorActionPreference = 'Stop'

# $PSScriptRoot nao existe dentro do param(): o destino padrao e resolvido aqui
if ($Destino -eq '') { $Destino = Join-Path $PSScriptRoot '..\packaging\winget' }
$Destino = [IO.Path]::GetFullPath($Destino)

$Hash = $Hash.Trim().ToUpperInvariant()
if ($Hash.Length -ne 64) { throw "hash invalido (esperado 64 caracteres hex): $Hash" }
$dir = Join-Path $Destino $Versao
New-Item -ItemType Directory -Force $dir | Out-Null

$url = "https://github.com/dellanio/cmdwatch/releases/download/v$Versao/cmdwatch.exe"
$notasUrl = "https://github.com/dellanio/cmdwatch/releases/tag/v$Versao"
$ident = 'dellanio.cmdwatch'
$esquema = '1.28.0'

# ------------------------------------------------------------------ helpers
function Escrever([string]$nome, [string]$conteudo)
{
    [IO.File]::WriteAllText((Join-Path $dir $nome), $conteudo, (New-Object System.Text.UTF8Encoding($false)))
}

# comentario de schema do yaml (o '$schema' precisa sair literal)
function Cabecalho([string]$tipo)
{
    '# yaml-language-server: $schema=https://aka.ms/winget-manifest.' + $tipo + '.' + $esquema + '.schema.json'
}

# texto de bloco YAML: cada linha com dois espacos de indentacao
function Bloco([string]$texto)
{
    ($texto -split "`r?`n" | ForEach-Object { '  ' + $_.TrimEnd() }) -join "`n"
}

# ------------------------------------------------------------------ versao
$txtVersao = @"
$(Cabecalho 'version')

PackageIdentifier: $ident
PackageVersion: $Versao
DefaultLocale: pt-BR
ManifestType: version
ManifestVersion: $esquema
"@
Escrever "$ident.yaml" $txtVersao

# --------------------------------------------------------------- instalador
$txtInstalador = @"
$(Cabecalho 'installer')

PackageIdentifier: $ident
PackageVersion: $Versao
InstallerType: portable
Commands:
- cmdwatch
ReleaseDate: $ReleaseDate
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $Hash
ManifestType: installer
ManifestVersion: $esquema
"@
Escrever "$ident.installer.yaml" $txtInstalador

# ------------------------------------------------------------------ locales
$descPt = @"
Acompanhe a execução de scripts .cmd e .bat em tempo real: quem chamou o
script, cada comando executado, as janelas abertas e o consumo de CPU e
memória de cada processo da máquina. O painel mostra o retrato vivo da
máquina numa segunda aba e a memória da máquina em tempo real na barra de
indicadores. Arquivo único, sem instalador e sem privilégios.
"@

$txtPt = @"
$(Cabecalho 'defaultLocale')

PackageIdentifier: $ident
PackageVersion: $Versao
PackageLocale: pt-BR
Publisher: Dellanio
PublisherUrl: https://github.com/dellanio
PublisherSupportUrl: https://github.com/dellanio/cmdwatch/issues
Author: Dellanio
PackageName: cmdwatch
PackageUrl: https://github.com/dellanio/cmdwatch
License: MIT
LicenseUrl: https://github.com/dellanio/cmdwatch/blob/main/LICENSE
Copyright: Copyright (c) 2026 Dellanio
CopyrightUrl: https://github.com/dellanio/cmdwatch/blob/main/LICENSE
ShortDescription: Monitor de sessões CMD em tempo real
Description: |-
$(Bloco $descPt)
Tags:
- cmd
- bat
- script
- monitor
- console
- terminal
ReleaseNotes: |-
$(Bloco $notasUrl)
ReleaseNotesUrl: $notasUrl
ManifestType: defaultLocale
ManifestVersion: $esquema
"@
Escrever "$ident.locale.pt-BR.yaml" $txtPt

$descEn = @"
Watch .cmd and .bat scripts run in real time: who called the script, every
command executed, the windows opened, and the CPU and memory use of each
process on the machine. The panel shows a live picture of the machine in a
second tab and the machine memory in real time in the KPI strip. Single
file, no installer and no privileges required.
"@

$txtEn = @"
$(Cabecalho 'locale')

PackageIdentifier: $ident
PackageVersion: $Versao
PackageLocale: en-US
Publisher: Dellanio
PublisherUrl: https://github.com/dellanio
PublisherSupportUrl: https://github.com/dellanio/cmdwatch/issues
Author: Dellanio
PackageName: cmdwatch
PackageUrl: https://github.com/dellanio/cmdwatch
License: MIT
LicenseUrl: https://github.com/dellanio/cmdwatch/blob/main/LICENSE
Copyright: Copyright (c) 2026 Dellanio
CopyrightUrl: https://github.com/dellanio/cmdwatch/blob/main/LICENSE
ShortDescription: Real-time CMD session monitor
Description: |-
$(Bloco $descEn)
Tags:
- cmd
- bat
- script
- monitor
- console
- terminal
ReleaseNotes: |-
$(Bloco $notasUrl)
ReleaseNotesUrl: $notasUrl
ManifestType: locale
ManifestVersion: $esquema
"@
Escrever "$ident.locale.en-US.yaml" $txtEn

Write-Output "manifestos do winget gerados em $dir"
Get-ChildItem $dir | Select-Object Name, Length | Format-Table -AutoSize

# copia para um checkout do winget-pkgs, no caminho que o repositorio exige
if ($CopiarPara -ne '') {
    $destino = Join-Path $CopiarPara "manifests\d\dellanio\cmdwatch\$Versao"
    New-Item -ItemType Directory -Force $destino | Out-Null
    Get-ChildItem $dir -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $destino $_.Name) -Force }
    Write-Output "copiado para $destino"
}
