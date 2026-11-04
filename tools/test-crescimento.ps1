# Teste longo de crescimento: monitor oculto por N minutos com churn de
# processos (.cmd curto oculto) e de janelas (formulario pequeno do teste).
# Mede memoria privada e handles do monitor por minuto (proxy das tabelas).
param([string]$Exe = "$PSScriptRoot\..\out\cmdwatch.exe", [int]$Minutos = 10)
Add-Type -AssemblyName System.Windows.Forms
$repro = Join-Path $env:TEMP 'cmdw-tests\repro'
$log = Join-Path $env:TEMP 'cmdw-tests\logs\crescimento.jsonl'
if (Test-Path $log) { Remove-Item $log -Force }
$p = New-Object Diagnostics.ProcessStartInfo -ArgumentList $Exe, "monitor --json --log `"$log`""
$p.UseShellExecute = $false; $p.CreateNoWindow = $true; $p.RedirectStandardOutput = $true
$m = [Diagnostics.Process]::Start($p); $null = $m.StandardOutput.ReadToEndAsync()
Start-Sleep 5
$fim = (Get-Date).AddMinutes($Minutos); $prox = Get-Date; $n = 0
while ((Get-Date) -lt $fim) {
  $q = New-Object Diagnostics.ProcessStartInfo -ArgumentList 'cmd.exe', ("/c `"" + (Join-Path $repro 'short.cmd') + "`"")
  $q.UseShellExecute = $false; $q.CreateNoWindow = $true; [Diagnostics.Process]::Start($q) | Out-Null
  $f = New-Object System.Windows.Forms.Form; $f.Text = "CMDW-CHURN-$n"; $f.Width = 120; $f.Height = 40; $f.ShowInTaskbar = $false
  $f.Show(); [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 300; $f.Close(); $f.Dispose()
  $n++; Start-Sleep -Milliseconds 700
  if ((Get-Date) -ge $prox) { $m.Refresh(); "{0:HH:mm:ss} ciclos={1} memPriv={2:N1}MB handles={3} threads={4}" -f (Get-Date), $n, ($m.PrivateMemorySize64/1MB), $m.HandleCount, $m.Threads.Count; $prox = (Get-Date).AddSeconds(60) }
}
$m.Refresh(); "{0:HH:mm:ss} FINAL ciclos={1} memPriv={2:N1}MB handles={3}" -f (Get-Date), $n, ($m.PrivateMemorySize64/1MB), $m.HandleCount
try { $m.Kill() } catch {}