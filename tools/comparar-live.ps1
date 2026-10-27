# comparar-live.ps1 - roda a "verdade de campo" (rastreio de criacao de cmd.exe)
# e o cmdwatch CLI na MESMA janela e compara o que cada um viu.
#
#   .\tools\comparar-live.ps1 -Seconds 90

param(
    [int]$Seconds = 90,
    [switch]$Verbose
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe  = Join-Path $root 'out\cmdwatch.exe'
$gtCsv = Join-Path $root '.build\gt-live.csv'
$cwCsv = Join-Path $root '.build\cw-live.jsonl'

Write-Host "=== janela de $Seconds s: verdade de campo x cmdwatch ===`n"

# ------------------------------------------------- 1. verdade de campo (WMI)
$job = Start-Job -ScriptBlock {
    $saida = @()
    $reg = Register-CimIndicationEvent -Query "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName='cmd.exe'" -SourceIdentifier 'gt-live'
    try {
        while ($true) {
            $ev = Wait-Event -SourceIdentifier 'gt-live' -Timeout 3
            if ($ev -ne $null) {
                $saida += [pscustomobject]@{
                    ts  = (Get-Date).ToString('o')
                    pid = $ev.SourceEventArgs.NewEvent.ProcessID
                    ppid= $ev.SourceEventArgs.NewEvent.ParentProcessID
                    cmd = $ev.SourceEventArgs.NewEvent.CommandLine
                }
                Remove-Event -EventIdentifier $ev.EventIdentifier
            }
        }
    } finally { Unregister-Event -SourceIdentifier 'gt-live' -ErrorAction SilentlyContinue }
}

# ------------------------------------------------- 2. cmdwatch CLI
if (Test-Path $cwCsv) { Remove-Item $cwCsv -Force }
$mon = Start-Process -FilePath $exe `
    -ArgumentList 'monitor', '--duration', "$Seconds", '--json', '--log', $cwCsv `
    -PassThru -WindowStyle Minimized

$fim = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $fim) { Start-Sleep -Seconds 2 }

$mon | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue

# ------------------------------------------------- 3. coletar a verdade de campo
Stop-Job $job -ErrorAction SilentlyContinue
$evs = @()
try { $evs = @(Receive-Job $job -ErrorAction SilentlyContinue) } catch { }
Remove-Job $job -Force -ErrorAction SilentlyContinue

# ------------------------------------------------- 4. comparar
$pidsCW = @{}
if (Test-Path $cwCsv) {
    foreach ($l in (Get-Content $cwCsv)) {
        if ($l.Length -lt 5) { continue }
        try {
            $e = $l | ConvertFrom-Json
            if ($e.cmdPid) { $pidsCW[[int]$e.cmdPid] = $e }
        } catch { }
    }
}

Write-Host ''
Write-Host "cmd.exe criados na janela (rastreio WMI): $($evs.Count)"
Write-Host "sessoes abertas pelo cmdwatch: $($pidsCW.Count)"
Write-Host ''

$perdidos = @()
$foraDeEscopo = @()
$ok = @()

foreach ($e in $evs) {
    $cl = $e.cmd
    $ehScript = $cl -match '\.(cmd|bat)(\s|"|$)' -or $cl -match 'call\s+"?[^"]*\.(cmd|bat)'
    if ($pidsCW.ContainsKey([int]$e.pid)) {
        $ok += $e
    } elseif ($ehScript) {
        $perdidos += $e
    } else {
        $foraDeEscopo += $e
    }
}

Write-Host "=== capturados pelo cmdwatch ($($ok.Count)) ==="
foreach ($e in $ok) { "  pid={0,-6} {1}" -f $e.pid, $e.cmd }

Write-Host ''
Write-Host "=== PERDIDOS: eram .cmd/.bat e o cmdwatch NAO viu ($($perdidos.Count)) ==="
foreach ($e in $perdidos) { "  pid={0,-6} pai={1,-6} {2}" -f $e.pid, $e.ppid, $e.cmd }
if ($perdidos.Count -eq 0) { '  (nenhum)' }

Write-Host ''
Write-Host "=== fora do escopo: cmd.exe de comando, nao de script .cmd ($($foraDeEscopo.Count)) ==="
foreach ($e in $foraDeEscopo) { "  pid={0,-6} {1}" -f $e.pid, $e.cmd }
