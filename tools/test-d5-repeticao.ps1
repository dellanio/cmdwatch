# Repeticao do cenario D5 (node -> python -> git numa janela nova do Windows Terminal)
# com outros consoles abertos. Mede a taxa de falha do autor da janela.
# Monitor oculto (CreateNoWindow); janelas visiveis: os consoles de fundo e as
# janelas que o cenario abre. So encerra processos que o teste iniciou.
param([string]$Exe = "$PSScriptRoot\..\out\cmdwatch.exe", [int]$N = 20, [int]$Fundo = 3)
$ErrorActionPreference = 'Continue'
$base = Join-Path $env:TEMP 'cmdw-tests'; $repro = Join-Path $base 'repro'; $logs = Join-Path $base 'logs'
New-Item -ItemType Directory -Force $repro, $logs | Out-Null
function Oculto($file, $args_) {
  $p = New-Object Diagnostics.ProcessStartInfo -ArgumentList $file, $args_
  $p.UseShellExecute = $false; $p.CreateNoWindow = $true; return [Diagnostics.Process]::Start($p)
}
@('import subprocess, time','subprocess.run(["git", "--version"], capture_output=True)','time.sleep(0.3)') | Set-Content (Join-Path $repro 'script_pai.py') -Encoding ASCII
@('const {spawn}=require("child_process");','const path=require("path");','const child=spawn("python",[path.join(__dirname,"script_pai.py")],{detached:true,windowsHide:true,stdio:"ignore"});','child.unref();','setTimeout(()=>process.exit(0),500);') | Set-Content (Join-Path $repro 'launch_pai.js') -Encoding ASCII
# consoles de fundo: metade console classico, metade aba do Windows Terminal (marcados para encerrar so eles)
$marca = 'cmdw-fundo-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$bg = @()
for ($i = 0; $i -lt $Fundo; $i++) {
  if ($i % 2 -eq 0) { $bg += Start-Process conhost.exe -ArgumentList 'cmd', '/c', "title $marca & ping -n 900 127.0.0.1 >nul" -PassThru }
  else { Start-Process wt.exe -ArgumentList '-w', 'new', 'cmd', '/c', "title $marca & ping -n 900 127.0.0.1 >nul" }
}
Start-Sleep 3
$log = Join-Path $logs 'test-d5-repeticao.jsonl'; if (Test-Path $log) { Remove-Item $log -Force }
$m = Oculto $Exe ("monitor --log `"$log`" --json"); Start-Sleep 3
$inicios = @()
for ($k = 0; $k -lt $N; $k++) {
  $inicios += Get-Date
  (Oculto 'node.exe' ("`"" + (Join-Path $repro 'launch_pai.js') + "`"")).WaitForExit()
  Start-Sleep -Milliseconds 3500
}
Start-Sleep 2
try { $m.Kill(); $m.WaitForExit(3000) | Out-Null } catch {}
# encerra so os consoles de fundo deste teste (pela marca na linha de comando)
Get-CimInstance Win32_Process -Filter "Name='cmd.exe'" | Where-Object { $_.CommandLine -match $marca } | ForEach-Object {
  Get-CimInstance Win32_Process -Filter "ParentProcessId=$($_.ProcessId)" | Where-Object Name -eq 'PING.EXE' | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -EA 0 }
  Stop-Process -Id $_.ProcessId -Force -EA 0 }
Start-Sleep 1
Get-CimInstance Win32_Process -Filter "Name='WindowsTerminal.exe'" | Where-Object { $_.CommandLine -match $marca } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -EA 0 }
foreach ($p in $bg) { try { if (-not $p.HasExited) { $p.Kill() } } catch {} }
$L = [IO.File]::ReadAllLines($log)
$ev = foreach ($l in $L) { if ($l -match '"type":"window_' -and $l -match 'CASCADIA_HOSTING_WINDOW_CLASS|ConsoleWindowClass') { $o = $l | ConvertFrom-Json; $o } }
$falhas = 0; $det = @()
for ($k = 0; $k -lt $N; $k++) {
  $t0 = $inicios[$k]; $t1 = $t0.AddMilliseconds(3400)
  $daqui = @($ev | Where-Object { $_.window.title -notmatch $marca -and $_.window.firstSeen -and ([datetime]$_.window.firstSeen -ge $t0) -and ([datetime]$_.window.firstSeen -lt $t1) })
  $autores = @($daqui | ForEach-Object { $_.window.author } | Select-Object -Unique)
  $ok = ($autores.Count -gt 0) -and -not ($autores | Where-Object { $_ -ne 'python.exe' })
  if (-not $ok) { $falhas++ }
  $det += ("{0,2}: {1} autores={2}" -f ($k + 1), $(if ($ok) { 'ok' } else { 'FALHA' }), ($autores -join ','))
}
$det
"D5-repeticao falhas: $falhas/$N (consoles de fundo: $Fundo)"
if ($falhas -gt 0) { exit 1 } else { exit 0 }