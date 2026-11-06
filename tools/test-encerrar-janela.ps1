# Encerrar autor de Janelas detectadas: menu + dialogo + kill seguro.
param([string]$Exe = "$env:TEMP\cmdw-tests\out-encerrar\cmdwatch.exe",
      [string]$Shots = "$env:TEMP\cmdw-tests\encerrar-shots",
      [int]$HeaderH = 26, [int]$RowH = 24)
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force $Shots | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
if (-not ('PW' -as [type])) { Add-Type -ReferencedAssemblies System.Drawing @"
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices; using System.Text; using System.Collections.Generic;
public static class PW {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  delegate bool EnumProc(IntPtr h, IntPtr l);
  public static void Shot(IntPtr h, string path) {
    RECT w; GetWindowRect(h, out w); RECT e;
    if (DwmGetWindowAttribute(h, 9, out e, Marshal.SizeOf(typeof(RECT))) != 0) e = w;
    using (Bitmap b = new Bitmap(w.R-w.L, w.B-w.T)) {
      using (Graphics g = Graphics.FromImage(b)) { IntPtr dc=g.GetHdc(); PrintWindow(h,dc,2); g.ReleaseHdc(dc); }
      Rectangle c=new Rectangle(e.L-w.L,e.T-w.T,e.R-e.L,e.B-e.T);
      using (Bitmap o=b.Clone(c,b.PixelFormat)) o.Save(path, ImageFormat.Png);
    }
  }
  public static string Texto(IntPtr h){ var sb=new StringBuilder(256); GetWindowText(h,sb,256); return sb.ToString(); }
  public static IntPtr Filho(IntPtr pai, string texto){
    IntPtr achou=IntPtr.Zero;
    EnumChildWindows(pai, delegate(IntPtr h, IntPtr l){ if(Texto(h)==texto){achou=h;return false;} return true;}, IntPtr.Zero);
    return achou;
  }
  public static IntPtr[] Topo(int pid, IntPtr principal){
    var l=new List<IntPtr>();
    EnumWindows(delegate(IntPtr h, IntPtr x){
      uint p; GetWindowThreadProcessId(h,out p);
      if(p==(uint)pid && h!=principal && IsWindowVisible(h)){
        var sb=new StringBuilder(128); GetClassName(h,sb,128);
        if(sb.ToString().StartsWith("WindowsForms10")) l.Add(h);
      } return true; }, IntPtr.Zero);
    return l.ToArray();
  }
  public static void Clique(IntPtr h, uint down, uint up, int x, int y, int mk){
    IntPtr lp=(IntPtr)((y<<16)|(x&0xFFFF));
    PostMessage(h,down,(IntPtr)mk,lp); PostMessage(h,up,IntPtr.Zero,lp);
  }
}
"@ }
$res=New-Object System.Collections.Generic.List[string]
function Ok($id,$cond,$info){ $script:res.Add(("{0} {1} {2}" -f $id,$(if($cond){'PASSOU'}else{'FALHOU'}),$info)) }

$cfg=Join-Path $env:APPDATA 'cmdwatch\ui.cfg'
$cfgAntes=if(Test-Path $cfg){[IO.File]::ReadAllText($cfg)}else{$null}
if($cfgAntes -ne $null){
  $linhas=@($cfgAntes -split "`r?`n" | Where-Object { $_ -and $_ -notmatch '^(recolhidos|lang)=' }) + 'lang=pt-br'
  [IO.File]::WriteAllText($cfg, ($linhas -join "`r`n"))
}

$base=Join-Path $env:TEMP 'cmdw-tests\encerrar'; New-Item -ItemType Directory -Force $base | Out-Null
$marca='cmdw-encerrar-'+[guid]::NewGuid().ToString('N').Substring(0,8)
$py=Join-Path $base 'janela.py'
Set-Content $py "import tkinter as tk, sys`nw=tk.Tk(); w.title(sys.argv[1]); w.geometry('280x100'); tk.Label(w,text='alvo Encerrar').pack(); w.after(90000,w.destroy); w.mainloop()" -Encoding ASCII

$log=Join-Path $base 'encerrar.jsonl'; if(Test-Path $log){Remove-Item $log -Force}
$g=$null; $pyProc=$null
try {
  $g=Start-Process $Exe -ArgumentList 'gui','--log',"`"$log`"" -PassThru
  for($i=0;$i -lt 40 -and $g.MainWindowHandle -eq 0;$i++){ Start-Sleep -Milliseconds 250; $g.Refresh() }
  Start-Sleep 3
  $AE=[Windows.Automation.AutomationElement]
  $root=$AE::FromHandle($g.MainWindowHandle)
  $edits=$root.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [Windows.Automation.ControlType]::Edit)))
  if($edits.Count -gt 0){ $edits[0].GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($marca) }
  Start-Sleep 1

  $psi=New-Object Diagnostics.ProcessStartInfo 'python.exe',("`"$py`" $marca")
  $psi.UseShellExecute=$false; $psi.CreateNoWindow=$true
  $pyProc=[Diagnostics.Process]::Start($psi)

  $ok=$false
  for($i=0;$i -lt 40;$i++){
    Start-Sleep -Milliseconds 500
    if((Test-Path $log) -and (Select-String -Path $log -Pattern $marca -SimpleMatch -Quiet)){ $ok=$true; break }
  }
  Ok 'E0' $ok "janela no log ($marca)"
  Start-Sleep 2
  [PW]::Shot($g.MainWindowHandle, (Join-Path $Shots 'encerrar-janela-antes.png'))

  $tab=[PW]::Filho($g.MainWindowHandle,'tabelaJanelas')
  Ok 'E0b' ($tab -ne [IntPtr]::Zero) "tabelaJanelas=$tab"
  [PW]::SetForegroundWindow($g.MainWindowHandle) | Out-Null
  Start-Sleep -Milliseconds 400
  [PW]::Clique($tab, 0x204, 0x205, 40, $HeaderH + [int]($RowH/2), 2)
  Start-Sleep -Milliseconds 800
  $menus=@([PW]::Topo($g.Id, $g.MainWindowHandle))
  Ok 'E1' ($menus.Count -gt 0) "menu=$($menus.Count)"
  if($menus.Count -eq 0){ throw 'menu nao abriu' }
  [PW]::Shot($menus[0], (Join-Path $Shots 'encerrar-janela-menu.png'))
  $r=New-Object PW+RECT; [PW]::GetWindowRect($menus[0],[ref]$r)|Out-Null
  [PW]::Clique($menus[0], 0x201, 0x202, [int](($r.R-$r.L)/2), ($r.B-$r.T)-15, 1)
  Start-Sleep -Milliseconds 900
  $dlgs=@([PW]::Topo($g.Id, $g.MainWindowHandle))
  Ok 'E2' ($dlgs.Count -gt 0) "dialogo=$($dlgs.Count)"
  if($dlgs.Count -eq 0){ throw 'dialogo nao abriu' }
  [PW]::Shot($dlgs[0], (Join-Path $Shots 'encerrar-janela-dialogo.png'))
  $titulo=([Windows.Automation.AutomationElement]::FromHandle($dlgs[0])).Current.Name; if(-not $titulo){ $titulo=[PW]::Texto($dlgs[0]) }
  Ok 'E3' ((Test-Path (Join-Path $Shots 'encerrar-janela-dialogo.png')) -and ((Get-Item (Join-Path $Shots 'encerrar-janela-dialogo.png')).Length -gt 1000) -and $dlgs.Count -gt 0) ('dialogo capturado; uia/nome=''' + $titulo + '''')
  [PW]::PostMessage($dlgs[0], 0x100, [IntPtr]0x0D, [IntPtr]::Zero) | Out-Null
  Start-Sleep 2

  $pyProc.Refresh()
  Ok 'E4' ($pyProc.HasExited) "python HasExited=$($pyProc.HasExited)"
  $fechou=$false
  if(Test-Path $log){ $fechou = [bool](Select-String -Path $log -Pattern 'window_close' -Quiet) }
  Ok 'E5' ($pyProc.HasExited -or $fechou) "window_close no log=$fechou"
  [PW]::Shot($g.MainWindowHandle, (Join-Path $Shots 'encerrar-janela-depois.png'))
} finally {
  if($g -and -not $g.HasExited){ try{ $g.CloseMainWindow()|Out-Null; if(-not $g.WaitForExit(4000)){ $g.Kill() } }catch{ try{$g.Kill()}catch{} } }
  if($pyProc -and -not $pyProc.HasExited){
    $k=New-Object Diagnostics.ProcessStartInfo 'taskkill.exe',("/T /F /PID $($pyProc.Id)")
    $k.UseShellExecute=$false; $k.CreateNoWindow=$true
    try{ [Diagnostics.Process]::Start($k).WaitForExit() }catch{}
  }
  if($cfgAntes -ne $null){ [IO.File]::WriteAllText($cfg,$cfgAntes) }
}
$res
if(@($res|Where-Object {$_ -match 'FALHOU'}).Count){ exit 1 }
