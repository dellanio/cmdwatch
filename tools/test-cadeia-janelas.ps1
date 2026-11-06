# Teste real STA: python pai -> python com janela Tk; auxiliares CREATE_NO_WINDOW.
param([string]$Exe, [string]$Nome='depois')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$base=Join-Path $env:TEMP 'cmdw-tests\cadeia'; New-Item -ItemType Directory -Force $base | Out-Null
$prefix=((Get-Content "$PSScriptRoot\test-limpar-recolher.ps1" -Raw) -split '\$res = New-Object')[0]
$start=$prefix.IndexOf('Add-Type -AssemblyName'); Invoke-Expression $prefix.Substring($start)
$a=[Reflection.Assembly]::LoadFile((Resolve-Path $Exe).Path)
$flags=[Reflection.BindingFlags]'Instance,NonPublic,Public'
function Campo($o,$n){return ,($o.GetType().GetField($n,$flags).GetValue($o))}
$o=[Activator]::CreateInstance($a.GetType('CmdWatch.Options'),$true); $o.LogPath=''; $o.Engine='polling'
$f=[Activator]::CreateInstance($a.GetType('CmdWatch.Gui.DashboardForm'),$flags,$null,[object[]]@($o),$null)
function Pump($ms){$end=(Get-Date).AddMilliseconds($ms); while((Get-Date)-lt $end){[Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 40}}
$marca='cmdw-cadeia-'+[guid]::NewGuid().ToString('N').Substring(0,8)
$filho=Join-Path $base 'filho.py'; $pai=Join-Path $base 'pai.py'
Set-Content $filho @('import tkinter as tk, sys', 'w=tk.Tk(); w.title(sys.argv[1]); w.geometry("300x120"); tk.Label(w,text="Teste da cadeia de origem").pack(); w.after(40000,w.destroy); w.mainloop()') -Encoding ASCII
Set-Content $pai @('import subprocess,sys,pathlib','subprocess.run([sys.executable,str(pathlib.Path(__file__).with_name("filho.py")),sys.argv[1]],creationflags=subprocess.CREATE_NO_WINDOW)') -Encoding ASCII
try {
 $f.Show(); Pump 2500
 $ps=New-Object Diagnostics.ProcessStartInfo 'python.exe',("`"$pai`" $marca"); $ps.UseShellExecute=$false; $ps.CreateNoWindow=$true
 $p=[Diagnostics.Process]::Start($ps)
 $busca=Campo $f '_busca'; $busca.Box.Text=$marca
 $rec=Campo $f '_recolhidos'; $rec.Clear(); $f.GetType().GetMethod('LayoutDashboard',$flags).Invoke($f,@()) | Out-Null
 Pump 9000
 $rows=Campo (Campo $f '_windows') 'Rows'; $r=@($rows | Where-Object {$_.Title -eq $marca}) | Select-Object -First 1
 if(-not $r){throw 'Janela Tk nao encontrada'}
 "autor=$($r.AuthorName) origem=$($r.Origin)" | Set-Content (Join-Path $base "$Nome.txt")
 [PWL]::Shot($f.Handle,(Join-Path $base "$Nome.png"))
 if($Nome -ne 'antes') {
  if($r.Origin -notmatch 'python.* <= .*powershell'){throw "Cadeia incompleta: $($r.Origin)"}
  if($r.OriginTip -notmatch 'pai.py'){throw 'Tooltip sem a linha de comando do pai'}
  $root=($r.Origin -split ' <= ')[-1]; $busca.Box.Text=$root; Pump 1500
  if(-not @((Campo (Campo $f '_windows') 'Rows') | Where-Object {$_.Title -eq $marca}).Count){throw 'Busca por ancestral falhou'}
  "PASSOU: cadeia completa, tooltip com cmdline, busca pelo ancestral remoto ($root)" | Add-Content (Join-Path $base "$Nome.txt")
 }
} finally {
 if($p){$k=New-Object Diagnostics.ProcessStartInfo 'taskkill.exe',("/T /F /PID "+$p.Id); $k.UseShellExecute=$false; $k.CreateNoWindow=$true; [Diagnostics.Process]::Start($k).WaitForExit()}
 $f.Close(); $f.Dispose()
}


