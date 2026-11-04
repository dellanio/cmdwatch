# Teste dos botoes "Limpar" (Sessoes CMD) e "recolher/expandir" (todos os cartoes).
# Janela visivel: so o proprio painel (necessaria ao teste). Scripts .cmd ocultos (CreateNoWindow).
# So encerra processos que o teste iniciou (por PID). Restaura o %APPDATA%\cmdwatch\ui.cfg no final.
param([string]$Exe = "$PSScriptRoot\..\out\cmdwatch.exe", [string]$Shots = "$PSScriptRoot\..\docs\screens")
$ErrorActionPreference = 'Continue'
$base = Join-Path $env:TEMP 'cmdw-tests'; $repro = Join-Path $base 'repro'; $logs = Join-Path $base 'logs'
New-Item -ItemType Directory -Force $repro, $logs, $Shots | Out-Null
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
if (-not ('PWL' -as [type])) { Add-Type -ReferencedAssemblies System.Drawing, Accessibility @"
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices; using System.Text;
public static class PWL {
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
      Rectangle c = new Rectangle(e.L - w.L, e.T - w.T, e.R - e.L, e.B - e.T);
      using (Bitmap o = b.Clone(c, b.PixelFormat)) o.Save(path, ImageFormat.Png);
    }
  }
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  public static string Texto(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
  public static IntPtr Filho(IntPtr pai, string prefixo) {
    IntPtr achou = IntPtr.Zero;
    EnumChildWindows(pai, delegate (IntPtr h, IntPtr l) {
      string t = Texto(h); if (t == prefixo || t.StartsWith(prefixo + "|")) { achou = h; return false; } return true; }, IntPtr.Zero);
    return achou;
  }
  [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr h, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object o);
  // acao padrao MSAA do controle (o botao do painel expoe "Press")
  public static string Acionar(IntPtr h) {
    Guid iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71"); object o;
    if (AccessibleObjectFromWindow(h, 0xFFFFFFFC, ref iid, out o) != 0 || o == null) return "sem IAccessible";
    var a = (Accessibility.IAccessible)o; string acao = a.get_accDefaultAction(0); a.accDoDefaultAction(0); return acao;
  }
  public static int Altura(IntPtr h) { RECT r; GetWindowRect(h, out r); return r.B - r.T; }

}
"@ }
$res = New-Object System.Collections.Generic.List[string]
function Ok($id, $cond, $info) { $script:res.Add(("{0} {1} {2}" -f $id, $(if ($cond) {'PASSOU'} else {'FALHOU'}), $info)) }
$meus = New-Object System.Collections.Generic.List[int]
function Sessao($nome) {
  $f = Join-Path $repro $nome
  Set-Content $f "@echo off`r`nping -n 45 127.0.0.1 >nul" -Encoding ASCII
  $p = New-Object Diagnostics.ProcessStartInfo -ArgumentList 'cmd.exe', ("/c `"$f`"")
  $p.UseShellExecute = $false; $p.CreateNoWindow = $true
  $pr = [Diagnostics.Process]::Start($p); $script:meus.Add($pr.Id); return $pr
}
# aciona o botao pela UI Automation (Invoke / acao padrao), sem mover o mouse do usuario
function Clicar($h) { [PWL]::Acionar($h) | Out-Null; Start-Sleep -Milliseconds 100 }
function Nomes($g) { $h = [PWL]::Filho($g.MainWindowHandle, 'cartao_sessoes'); $t = [PWL]::Texto($h); $x = $t -split '\|'; if ($x.Count -ge 3) { $x[2] } else { '' } }
function Linhas($g) { $h = [PWL]::Filho($g.MainWindowHandle, 'cartao_sessoes'); $t = [PWL]::Texto($h); if ($t -match 'linhas=(\d+)') { [int]$Matches[1] } else { -1 } }
function Esperar([scriptblock]$c, $s) { $fim = (Get-Date).AddSeconds($s); while ((Get-Date) -lt $fim) { if (& $c) { return $true }; Start-Sleep -Milliseconds 250 }; return $false }
function Alt($g, $k) { [PWL]::Altura([PWL]::Filho($g.MainWindowHandle, "cartao_$k")) }
function Painel($log) {
  $g = Start-Process $Exe -ArgumentList 'gui', '--log', "`"$log`"" -PassThru
  for ($i = 0; $i -lt 40 -and $g.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $g.Refresh() }
  Start-Sleep 3; return $g
}
function Fechar($g) { [PWL]::PostMessage($g.MainWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
  if (-not $g.WaitForExit(5000)) { try { $g.Kill() } catch {} } }

# ui.cfg do usuario: guarda e restaura no final
$cfg = Join-Path $env:APPDATA 'cmdwatch\ui.cfg'; $cfgAntes = if (Test-Path $cfg) { [IO.File]::ReadAllText($cfg) } else { $null }
if ($cfgAntes -ne $null) { [IO.File]::WriteAllText($cfg, (($cfgAntes -split "`r?`n" | Where-Object { $_ -notmatch '^recolhidos=' }) -join "`r`n")) }
$log = Join-Path $logs 'limpar-recolher.jsonl'; if (Test-Path $log) { Remove-Item $log -Force }
try {
  $g = Painel $log
  # ---- Limpar
  $a = Sessao 'limpar-a.cmd'; $b = Sessao 'limpar-b.cmd'
  $ok = Esperar { (Nomes $g) -match 'limpar-a' -and (Nomes $g) -match 'limpar-b' } 15
  $n0 = Linhas $g; Ok 'L0' $ok "sessoes listadas antes do Limpar: $n0"
  [PWL]::Shot($g.MainWindowHandle, (Join-Path $Shots 'painel-v2-antes-limpar.png'))
  $logAntes = (Get-Item $log).Length
  Clicar([PWL]::Filho($g.MainWindowHandle, 'btnLimparSessoes'))
  $ok = Esperar { (Linhas $g) -eq 0 } 5; $n1 = Linhas $g; Start-Sleep 3
  Ok 'L1' ($ok -and (Nomes $g) -notmatch 'limpar-') ("lista vazia logo apos Limpar: $n1 linha(s); 3 s depois: " + (Linhas $g) + " [" + (Nomes $g) + "]")
  $a.Refresh(); $b.Refresh(); Ok 'L2' (-not $a.HasExited -and -not $b.HasExited) 'sessoes limpas continuam rodando (nada encerrado)'
  [PWL]::Shot($g.MainWindowHandle, (Join-Path $Shots 'painel-v2-depois-limpar.png'))
  $c = Sessao 'limpar-c.cmd'
  $ok = Esperar { (Nomes $g) -match 'limpar-c' } 15; Start-Sleep 2
  Ok 'L3' ($ok -and (Nomes $g) -notmatch 'limpar-[ab]') ("so a sessao nova aparece: " + (Linhas $g) + " linha(s) [" + (Nomes $g) + "]")
  $L = Get-Content $log; $nOpen = @($L | Select-String '"type":"batch_open"' | Select-String 'limpar-').Count
  Ok 'L4' ((Get-Item $log).Length -gt $logAntes -and $nOpen -ge 3) "log intacto e seguindo (batch_open dos 3 scripts no log: $nOpen)"
  [PWL]::Shot($g.MainWindowHandle, (Join-Path $Shots 'painel-v2-limpar-nova.png'))
  # ---- Recolher
  $h0 = @{}; foreach ($k in 'sessoes','janelas','fluxo','atividade') { $h0[$k] = Alt $g $k }
  Clicar([PWL]::Filho($g.MainWindowHandle, 'recolher_janelas')); Start-Sleep -Milliseconds 900
  $h1 = @{}; foreach ($k in 'sessoes','janelas','fluxo','atividade') { $h1[$k] = Alt $g $k }
  Ok 'R1' ($h1.janelas -le 42) ("Janelas detectadas recolhida: {0} -> {1} px" -f $h0.janelas, $h1.janelas)
  Ok 'R2' ($h1.sessoes -gt $h0.sessoes -and $h1.fluxo -gt $h0.fluxo) ("outros cresceram: sessoes {0}->{1}, fluxo {2}->{3}, atividade {4}->{5}" -f $h0.sessoes,$h1.sessoes,$h0.fluxo,$h1.fluxo,$h0.atividade,$h1.atividade)
  [PWL]::Shot($g.MainWindowHandle, (Join-Path $Shots 'painel-v2-recolhido.png'))
  Ok 'R3' ((Get-Content $cfg) -contains 'recolhidos=janelas') ('ui.cfg: ' + ((Get-Content $cfg) -join ' ; '))
  Fechar $g
  $g = Painel $log
  $h2 = Alt $g 'janelas'; Ok 'R4' ($h2 -le 42) "apos reiniciar continua recolhida: $h2 px"
  Clicar([PWL]::Filho($g.MainWindowHandle, 'recolher_janelas')); Start-Sleep -Milliseconds 900
  $h3 = @{}; foreach ($k in 'sessoes','janelas','fluxo','atividade') { $h3[$k] = Alt $g $k }
  Ok 'R5' ([Math]::Abs($h3.janelas - $h0.janelas) -le 2 -and [Math]::Abs($h3.sessoes - $h0.sessoes) -le 2 -and [Math]::Abs($h3.fluxo - $h0.fluxo) -le 2) ("expandida de novo: janelas {0}, sessoes {1}, fluxo {2} (originais {3}/{4}/{5})" -f $h3.janelas,$h3.sessoes,$h3.fluxo,$h0.janelas,$h0.sessoes,$h0.fluxo)
  [PWL]::Shot($g.MainWindowHandle, (Join-Path $Shots 'painel-v2-expandido.png'))
  # todos recolhidos e de volta (layout nao quebra)
  foreach ($k in 'sessoes','fluxo') { Clicar([PWL]::Filho($g.MainWindowHandle, "recolher_$k")); Start-Sleep -Milliseconds 600 }
  $h4 = @{}; foreach ($k in 'sessoes','janelas','fluxo','atividade') { $h4[$k] = Alt $g $k }
  Ok 'R6' ($h4.sessoes -le 42 -and $h4.fluxo -le 42 -and $h4.janelas -gt $h0.janelas -and $h4.atividade -gt $h0.atividade) ("sessoes+fluxo recolhidos: sessoes {0}, fluxo {1}, janelas {2}, atividade {3}" -f $h4.sessoes,$h4.fluxo,$h4.janelas,$h4.atividade)
  [PWL]::Shot($g.MainWindowHandle, (Join-Path $Shots 'painel-v2-dois-recolhidos.png'))
  foreach ($k in 'sessoes','fluxo') { Clicar([PWL]::Filho($g.MainWindowHandle, "recolher_$k")); Start-Sleep -Milliseconds 600 }
  Fechar $g
} finally {
  foreach ($id in $meus) { & taskkill.exe /T /F /PID $id 2>&1 | Out-Null }
  if ($cfgAntes -ne $null) { [IO.File]::WriteAllText($cfg, $cfgAntes) } elseif (Test-Path $cfg) { Remove-Item $cfg }
}
$res
