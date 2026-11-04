# Rajada sintetica de janelas: N formularios pequenos abertos em sequencia e
# fechados. Conta open/close perdidos e o atraso do evento. Janelas sao do teste.
param([string]$Exe = "$PSScriptRoot\..\out\cmdwatch.exe", [int]$N = 100)
Add-Type -AssemblyName System.Windows.Forms
$log = Join-Path $env:TEMP 'cmdw-tests\logs\burst-janelas.jsonl'
if (Test-Path $log) { Remove-Item $log -Force }
$p = New-Object Diagnostics.ProcessStartInfo -ArgumentList $Exe, "monitor --json --log `"$log`""
$p.UseShellExecute = $false; $p.CreateNoWindow = $true; $m = [Diagnostics.Process]::Start($p)
Start-Sleep 4
$criadas = @{}; $forms = @()
for ($i = 0; $i -lt $N; $i++) {
  $f = New-Object System.Windows.Forms.Form; $f.Text = "CMDW-BURST-$i"; $f.Width = 120; $f.Height = 40
  $f.StartPosition = 'Manual'; $f.Left = 10 + ($i % 10) * 15; $f.Top = 10 + [int]($i / 10) * 15; $f.ShowInTaskbar = $false
  $f.Show(); $criadas["CMDW-BURST-$i"] = Get-Date; $forms += $f
}
[System.Windows.Forms.Application]::DoEvents(); Start-Sleep 3
$fechadas = Get-Date
foreach ($f in $forms) { $f.Close(); $f.Dispose() }
[System.Windows.Forms.Application]::DoEvents(); Start-Sleep 4
try { $m.Kill() } catch {}; Start-Sleep -Milliseconds 400
$open = @{}; $close = @{}
foreach ($l in [IO.File]::ReadAllLines($log)) {
  if ($l -match '"ts":"([^"]+)","type":"window_(open|close)".*"title":"(CMDW-BURST-\d+)"') {
    if ($Matches[2] -eq 'open') { if (-not $open.ContainsKey($Matches[3])) { $open[$Matches[3]] = [datetime]$Matches[1] } } else { $close[$Matches[3]] = 1 }
  }
}
$atrasos = $open.Keys | ForEach-Object { ($open[$_] - $criadas[$_]).TotalMilliseconds } | Sort-Object
"janelas: $N  open registrados: $($open.Count)  close registrados: $($close.Count)"
if ($atrasos) { "atraso do open (ms): mediana {0:N0}  p95 {1:N0}  max {2:N0}" -f $atrasos[[int]($atrasos.Count/2)], $atrasos[[int]($atrasos.Count*0.95)-1], $atrasos[-1] }