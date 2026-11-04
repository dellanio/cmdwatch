# Teste do motor ETW (precisa de PowerShell como ADMINISTRADOR).
# 8 .cmd curtos (~25 ms) e ocultos com a maquina parada; conta quantos saem
# como batch_open com linha de comando e quem chamou. Tudo oculto.
param([string]$Exe = "$PSScriptRoot\..\out\cmdwatch.exe")
$adm = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $adm) { Write-Output 'NAO VALIDADO: rode este script em PowerShell como administrador.'; exit 2 }
$repro = Join-Path $env:TEMP 'cmdw-tests\repro'; New-Item -ItemType Directory -Force $repro | Out-Null
Set-Content (Join-Path $repro 'short.cmd') "@echo off`r`nexit /b 0" -Encoding ASCII
$log = Join-Path $env:TEMP 'cmdw-tests\logs\test-etw.jsonl'; if (Test-Path $log) { Remove-Item $log -Force }
$p = New-Object Diagnostics.ProcessStartInfo -ArgumentList $Exe, "monitor --engine etw --json --log `"$log`""
$p.UseShellExecute = $false; $p.CreateNoWindow = $true; $p.RedirectStandardOutput = $true; $p.RedirectStandardError = $true
$oem = [Text.Encoding]::GetEncoding([Globalization.CultureInfo]::CurrentCulture.TextInfo.OEMCodePage); $p.StandardErrorEncoding = $oem; $p.StandardOutputEncoding = $oem
$m = [Diagnostics.Process]::Start($p); $null = $m.StandardOutput.ReadToEndAsync(); $err = $m.StandardError.ReadToEndAsync()
Start-Sleep 12
$c0 = $m.TotalProcessorTime.TotalMilliseconds; $t0 = Get-Date
for ($i = 0; $i -lt 8; $i++) {
  $q = New-Object Diagnostics.ProcessStartInfo -ArgumentList 'cmd.exe', ("/c `"" + (Join-Path $repro 'short.cmd') + "`"")
  $q.UseShellExecute = $false; $q.CreateNoWindow = $true; [Diagnostics.Process]::Start($q).WaitForExit(); Start-Sleep 2 }
Start-Sleep 3
$m.Refresh(); $cpu = ($m.TotalProcessorTime.TotalMilliseconds - $c0) / ((Get-Date) - $t0).TotalMilliseconds * 100
try { $m.Kill() } catch {}; Start-Sleep -Milliseconds 500
$linhas = [IO.File]::ReadAllLines($log)
$motor = ($linhas | Select-String '"engine":"(\w+)"' | Select-Object -First 1)
$ok = @($linhas | Where-Object { $_ -match '"type":"batch_open"' -and $_ -match 'short\.cmd' -and $_ -match '"caller":\{"pid":\d+' })
"stderr: " + $err.Result.Trim()
"capturados com linha de comando e chamador: $($ok.Count)/8   CPU: {0:N1}% de 1 nucleo" -f $cpu
if ($ok.Count -ge 8) { 'PASSOU'; exit 0 } else { 'FALHOU'; exit 1 }