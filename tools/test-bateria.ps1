# Bateria de 11 formas de chamar .cmd (runner2.cmd). Monitor oculto; o runner
# roda oculto. Unica janela visivel: a forma [10] "start", que abre console por natureza.
param([string]$Exe = "$PSScriptRoot\..\out\cmdwatch.exe", [string]$Extra = '', [switch]$RunnerVisivel)
# -RunnerVisivel: mesmo regime da linha de base (runner em console minimizado)
$test = "$PSScriptRoot\..\.build\calltest"
$log = Join-Path $env:TEMP ("cmdw-tests\logs\bateria-" + (Get-Date -Format HHmmss) + ".jsonl")
function Oculto($file, $a, $wd) { $p = New-Object Diagnostics.ProcessStartInfo -ArgumentList $file, $a; $p.UseShellExecute = $false; $p.CreateNoWindow = $true; if ($wd) { $p.WorkingDirectory = $wd }; [Diagnostics.Process]::Start($p) }
$m = Oculto $Exe ("monitor --json --log `"$log`" " + $Extra)
Start-Sleep 12
$t0 = Get-Date
if ($RunnerVisivel) { $r = Start-Process cmd.exe -ArgumentList '/c','runner2.cmd' -WorkingDirectory (Resolve-Path $test).Path -WindowStyle Minimized -PassThru; $r.WaitForExit(120000) | Out-Null }
else { (Oculto 'cmd.exe' '/c runner2.cmd' (Resolve-Path $test).Path).WaitForExit(120000) | Out-Null }
Start-Sleep 4
try { $m.Kill() } catch {}
Start-Sleep -Milliseconds 400
$ev = [IO.File]::ReadAllLines($log) | Where-Object { $_ -match '"type":"batch_open"' -and $_ -match 'calltest' }
$nomes = $ev | ForEach-Object { if ($_ -match '"scriptName":"([^"]+)"') { $Matches[1] } }
$nomes | Group-Object | ForEach-Object { "{0,-22} {1}" -f $_.Name, $_.Count }
"TOTAL batch_open em calltest: " + @($ev).Count + "  (linha de base: 12 = 12)"
"batch_call (passos do runner2): " + @([IO.File]::ReadAllLines($log) | Where-Object { $_ -match '"type":"batch_call"' }).Count + "  (linha de base v1.0.10: 12)"
"LOG=$log"