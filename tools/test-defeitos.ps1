# Testes repetiveis dos defeitos da revisao 1.1.0 (D1-D5, ETW sem admin, abas, mascara, kill).
# Monitores e auxiliares rodam ocultos (CreateNoWindow). Unicas janelas
# visiveis: a do teste D3 (formulario pequeno) e a aba do Windows Terminal
# do D1/D5, que o proprio cenario exige. So encerra processos que iniciou.
param([string]$Exe = "$PSScriptRoot\..\out\cmdwatch.exe")
$ErrorActionPreference = 'Continue'
$base = Join-Path $env:TEMP 'cmdw-tests'; $repro = Join-Path $base 'repro'; $logs = Join-Path $base 'logs'
New-Item -ItemType Directory -Force $repro, $logs | Out-Null
$res = New-Object System.Collections.Generic.List[string]
function Ok($id, $cond, $info) { $script:res.Add(("{0} {1} {2}" -f $id, $(if ($cond) {'PASSOU'} else {'FALHOU'}), $info)) }
function Oculto($file, $args_) {
  $p = New-Object Diagnostics.ProcessStartInfo -ArgumentList $file, $args_
  $p.UseShellExecute = $false; $p.CreateNoWindow = $true
  return [Diagnostics.Process]::Start($p)
}
function Monitor($log, $extra) {
  if (Test-Path $log) { Remove-Item $log -Force }
  $m = Oculto $Exe ("monitor --log `"$log`" --json " + $extra)
  Start-Sleep -Seconds 3; return $m
}
function Parar($m) { try { if (-not $m.HasExited) { $m.Kill(); $m.WaitForExit(3000) | Out-Null } } catch {} ; Start-Sleep -Milliseconds 300 }
function Linhas($log) { if (Test-Path $log) { [IO.File]::ReadAllLines($log) } else { @() } }
Set-Content (Join-Path $repro 'short.cmd') "@echo off`r`nexit /b 0" -Encoding ASCII
@('import subprocess, time','subprocess.run(["git", "--version"], capture_output=True)','time.sleep(0.3)') | Set-Content (Join-Path $repro 'script_pai.py') -Encoding ASCII
# o node com detached:true abre um console novo (o cenario do campo): a janela e esperada
@('const {spawn}=require("child_process");','const path=require("path");','const child=spawn("python",[path.join(__dirname,"script_pai.py")],{detached:true,windowsHide:true,stdio:"ignore"});','child.unref();','setTimeout(()=>process.exit(0),500);') | Set-Content (Join-Path $repro 'launch_pai.js') -Encoding ASCII

# ---- D1 / D5 / D4: autor e cadeia da janela aberta por script curto
$log = Join-Path $logs 'test-d1d5.jsonl'
$m = Monitor $log ''
(Oculto 'node.exe' ("`"" + (Join-Path $repro 'launch_pai.js') + "`"")).WaitForExit()
Start-Sleep -Seconds 5; Parar $m
$jan = Linhas $log | Where-Object { $_ -match '"type":"window_\w+"' -and $_ -match 'script_pai' }
$cas = $jan | Where-Object { $_ -match 'CASCADIA_HOSTING_WINDOW_CLASS|ConsoleWindowClass' }
Ok 'D1' ($jan.Count -gt 0) ("janelas com script_pai na cadeia/autor: " + $jan.Count)
$autores = $cas | ForEach-Object { if ($_ -match '"author":"([^"]+)"') { $Matches[1] } } | Select-Object -Unique
Ok 'D5' (($autores -contains 'python.exe') -and -not ($autores | Where-Object { $_ -match 'git|curl' })) ("autores da janela visivel: " + ($autores -join ','))
$sids = Linhas $log | ForEach-Object { if ($_ -match '"sessionId":(\d+)') { $Matches[1] } } | Select-Object -Unique
Ok 'D4' (($sids | Where-Object { $_ -ne '0' }).Count -gt 0) ("sessionIds: " + ($sids -join ','))

# ---- D3: janela escondida nao vira fechada
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Name W -Namespace CmdwT -MemberDefinition '[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);'
$log = Join-Path $logs 'test-d3.jsonl'
$m = Monitor $log ''
$f = New-Object System.Windows.Forms.Form; $f.Text = 'CMDW-HIDE-TEST'; $f.Width = 200; $f.Height = 80; $f.Show(); [System.Windows.Forms.Application]::DoEvents()
Start-Sleep 2; [CmdwT.W]::ShowWindow($f.Handle, 0) | Out-Null
Start-Sleep 6; [CmdwT.W]::ShowWindow($f.Handle, 5) | Out-Null; [System.Windows.Forms.Application]::DoEvents()
Start-Sleep 3; $tFecha = Get-Date; $f.Close(); $f.Dispose(); Start-Sleep 2; Parar $m
$ev = Linhas $log | Where-Object { $_ -match 'CMDW-HIDE-TEST' } | ForEach-Object { if ($_ -match '"ts":"([^"]+)","type":"window_(\w+)"') { [pscustomobject]@{ t = [datetime]$Matches[1]; e = $Matches[2] } } }
$closeCedo = $ev | Where-Object { $_.e -eq 'close' -and $_.t -lt $tFecha.AddMilliseconds(-200) }
$opens = @($ev | Where-Object { $_.e -eq 'open' }).Count
$hides = @($ev | Where-Object { $_.e -eq 'hide' }).Count
Ok 'D3' (($hides -ge 1) -and (@($closeCedo).Count -eq 0) -and ($opens -eq 1)) ("eventos: " + (($ev | ForEach-Object { $_.t.ToString('ss.fff') + ' ' + $_.e }) -join ' | '))

# ---- D2: .cmd curto e oculto com a maquina parada
foreach ($cfg in @(@{n='padrao'; a=''; min=-1}, @{n='interval50'; a='--interval 50 --no-windows'; min=7})) {
  $log = Join-Path $logs ("test-d2-" + $cfg.n + '.jsonl')
  $m = Monitor $log $cfg.a; Start-Sleep 9
  for ($i = 0; $i -lt 8; $i++) { (Oculto 'cmd.exe' ("/c `"" + (Join-Path $repro 'short.cmd') + "`"")).WaitForExit(); Start-Sleep 2 }
  Start-Sleep 2; Parar $m
  $n = @(Linhas $log | Where-Object { $_ -match '"type":"batch_open"' -and $_ -match 'short\.cmd' }).Count
  $res.Add("D2 INFO " + $cfg.n + " capturou $n/8 (polling sem admin: limitacao documentada; captura garantida so com --engine etw, ver test-etw.ps1)")
}
# ---- ETW sem admin: o monitor recebe um EtwEngine (nao iniciado) e deve chegar ao Start dele
# sem InvalidCastException (defeito do RunMonitor achado no teste elevado). O Start falha com o
# erro do StartTrace por falta de permissao, que e o esperado aqui.
$p = New-Object Diagnostics.ProcessStartInfo -ArgumentList $Exe, "monitor --engine etw --duration 3"
$p.UseShellExecute = $false; $p.CreateNoWindow = $true; $p.RedirectStandardError = $true; $p.RedirectStandardOutput = $true
$oem = [Text.Encoding]::GetEncoding([Globalization.CultureInfo]::CurrentCulture.TextInfo.OEMCodePage)
$p.StandardErrorEncoding = $oem; $p.StandardOutputEncoding = $oem
$p.EnvironmentVariables['CMDWATCH_TESTE_ETW_SEM_ADMIN'] = '1'
$e = [Diagnostics.Process]::Start($p); $null = $e.StandardOutput.ReadToEndAsync(); $se = $e.StandardError.ReadToEndAsync()
if (-not $e.WaitForExit(15000)) { try { $e.Kill() } catch {} }
$se = $se.Result
Ok 'ETW-caminho' (($se -notmatch 'InvalidCast') -and ($se -match 'StartTrace')) ("stderr: " + (($se -split "`n")[0]).Trim())
# ---- D5-abas: duas abas do Windows Terminal abertas juntas (menos de 2 s) nao trocam de autor.
# Cada python (pid conhecido) chama ping, que ganha console proprio: a aba visivel e esperada.
@('import subprocess, sys','subprocess.run(["ping", "-n", "3", sys.argv[1]], capture_output=True)') | Set-Content (Join-Path $repro 'script_aba.py') -Encoding ASCII
@('const {spawn}=require("child_process");','const path=require("path");','const fs=require("fs");','const s=path.join(__dirname,"script_aba.py");',
  'const a=spawn("python",[s,"127.0.0.1"],{detached:true,windowsHide:true,stdio:"ignore"});','const b=spawn("python",[s,"127.0.0.2"],{detached:true,windowsHide:true,stdio:"ignore"});',
  'fs.writeFileSync(path.join(__dirname,"abas.pids"),a.pid+" "+b.pid);','a.unref();b.unref();','setTimeout(()=>process.exit(0),500);') | Set-Content (Join-Path $repro 'launch_abas.js') -Encoding ASCII
$log = Join-Path $logs 'test-d5-abas.jsonl'
$m = Monitor $log ''
(Oculto 'node.exe' ("`"" + (Join-Path $repro 'launch_abas.js') + "`"")).WaitForExit()
Start-Sleep -Seconds 6; Parar $m
$pids = (Get-Content (Join-Path $repro 'abas.pids')) -split ' '
$porJanela = @{}
Linhas $log | Where-Object { $_ -match '"type":"window_\w+"' -and $_ -match 'CASCADIA_HOSTING_WINDOW_CLASS|ConsoleWindowClass' } | ForEach-Object {
  if ($_ -match '"hwnd":"(0x[0-9A-F]+)"' ) { $h = $Matches[1]; if ($_ -match '"authorPid":(\d+)') { $a = $Matches[1]; if ($pids -contains $a) { if (-not $porJanela[$h]) { $porJanela[$h] = New-Object System.Collections.Generic.List[string] }; $porJanela[$h].Add($a) } } } }
$finais = @($porJanela.Keys | ForEach-Object { $porJanela[$_][$porJanela[$_].Count - 1] } | Select-Object -Unique)
$trocas = @($porJanela.Keys | Where-Object { @($porJanela[$_] | Select-Object -Unique).Count -gt 1 })
Ok 'D5-abas' (($porJanela.Count -ge 2) -and ($finais.Count -ge 2) -and ($trocas.Count -eq 0)) ("abas com autor python: " + $porJanela.Count + ", autores distintos: " + $finais.Count + ", abas que trocaram de autor: " + $trocas.Count + " (pids python " + ($pids -join ',') + ")")

# ---- MASCARA: segredos provaveis nas linhas de comando (unidade + log real)
$asm = [Reflection.Assembly]::LoadFile((Resolve-Path $Exe).Path)
$masc = $asm.GetType('CmdWatch.Mascara').GetMethod('Aplicar')
# valores falsos gerados na hora: nenhum segredo literal no codigo-fonte (scanners como o GitGuardian)
function Falso($n, $abc = 'abcdefghjkmnpqrstuvwxyz23456789') { -join (1..$n | ForEach-Object { $abc[(Get-Random -Maximum $abc.Length)] }) }
$pw = 'fx' + (Falso 8); $tk = 'tk' + (Falso 10); $jwt = 'ey' + 'J' + (Falso 20); $sq = 'sq' + (Falso 8); $my = 'my' + (Falso 6)
$aws = 'AK' + 'IA' + (Falso 16 'ABCDEFGHJKLMNPQRSTUVWXYZ234567')
$casos = @(
  @("app.exe --password $pw -v", 'app.exe --password *** -v'),
  @("app.exe --token=$tk --api-key `"x y z`"", 'app.exe --token=*** --api-key ***'),
  @(("sqlcmd `"Server=db;User Id=sa;Pass" + "word=$sq;`""), 'sqlcmd "Server=db;User Id=sa;Password=***;"'),
  @(("curl -H `"Authorization: Bear" + "er $jwt`" https://u:" + $pw + "@h.com/x"), 'curl -H "Authorization: Bearer ***" https://u:***@h.com/x'),
  @("mysql.exe -u root -p$my db", 'mysql.exe -u root -p*** db'),
  @("aws s3 ls --profile x $aws", 'aws s3 ls --profile x AKIA***'),
  @('ping -n 3 127.0.0.1 & ssh -p 22 host & echo basic configuration', 'ping -n 3 127.0.0.1 & ssh -p 22 host & echo basic configuration'))
$falhas = @($casos | Where-Object { $masc.Invoke($null, @($_[0])) -ne $_[1] } | ForEach-Object { $_[0] + ' => ' + $masc.Invoke($null, @($_[0])) })
$log = Join-Path $logs 'test-mascara.jsonl'
$m = Monitor $log '--no-windows --interval 100'
(Oculto 'cmd.exe' ("/c `"`"" + (Join-Path $repro 'segredos.cmd') + "`" --password $pw token=$tk`"")).WaitForExit()
Start-Sleep 2; Parar $m
$vazou = @(Linhas $log | Where-Object { $_.Contains($pw) -or $_.Contains($tk) }).Count
$mascarou = @(Linhas $log | Where-Object { $_ -match 'segredos\.cmd' -and $_ -match '--password \*\*\* token=\*\*\*' }).Count
Ok 'MASCARA' (($falhas.Count -eq 0) -and ($vazou -eq 0) -and ($mascarou -gt 0)) ("casos de unidade com erro: " + $falhas.Count + " " + ($falhas -join ' | ') + "; linhas do log com segredo: $vazou, mascaradas: $mascarou")

# ---- KILL: registro velho (pid reaproveitado simulado) nao encerra; registro certo encerra
$pc = $asm.GetType('CmdWatch.ProcessControl')
$m3 = $pc.GetMethods() | Where-Object { $_.Name -eq 'Terminate' -and $_.GetParameters().Count -eq 3 }
$alvo = Oculto 'ping.exe' '-n 30 127.0.0.1'; Start-Sleep 1
$r1 = $m3.Invoke($null, @($alvo.Id, $true, $alvo.StartTime.AddSeconds(-10))); Start-Sleep -Milliseconds 300; $alvo.Refresh(); $vivo1 = -not $alvo.HasExited
$r2 = $m3.Invoke($null, @($alvo.Id, $true, $alvo.StartTime)); Start-Sleep -Milliseconds 300; $alvo.Refresh(); $vivo2 = -not $alvo.HasExited
if ($vivo2) { try { $alvo.Kill() } catch {} }
Ok 'KILL' ($vivo1 -and -not $r1.Ok -and $r2.Ok -and -not $vivo2) ("registro velho: Ok=$($r1.Ok) vivo=$vivo1 ('$($r1.Error)'); registro certo: Ok=$($r2.Ok) vivo=$vivo2")

$res | ForEach-Object { Write-Output $_ }
if ($res | Where-Object { $_ -match ' FALHOU ' }) { exit 1 } else { exit 0 }