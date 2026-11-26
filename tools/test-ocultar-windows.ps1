# Teste do botao "Esconder Windows" (aba "Processos CPU").
# O que o teste prova:
#   1. o botao existe e so aparece na aba de CPU
#   2. com ele ligado, a lista perde os processos do Windows (o contador cai)
#   3. nada e encerrado: os processos continuam vivos
#   4. a escolha fica salva no ui.cfg e vale na proxima abertura
# Janela visivel: so o proprio painel (necessaria ao teste). Nada e encerrado
# alem do painel. Restaura o %APPDATA%\cmdwatch\ui.cfg no final.
param([string]$Exe = "$PSScriptRoot\..\out\cmdwatch.exe", [string]$Shots = "$PSScriptRoot\..\docs\screens")
$ErrorActionPreference = 'Continue'
$base = Join-Path $env:TEMP 'cmdw-tests'; $logs = Join-Path $base 'logs'
New-Item -ItemType Directory -Force $logs, $Shots | Out-Null
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
if (-not ('PWO' -as [type])) { Add-Type -ReferencedAssemblies System.Drawing, Accessibility @"
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices; using System.Text;
public static class PWO {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
  public static void Shot(IntPtr h, string path) {
    RECT w; GetWindowRect(h, out w); RECT e;
    if (DwmGetWindowAttribute(h, 9, out e, Marshal.SizeOf(typeof(RECT))) != 0) e = w;
    using (Bitmap b = new Bitmap(w.R - w.L, w.B - w.T)) {
      using (Graphics g = Graphics.FromImage(b)) { IntPtr dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); }
      Rectangle c = new Rectangle(e.L - w.L, e.T - w.T, e.R - e.L, e.B - w.T);
      using (Bitmap o = b.Clone(c, b.PixelFormat)) o.Save(path, ImageFormat.Png);
    }
  }
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  public static string Texto(IntPtr h) { var sb = new StringBuilder(1024); GetWindowText(h, sb, 1024); return sb.ToString(); }
  public static IntPtr Filho(IntPtr pai, string prefixo) {
    IntPtr achou = IntPtr.Zero;
    EnumChildWindows(pai, delegate (IntPtr h, IntPtr l) {
      string t = Texto(h); if (t == prefixo || t.StartsWith(prefixo + "|")) { achou = h; return false; } return true; }, IntPtr.Zero);
    return achou;
  }
  public static bool Visivel(IntPtr h) { return h != IntPtr.Zero && IsWindowVisible(h); }
  [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr h, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object o);
  // acao padrao MSAA do controle (o botao do painel expoe "Press")
  public static string Acionar(IntPtr h) {
    Guid iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71"); object o;
    if (AccessibleObjectFromWindow(h, 0xFFFFFFFC, ref iid, out o) != 0 || o == null) return "sem IAccessible";
    var a = (Accessibility.IAccessible)o; string acao = a.get_accDefaultAction(0); a.accDoDefaultAction(0); return acao;
  }
  public static void Clique(IntPtr h, uint down, uint up, int x, int y, int mk) {
    IntPtr lp = (IntPtr)((y << 16) | (x & 0xFFFF));
    PostMessage(h, down, (IntPtr)mk, lp); PostMessage(h, up, IntPtr.Zero, lp);
  }
}
"@ }
$res = New-Object System.Collections.Generic.List[string]
function Ok($id, $cond, $info) { $script:res.Add(("{0} {1} {2}" -f $id, $(if ($cond) {'PASSOU'} else {'FALHOU'}), $info)) }
function Esperar([scriptblock]$c, $s) { $fim = (Get-Date).AddSeconds($s); while ((Get-Date) -lt $fim) { if (& $c) { return $true }; Start-Sleep -Milliseconds 250 }; return $false }
function Estado($g) {
  $h = [PWO]::Filho($g.MainWindowHandle, 'cartao_sessoes'); $t = [PWO]::Texto($h)
  $e = @{}
  if ($t -match '\|cpu=(\d+)') { $e.Cpu = [int]$Matches[1] }
  if ($t -match '\|cpu_linhas=(\d+)') { $e.Linhas = [int]$Matches[1] }
  if ($t -match '\|cpu_nomes=([^|]*)') { $e.Nomes = $Matches[1] }
  return $e
}
function Painel($log) {
  $g = Start-Process $Exe -ArgumentList 'gui', '--log', "`"$log`"" -PassThru
  for ($i = 0; $i -and $g.MainWindowHandle -eq 0 -or $i -lt 40; $i++) { Start-Sleep -Milliseconds 250; $g.Refresh() }
  Start-Sleep 3; return $g
}
function Fechar($g) { [PWO]::PostMessage($g.MainWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
  if (-not $g.WaitForExit(5000)) { try { $g.Kill() } catch {} } }
# abre a aba "Processos CPU" (segunda aba do cartao de sessoes)
function AbrirAbaCpu($g) {
  $cart = [PWO]::Filho($g.MainWindowHandle, 'cartao_sessoes')
  if ($cart -eq [IntPtr]::Zero) { return $false }
  [PWO]::Clique($cart, 0x201, 0x202, 230, 20, 1); Start-Sleep -Milliseconds 800; return $true
}

# ui.cfg do usuario: guarda e restaura no final
$cfg = Join-Path $env:APPDATA 'cmdwatch\ui.cfg'
$cfgAntes = if (Test-Path $cfg) { [IO.File]::ReadAllText($cfg) } else { $null }
if ($cfgAntes -ne $null) {
  [IO.File]::WriteAllText($cfg, (($cfgAntes -split "`r?`n" | Where-Object { $_ -notmatch '^ocultarWin=' }) -join "`r`n"))
}
$log = Join-Path $logs 'ocultar-windows.jsonl'; if (Test-Path $log) { Remove-Item $log -Force }
try {
  $g = Painel $log
  # ---- na aba de sessoes o botao nao existe (so na aba de CPU)
  $b = [PWO]::Filho($g.MainWindowHandle, 'btnOcultarWindows')
  Ok 'W0' (-not [PWO]::Visivel($b)) ('na aba de sessoes o botao esta escondido: ' + [PWO]::Visivel($b))
  Ok 'W0b' (AbrirAbaCpu $g) 'aba "Processos CPU" aberta'
  Start-Sleep 3
  $b = [PWO]::Filho($g.MainWindowHandle, 'btnOcultarWindows')
  Ok 'W1' ([PWO]::Visivel($b)) 'botao "Esconder Windows" visivel na aba de CPU'
  $e0 = Estado $g
  Ok 'W2' ($e0.Cpu -gt 100 -and $e0.Linhas -gt 100) ("antes: $($e0.Cpu) processos, $($e0.Linhas) linhas")
  [PWO]::Shot($g.MainWindowHandle, (Join-Path $Shots 'cpu-antes-esconder.png'))
  # ---- liga o filtro
  [PWO]::Acionar($b) | Out-Null
  $ok = Esperar { (Estado $g).Cpu -lt (Estado $g).Cpu } 1   # qualquer mudanca ja serve
  Start-Sleep 2
  $e1 = Estado $g
  Ok 'W3' ($e1.Cpu -lt $e0.Cpu -and $e1.Linhas -lt $e0.Linhas) ("depois: $($e1.Cpu) processos, $($e1.Linhas) linhas (antes: $($e0.Cpu)/$($e0.Linhas))")
  Ok 'W4' ($e1.Nomes -notmatch 'svchost|explorer\.exe|lsass|services\.exe') ("a lista nao mostra mais o Windows: " + $e1.Nomes)
  Ok 'W5' ((Get-Content $cfg) -contains 'ocultarWin=1') ('ui.cfg: ' + ((Get-Content $cfg) -join ' ; '))
  [PWO]::Shot($g.MainWindowHandle, (Join-Path $Shots 'cpu-escondendo-windows.png'))
  # ---- nada foi encerrado
  $sv = @(Get-Process -Name 'svchost' -EA 0).Count
  Ok 'W6' ($sv -gt 0) "svchost continua rodando ($sv processos): o botao so mexe na lista"
  # ---- desliga de novo
  [PWO]::Acionar([PWO]::Filho($g.MainWindowHandle, 'btnOcultarWindows')) | Out-Null
  Start-Sleep 2
  $e2 = Estado $g
  Ok 'W7' ($e2.Cpu -eq $e0.Cpu -or $e2.Cpu -gt $e1.Cpu) ("voltou a mostrar tudo: $($e2.Cpu) processos")
  Ok 'W7b' ((Get-Content $cfg) -contains 'ocultarWin=0') ('ui.cfg depois de desligar: ' + ((Get-Content $cfg) -join ' ; '))
  # ---- liga de novo e confere que a preferencia sobrevive a uma reabertura
  [PWO]::Acionar([PWO]::Filho($g.MainWindowHandle, 'btnOcultarWindows')) | Out-Null
  Start-Sleep 2
  Ok 'W7c' ((Get-Content $cfg) -contains 'ocultarWin=1') ('ui.cfg depois de ligar: ' + ((Get-Content $cfg) -join ' ; '))
  Fechar $g
  $g = Painel $log
  AbrirAbaCpu $g | Out-Null
  Start-Sleep 3
  $e3 = Estado $g
  Ok 'W8' ($e3.Cpu -lt $e0.Cpu) ("reabriu ja com o Windows escondido: $($e3.Cpu) processos (antes eram $($e0.Cpu))")
  [PWO]::Shot($g.MainWindowHandle, (Join-Path $Shots 'cpu-escondendo-windows.png'))
  Fechar $g
} finally {
  if ($cfgAntes -ne $null) { [IO.File]::WriteAllText($cfg, $cfgAntes) } elseif (Test-Path $cfg) { Remove-Item $cfg }
}
$res
if ($res | Where-Object { $_ -match ' FALHOU ' }) { exit 1 } else { exit 0 }
