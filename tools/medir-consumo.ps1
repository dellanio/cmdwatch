# medir-consumo.ps1 - mede o consumo de CPU e memoria do cmdwatch
#
# Uso:
#   powershell -ExecutionPolicy Bypass -File .\tools\medir-consumo.ps1 -Exe .\out\cmdwatch.exe -Segundos 60
#
# O app e aberto com a GUI visivel e ocioso; a cada 5 s sao lidos o tempo de
# CPU acumulado, o working set e a memoria privada. No fim, a media do
# percentual de CPU (em relacao a maquina inteira) e os picos de memoria.
param([string]$Exe = "$PSScriptRoot\..\out\cmdwatch.exe",
      [int]$Segundos = 60,
      [string]$Rotulo = 'cmdwatch')
$ErrorActionPreference = 'Continue'
if (-not (Test-Path $Exe)) { "exe nao encontrado: $Exe"; exit 1 }
$nucleos = [Environment]::ProcessorCount
$g = Start-Process $Exe -ArgumentList 'gui', '--quiet' -PassThru
Start-Sleep 4
$p = Get-Process -Id $g.Id
$cpuAnterior = $p.TotalProcessorTime.TotalSeconds
$tAnterior = Get-Date
$maxWs = 0; $maxPriv = 0; $amostras = @()
Write-Output "[$Rotulo] exe=$Exe  nucleos=$nucleos"
for ($i = 0; $i -lt [int]($Segundos / 5); $i++) {
    Start-Sleep 5
    $p.Refresh()
    $cpuAgora = $p.TotalProcessorTime.TotalSeconds
    $tAgora = Get-Date
    $dt = ($tAgora - $tAnterior).TotalSeconds
    $pct = ($cpuAgora - $cpuAnterior) / $dt / $nucleos * 100
    $amostras += $pct
    $ws = $p.WorkingSet64 / 1MB; $pr = $p.PrivateMemorySize64 / 1MB
    if ($ws -gt $maxWs) { $maxWs = $ws }
    if ($pr -gt $maxPriv) { $maxPriv = $pr }
    Write-Output ("  {0:HH:mm:ss}  cpu={1,6:N2}%  ws={2,7:N1}MB  privado={3,7:N1}MB  threads={4}" -f `
        $tAgora, $pct, $ws, $pr, $p.Threads.Count)
    $cpuAnterior = $cpuAgora; $tAnterior = $tAgora
}
$media = ($amostras | Measure-Object -Average).Average
Write-Output ("[{0}] media cpu = {1:N2}% da maquina | pico ws = {2:N1}MB | pico privado = {3:N1}MB" -f $Rotulo, $media, $maxWs, $maxPriv)
try { $g.CloseMainWindow() | Out-Null; if (-not $g.WaitForExit(5000)) { $g.Kill() } } catch {}
