# CPU do painel (gui) parado >=60 s e sob rajada. A janela do painel e
# necessaria ao teste (abre minimizada). Auxiliares ocultos.
param([string]$Exe = "$PSScriptRoot\..\out\cmdwatch.exe", [switch]$Visivel)
$repro = Join-Path $env:TEMP 'cmdw-tests\repro'
$log = Join-Path $env:TEMP 'cmdw-tests\logs\gui-cpu.jsonl'
$nc = [Environment]::ProcessorCount
$g = Start-Process $Exe -ArgumentList 'gui','--log',"`"$log`"" -WindowStyle $(if ($Visivel) { 'Normal' } else { 'Minimized' }) -PassThru
Start-Sleep 15
function Medir($seg, [scriptblock]$carga) {
  $g.Refresh(); $c0 = $g.TotalProcessorTime.TotalMilliseconds; $t0 = Get-Date
  if ($carga) { & $carga } else { Start-Sleep $seg }
  $g.Refresh(); $ms = ((Get-Date) - $t0).TotalMilliseconds
  $nuc = ($g.TotalProcessorTime.TotalMilliseconds - $c0) / $ms * 100
  "{0:N1}% de 1 nucleo = {1:N2}% da maquina ({2} logicos), {3:N0} s" -f $nuc, ($nuc / $nc), $nc, ($ms / 1000)
}
"parado : " + (Medir 65 $null)
"rajada : " + (Medir 0 {
  for ($i = 0; $i -lt 30; $i++) {
    $p = New-Object Diagnostics.ProcessStartInfo -ArgumentList 'cmd.exe', ("/c `"" + (Join-Path $repro 'short.cmd') + "`"")
    $p.UseShellExecute = $false; $p.CreateNoWindow = $true; [Diagnostics.Process]::Start($p).WaitForExit()
    Start-Sleep -Milliseconds 500 }
})
"memoria: {0:N1} MB privados" -f ($g.PrivateMemorySize64 / 1MB)
try { $g.Kill() } catch {}