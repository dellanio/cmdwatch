# summarize-log.ps1 - resume o log JSONL do cmdwatch: sessoes, scripts e buracos.
param(
    [string]$Log = 'D:\projetos-pessoal\CMDWatcher\cmdwatch-log.jsonl'
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Log)) { throw "log nao encontrado: $Log" }

$linhas = Get-Content $Log
Write-Host "log: $Log"
Write-Host "linhas: $($linhas.Count)  ($((Get-Item $Log).Length) bytes)"

$eventos = @()
foreach ($l in $linhas) {
    if ($l.Length -lt 5) { continue }
    try { $eventos += ($l | ConvertFrom-Json) } catch { }
}
Write-Host "eventos validos: $($eventos.Count)"
if ($eventos.Count -eq 0) { return }

Write-Host ''
Write-Host '=== por tipo ==='
$eventos | Group-Object type | Sort-Object Count -Descending | ForEach-Object { "  {0,-14} {1}" -f $_.Name, $_.Count }

Write-Host ''
Write-Host '=== sessoes (batch_open) ==='
$abre = $eventos | Where-Object { $_.type -eq 'batch_open' }
$fecha = $eventos | Where-Object { $_.type -eq 'batch_close' }
foreach ($a in $abre) {
    $f = $fecha | Where-Object { $_.cmdPid -eq $a.cmdPid } | Select-Object -First 1
    $dur = if ($f) { [int](([datetime]$f.ts) - ([datetime]$a.ts)).TotalMilliseconds } else { -1 }
    $passos = ($eventos | Where-Object { $_.type -eq 'batch_step' -and $_.cmdPid -eq $a.cmdPid }).Count
    "{0}  pid={1,-6} dur={2,-7} passos={3,-3} {4}" -f $a.ts, $a.cmdPid, $dur, $passos, $a.scriptName
}

Write-Host ''
Write-Host '=== por hora ==='
$eventos | Group-Object { ([datetime]$_.ts).ToString('dd/MM HH') } | Sort-Object Name |
    ForEach-Object { "  {0}  {1,4} evento(s)" -f $_.Name, $_.Count }

Write-Host ''
Write-Host '=== scripts distintos ==='
$eventos | Where-Object { $_.scriptName } | Group-Object scriptName | Sort-Object Count -Descending |
    Select-Object -First 25 | ForEach-Object { "  {0,-45} {1}" -f $_.Name, $_.Count }

Write-Host ''
Write-Host '=== buracos (>30s sem evento) ==='
$ts = $eventos | ForEach-Object { [datetime]$_.ts } | Sort-Object
for ($i = 1; $i -lt $ts.Count; $i++) {
    $d = ($ts[$i] - $ts[$i-1]).TotalSeconds
    if ($d -gt 30) { "  {0} -> {1}  ({2:N0}s sem evento)" -f $ts[$i-1].ToString('HH:mm:ss'), $ts[$i].ToString('HH:mm:ss'), $d }
}
