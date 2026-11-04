# Captura as imagens do README com dados controlados (.build\exemplo) e busca "exemplo":
# so as janelas/sessoes da demonstracao aparecem (nada de titulos de outros apps).
# Janelas visiveis: o painel e o console da demonstracao. Nada e encerrado alem do que o script abriu.
param([string]$Exe = "$PSScriptRoot\..\out\cmdwatch.exe", [string]$Dest = "$PSScriptRoot\..\docs",
      [int]$HeaderH = 26, [int]$RowH = 24)
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
if (-not ('PW' -as [type])) { Add-Type -ReferencedAssemblies System.Drawing @"
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class PW {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
  public static void Shot(IntPtr h, string path) {
    RECT w; GetWindowRect(h, out w); RECT e;
    if (DwmGetWindowAttribute(h, 9, out e, Marshal.SizeOf(typeof(RECT))) != 0) e = w;
    using (Bitmap b = new Bitmap(w.R - w.L, w.B - w.T)) {
      using (Graphics g = Graphics.FromImage(b)) { IntPtr dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); }
      Rectangle c = new Rectangle(e.L - w.L, e.T - w.T, e.R - e.L, e.B - e.T);
      using (Bitmap o = b.Clone(c, b.PixelFormat)) o.Save(path, ImageFormat.Png);
    }
  }
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
  public static IntPtr Filho(IntPtr pai, string texto) {
    IntPtr achou = IntPtr.Zero;
    EnumChildWindows(pai, delegate (IntPtr h, IntPtr l) {
      var sb = new System.Text.StringBuilder(64); GetWindowText(h, sb, 64);
      if (sb.ToString() == texto) { achou = h; return false; } return true; }, IntPtr.Zero);
    return achou;
  }
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
  // janelas de topo visiveis do processo (classe WinForms), exceto a principal
  public static IntPtr[] Topo(int pid, IntPtr principal) {
    var l = new System.Collections.Generic.List<IntPtr>();
    EnumWindows(delegate (IntPtr h, IntPtr x) {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && h != principal && IsWindowVisible(h)) {
        var sb = new System.Text.StringBuilder(128); GetClassName(h, sb, 128);
        if (sb.ToString().StartsWith("WindowsForms10")) l.Add(h); }
      return true; }, IntPtr.Zero);
    return l.ToArray();
  }
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, string l);
  // caixa de texto mais alta (a busca do cabecalho) recebe o texto
  public static bool Buscar(IntPtr pai, string texto) {
    IntPtr melhor = IntPtr.Zero; int my = int.MaxValue;
    EnumChildWindows(pai, delegate (IntPtr h, IntPtr l) {
      var sb = new System.Text.StringBuilder(128); GetClassName(h, sb, 128);
      if (sb.ToString().ToUpper().Contains(".EDIT.")) { RECT r; GetWindowRect(h, out r); if (r.T < my) { my = r.T; melhor = h; } }
      return true; }, IntPtr.Zero);
    if (melhor == IntPtr.Zero) return false;
    SendMessage(melhor, 0x000C, IntPtr.Zero, texto); return true;
  }
  public static void Clique(IntPtr h, uint down, uint up, int x, int y, int mk) {
    IntPtr lp = (IntPtr)((y << 16) | (x & 0xFFFF));
    PostMessage(h, down, (IntPtr)mk, lp); PostMessage(h, up, IntPtr.Zero, lp);
  }
}
"@ }
$AE = [Windows.Automation.AutomationElement]; $TS = [Windows.Automation.TreeScope]
$AE = [Windows.Automation.AutomationElement]; $TS = [Windows.Automation.TreeScope]
Remove-Item Env:CMDWATCH_TEMA, Env:CMDWATCH_TESTE_PID_VELHO -EA 0
# scripts de exemplo em .build\exemplo (fora do git): dois .cmd longos, um visivel e um oculto
$demo = Join-Path (Resolve-Path "$PSScriptRoot\..").Path '.build\exemplo'
New-Item -ItemType Directory -Force $demo | Out-Null
Set-Content (Join-Path $demo 'exemplo-backup.cmd') "@echo off`r`ntitle exemplo - backup noturno`r`necho copiando relatorios`r`nwhere git`r`nping -n 3 127.0.0.1 >nul`r`ndir /b`r`nping -n 60 127.0.0.1 >nul" -Encoding ASCII
Set-Content (Join-Path $demo 'exemplo-sincroniza.cmd') "@echo off`r`necho sincronizando`r`nwhere cmd`r`nping -n 2 127.0.0.1 >nul`r`ngit --version`r`nping -n 60 127.0.0.1 >nul" -Encoding ASCII
$g = Start-Process $Exe -ArgumentList 'gui', '--quiet' -PassThru
for ($i = 0; $i -lt 40 -and $g.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $g.Refresh() }
Start-Sleep 3
$h = $g.MainWindowHandle
# demonstracao num console visivel e outra oculta (duas sessoes reais)
$psi = New-Object Diagnostics.ProcessStartInfo 'cmd.exe', "/c `"$demo\exemplo-sincroniza.cmd`""
$psi.UseShellExecute = $false; $psi.CreateNoWindow = $true; $psi.WorkingDirectory = $demo
$o = [Diagnostics.Process]::Start($psi)
Start-Sleep 2
$v = Start-Process cmd.exe -ArgumentList '/c', "`"$demo\exemplo-backup.cmd`"" -WorkingDirectory $demo -PassThru
Start-Sleep 9
# busca "exemplo" na barra superior: so as sessoes e janelas da demonstracao
if (-not [PW]::Buscar($h, 'exemplo')) { 'busca nao encontrada' }
Start-Sleep 2
# clique na primeira sessao: o fluxo mostra so ela
$t = [PW]::Filho($h, 'tabelaSessoes')
[PW]::SetForegroundWindow($h) | Out-Null; Start-Sleep -Milliseconds 400
[PW]::Clique($t, 0x201, 0x202, 30, $HeaderH + [int]($RowH / 2), 1)
Start-Sleep 2
[PW]::Shot($h, (Join-Path $Dest 'dashboard.png'))
# menu de contexto (composto sobre o painel) e dialogo de confirmacao, cancelado com ESC
[PW]::Clique($t, 0x204, 0x205, 30, $HeaderH + [int]($RowH / 2), 2)
Start-Sleep -Milliseconds 800
$m = @([PW]::Topo($g.Id, $h))
if ($m.Count) {
  $tmp = Join-Path $env:TEMP 'cmdw-menu.png'; [PW]::Shot($m[0], $tmp)
  $pt = Join-Path $env:TEMP 'cmdw-painel.png'; [PW]::Shot($h, $pt)
  $mr = New-Object PW+RECT; [void][PW]::GetWindowRect($m[0], [ref]$mr)
  $wr = New-Object PW+RECT; [void][PW]::GetWindowRect($h, [ref]$wr)
  $base = [Drawing.Image]::FromFile($pt); $mi = [Drawing.Image]::FromFile($tmp)
  $gr = [Drawing.Graphics]::FromImage($base); $gr.DrawImage($mi, $mr.L - $wr.L - 7, $mr.T - $wr.T); $gr.Dispose()
  # recorte em volta do menu
  $x = [Math]::Max(0, $mr.L - $wr.L - 260); $y = [Math]::Max(0, $mr.T - $wr.T - 120)
  $cw = [Math]::Min($base.Width - $x, 760); $ch = [Math]::Min($base.Height - $y, 300)
  $crop = New-Object Drawing.Bitmap $cw, $ch; $gc = [Drawing.Graphics]::FromImage($crop)
  $gc.DrawImage($base, (New-Object Drawing.Rectangle 0, 0, $cw, $ch), (New-Object Drawing.Rectangle $x, $y, $cw, $ch), [Drawing.GraphicsUnit]::Pixel); $gc.Dispose()
  $crop.Save((Join-Path $Dest 'menu-contexto.png'), [Drawing.Imaging.ImageFormat]::Png)
  $crop.Dispose(); $base.Dispose(); $mi.Dispose(); Remove-Item $tmp, $pt -EA 0
  $r = New-Object PW+RECT; [void][PW]::GetWindowRect($m[0], [ref]$r)
  [PW]::Clique($m[0], 0x201, 0x202, [int](($r.R - $r.L) / 2), ($r.B - $r.T) - 15, 1)
  Start-Sleep -Milliseconds 900
  $d = @([PW]::Topo($g.Id, $h))
  if ($d.Count) { [PW]::Shot($d[0], (Join-Path $Dest 'confirmacao.png')); [PW]::PostMessage($d[0], 0x100, [IntPtr]0x1B, [IntPtr]::Zero) | Out-Null; "ok: menu e dialogo" }
  else { 'dialogo nao abriu' }
} else { 'menu nao abriu' }
Start-Sleep 1
try { $g.CloseMainWindow() | Out-Null; if (-not $g.WaitForExit(4000)) { $g.Kill() } } catch {}
foreach ($p in @($v, $o)) { try { if (-not $p.HasExited) { Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" | % { Stop-Process -Id $_.ProcessId -Force -EA 0 }; $p.Kill() } } catch {} }
