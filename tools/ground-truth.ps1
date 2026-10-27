# ground-truth.ps1 - referencia independente do cmdwatch: registra a criacao de
# todo cmd.exe (com linha de comando e pai) usando o proprio WMI, para
# comparar com o que o cmdwatch capturou na mesma janela.
#
#   .\tools\ground-truth.ps1 -Seconds 90 -Out .build\gt.csv

param(
    [int]$Seconds = 90,
    [string]$Out = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Out -eq '') { $Out = Join-Path $root '.build\gt.csv' }

$registros = New-Object System.Collections.Generic.List[object]
$fim = (Get-Date).AddSeconds($Seconds)

Write-Host "rastreando cmd.exe por $Seconds s -> $Out"

# --- 1) rastreio por evento do WMI (preciso, mas depende do WMI)
$job = Start-Job -ScriptBlock {
    $saida = @()
    $reg = Register-CimIndicationEvent -Query "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName='cmd.exe'" -SourceIdentifier 'gt-cmd'
    try {
        while ($true) {
            $ev = Wait-Event -SourceIdentifier 'gt-cmd' -Timeout 5
            if ($ev -ne $null) {
                $saida += [pscustomobject]@{
                    t    = (Get-Date).ToString('HH:mm:ss.fff')
                    pid  = $ev.SourceEventArgs.NewEvent.ProcessID
                    pai  = $ev.SourceEventArgs.NewEvent.ParentProcessID
                    cmd  = $ev.SourceEventArgs.NewEvent.CommandLine
                }
                Remove-Event -EventIdentifier $ev.EventIdentifier
            }
        }
    } finally { Unregister-Event -SourceIdentifier 'gt-cmd' -ErrorAction SilentlyContinue }
}

# --- 2) varredura por polling (rede de seguranca do WMI)
$conhecidos = @{}
foreach ($c in (Get-Process cmd -ErrorAction SilentlyContinue)) { $conhecidos[$c.Id] = $true }

while ((Get-Date) -lt $fim) {
    foreach ($c in (Get-Process cmd -ErrorAction SilentlyContinue)) {
        if (-not $conhecidos.ContainsKey($c.Id)) {
            $conhecidos[$c.Id] = $true
            $w = Get-CimInstance Win32_Process -Filter "ProcessId=$($c.Id)" -ErrorAction SilentlyContinue
            if ($w) {
                $registros.Add([pscustomobject]@{
                    t   = (Get-Date).ToString('HH:mm:ss.fff')
                    pid = $c.Id
                    pai = $w.ParentProcessId
                    cmd = $w.CommandLine
                    src = 'poll'
                })
            }
        }
    }
    Start-Sleep -Milliseconds 120
}

Stop-Job $job -ErrorAction SilentlyContinue
try { $evs = Receive-Job $job -ErrorAction SilentlyContinue } catch { $evs = @() }
Remove-Job $job -Force -ErrorAction SilentlyContinue

foreach ($e in $evs) {
    $registros.Add([pscustomobject]@{ t = $e.t; pid = $e.pid; pai = $e.pai; cmd = $e.cmd; src = 'wmi' })
}

$registros | Sort-Object t | Export-Csv -Path $Out -NoTypeInformation -Encoding UTF8
Write-Host "capturados $($registros.Count) cmd.exe novos -> $Out"
$registros | Sort-Object t | ForEach-Object { "  [{0}] pid={1,-6} pai={2,-6} {3}" -f $_.t, $_.pid, $_.pai, $_.cmd }
