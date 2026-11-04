# Teste real do painel (gui): D5 pelo caminho do ConsoleProbe, mascara na tabela e
# a acao "Fechar" de ponta a ponta dirigindo a UI (mensagens de mouse/teclado + UI Automation).
# Janelas visiveis: o proprio painel e as janelas de console que o cenario exige.
# So encerra processos que o teste iniciou.
param([string]$Exe = "$PSScriptRoot\..\out\cmdwatch.exe", [string]$Shots = "$PSScriptRoot\..\docs\screens",
      [string]$Nome = 'painel', [int]$HeaderH = 20, [int]$RowH = 22, [switch]$SoFoto)
$ErrorActionPreference = 'Continue'
$base = Join-Path $env:TEMP 'cmdw-tests'; $repro = Join-Path $base 'repro'; $logs = Join-Path $base 'logs'
New-Item -ItemType Directory -Force $repro, $logs, $Shots | Out-Null
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
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool r);
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
  public static void Clique(IntPtr h, uint down, uint up, int x, int y, int mk) {
    IntPtr lp = (IntPtr)((y << 16) | (x & 0xFFFF));
    PostMessage(h, down, (IntPtr)mk, lp); PostMessage(h, up, IntPtr.Zero, lp);
  }
}
"@ }
$AE = [Windows.Automation.AutomationElement]; $TS = [Windows.Automation.TreeScope]
$res = New-Object System.Collections.Generic.List[string]
function Ok($id, $cond, $info) { $script:res.Add(("{0} {1} {2}" -f $id, $(if ($cond) {'PASSOU'} else {'FALHOU'}), $info)) }
function Oculto($file, $args_) {
  $p = New-Object Diagnostics.ProcessStartInfo -ArgumentList $file, $args_
  $p.UseShellExecute = $false; $p.CreateNoWindow = $true; return [Diagnostics.Process]::Start($p)
}
function Topos($procId) {
  $c = New-Object Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $procId)
  @($AE::RootElement.FindAll($TS::Children, $c))
}
function Painel($log, $velho) {
  if (Test-Path $log) { Remove-Item $log -Force }
  if ($velho) { $env:CMDWATCH_TESTE_PID_VELHO = '1' } else { Remove-Item Env:CMDWATCH_TESTE_PID_VELHO -EA 0 }
  $g = Start-Process $Exe -ArgumentList 'gui', '--log', "`"$log`"" -PassThru
  Remove-Item Env:CMDWATCH_TESTE_PID_VELHO -EA 0
  for ($i = 0; $i -lt 40 -and $g.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $g.Refresh() }
  Start-Sleep 4; return $g
}
function Tabela($g) { [PW]::Filho($g.MainWindowHandle, 'tabelaSessoes') }
# a ordem da tabela e por inicio (mais nova primeiro): a sessao do teste e a linha 0
function Fechar($g, $linha, $foto) {
  $t = Tabela $g; if ($t -eq [IntPtr]::Zero) { return 'tabela de sessoes nao encontrada' }
  [PW]::SetForegroundWindow($g.MainWindowHandle) | Out-Null
  [PW]::Clique($t, 0x204, 0x205, 30, $HeaderH + $linha * $RowH + [int]($RowH / 2), 2)   # botao direito na linha
  Start-Sleep -Milliseconds 700
  $m = @([PW]::Topo($g.Id, $g.MainWindowHandle)); if ($m.Count -eq 0) { return 'menu da sessao nao abriu' }
  $r = New-Object PW+RECT; [PW]::GetWindowRect($m[0], [ref]$r) | Out-Null
  if ($foto) { [PW]::Shot($m[0], ($foto -replace 'dialogo', 'menu')) }
  # ultimo item do menu = "Fechar" (cabecalho desativado, separador, Fechar)
  [PW]::Clique($m[0], 0x201, 0x202, [int](($r.R - $r.L) / 2), ($r.B - $r.T) - 15, 1)
  Start-Sleep -Milliseconds 900
  $d = @([PW]::Topo($g.Id, $g.MainWindowHandle)); if ($d.Count -eq 0) { return 'dialogo de confirmacao nao abriu' }
  if ($foto) { [PW]::Shot($d[0], $foto) }
  $titulo = $AE::FromHandle($d[0]).Current.Name
  [PW]::PostMessage($d[0], 0x100, [IntPtr]0x0D, [IntPtr]::Zero) | Out-Null   # Enter = confirmar
  Start-Sleep -Milliseconds 1500
  return "ok (dialogo: $titulo)"
}
Set-Content (Join-Path $repro 'segredos-longo.cmd') "@echo off`r`ncmd /c rem sqlcmd -S db `"Server=db;User Id=sa;Password=Pa55w0rd;`"`r`nping -n 90 127.0.0.1 >nul" -Encoding ASCII

# ---- painel 1: cenarios D5 + mascara + Fechar
$log = Join-Path $logs "gui-$Nome.jsonl"
$g = Painel $log $false
(Oculto 'node.exe' ("`"" + (Join-Path $repro 'launch_pai.js') + "`"")).WaitForExit()      # node -> python -> git, aba do Windows Terminal
$ch = Start-Process conhost.exe -ArgumentList 'python', "`"$(Join-Path $repro 'script_pai.py')`"" -PassThru   # mesmo script num console classico
$sec = Start-Process python -ArgumentList '-c', '"import time; time.sleep(40)"', '--password', 'Hunter2xyz', 'token=abc123def456' -PassThru
$ses = Oculto 'cmd.exe' ("/c `"" + (Join-Path $repro 'segredos-longo.cmd') + "`" --api-key ZZsegredo99 token=abc123def456")
Start-Sleep 6
[PW]::SetForegroundWindow($g.MainWindowHandle) | Out-Null; Start-Sleep -Milliseconds 500
[PW]::Shot($g.MainWindowHandle, (Join-Path $Shots "$Nome.png"))
# segunda captura no tamanho da referencia (1024x640) e volta ao tamanho padrao
$rc = New-Object PW+RECT; [void][PW]::GetWindowRect($g.MainWindowHandle, [ref]$rc)
[void][PW]::MoveWindow($g.MainWindowHandle, $rc.L, $rc.T, 1024, 640, $true); Start-Sleep 2
[PW]::Shot($g.MainWindowHandle, (Join-Path $Shots "$Nome-1024.png"))
[void][PW]::MoveWindow($g.MainWindowHandle, $rc.L, $rc.T, $rc.R - $rc.L, $rc.B - $rc.T, $true); Start-Sleep 1
if (-not $SoFoto) {
  $ping = Get-CimInstance Win32_Process -Filter "ParentProcessId=$($ses.Id)" | Where-Object Name -eq 'PING.EXE'
  $r = Fechar $g 0 (Join-Path $Shots "$Nome-dialogo.png")
  Start-Sleep 2
  $pingVivo = if ($ping) { [bool](Get-Process -Id $ping.ProcessId -EA 0) } else { $null }
  Ok 'FECHAR' ($ses.HasExited -and $pingVivo -eq $false) ("$r; cmd $($ses.Id) encerrado=$($ses.HasExited); ping filho " + $(if ($ping) { "$($ping.ProcessId) vivo=$pingVivo" } else { 'nao achado' }))
  [PW]::Shot($g.MainWindowHandle, (Join-Path $Shots "$Nome-fechado.png"))
}
try { $g.CloseMainWindow() | Out-Null; if (-not $g.WaitForExit(4000)) { $g.Kill() } } catch {}
foreach ($p in @($sec, $ch, $ses)) { try { if (-not $p.HasExited) { $p.Kill() } } catch {} }
Start-Sleep 2   # o log do painel termina de ser gravado depois do fechamento
$L = if (Test-Path $log) { [IO.File]::ReadAllLines($log) } else { @() }
# estado final de cada janela de console/terminal (ultimo evento por hwnd) - uma linha da tabela por janela
$fim = @{}; foreach ($l in $L) { if ($l -match '"type":"window_' -and $l -match '"class":"(CASCADIA_HOSTING_WINDOW_CLASS|ConsoleWindowClass|PseudoConsoleWindow)"') { $o = $l | ConvertFrom-Json; $fim[$o.window.hwnd] = $o.window } }
$d5 = @($fim.Values | Where-Object { $_.authorCmdLine -match 'script_pai' -or ($_.chain -and (($_.chain | ConvertTo-Json -Compress) -match 'script_pai')) })
$cas = @($fim.Values | Where-Object { $_.class -ne 'PseudoConsoleWindow' -and $_.firstSeen -and $_.author -match 'git' })
$aux = @($fim.Values | Where-Object { $_.class -eq 'PseudoConsoleWindow' -and -not $_.auxiliary })
$txt = ($d5 | ForEach-Object { $_.class + '=' + $_.author }) -join ', '
Ok 'D5-painel' (($d5.Count -ge 2) -and -not ($d5 | Where-Object { $_.author -ne 'python.exe' }) -and $cas.Count -eq 0 -and $aux.Count -eq 0) ("janelas do script_pai (estado final): $txt; com autor git: " + $cas.Count + "; PseudoConsole como linha: " + $aux.Count)$vaz = $L | Where-Object { $_ -match 'Hunter2xyz|abc123def456|ZZsegredo99|Pa55w0rd' }
$mas = $L | Where-Object { $_ -match '--password \*\*\*|token=\*\*\*' }
Ok 'MASCARA-painel' (($vaz.Count -eq 0) -and ($mas.Count -gt 0)) ("linhas do log do painel com segredo: " + $vaz.Count + ", com mascara: " + $mas.Count)

# ---- painel 2: registro velho (pid reaproveitado simulado) -> nao pode encerrar
if (-not $SoFoto) {
  $log2 = Join-Path $logs "gui-$Nome-velho.jsonl"
  $g = Painel $log2 $true
  $ses = Oculto 'cmd.exe' ("/c `"" + (Join-Path $repro 'segredos-longo.cmd') + "`"")
  Start-Sleep 4
  $r = Fechar $g 0 $null
  Start-Sleep 1
  $min = [PW]::IsIconic($g.MainWindowHandle); if ($min) { [PW]::ShowWindow($g.MainWindowHandle, 9) | Out-Null; Start-Sleep 1 }
  [PW]::Shot($g.MainWindowHandle, (Join-Path $Shots "$Nome-velho.png")); "(painel 2 estava minimizado: $min)"
  Ok 'FECHAR-velho' (($r -like 'ok*') -and -not $ses.HasExited) ("$r; cmd $($ses.Id) continua vivo=" + (-not $ses.HasExited))
  try { $g.CloseMainWindow() | Out-Null; if (-not $g.WaitForExit(4000)) { $g.Kill() } } catch {}
  Get-CimInstance Win32_Process -Filter "ParentProcessId=$($ses.Id)" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -EA 0 }
  try { if (-not $ses.HasExited) { $ses.Kill() } } catch {}
}
$res
if ($res | Where-Object { $_ -match ' FALHOU ' }) { exit 1 } else { exit 0 }