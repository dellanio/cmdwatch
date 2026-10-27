# click-session.ps1 - aciona o menu de contexto de uma sessao do painel.
#
#   .\tools\click-session.ps1 -Row 0                 # botao direito na 1a linha
#   .\tools\click-session.ps1 -Row 0 -Shot out\m.png # captura a janela do menu
#   .\tools\click-session.ps1 -Row 0 -Vk 40          #VK_DOWN no menu
#   .\tools\click-session.ps1 -Row 0 -Vk 13          # VK_ENTER no menu/dialogo
#
# Localiza o controle SessionTable entre as janelas filhas do painel (pela
# geometria do layout), dispara WM_RBUTTONDOWN/WM_RBUTTONUP e depois pode
# enviar teclas ou capturar a janela de popup (menu / dialogo).

param(
    [int]$Row = 0,
    [string]$Vk = '',
    [string]$Shot = '',
    [int]$WaitMs = 600,
    [switch]$SkipClick,
    [string]$ClickAt = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# caminho relativo e resolvido a partir da raiz do repo: o .NET usa o CWD do
# processo (nao o do PowerShell) ao salvar
$root = Split-Path -Parent $PSScriptRoot
if ($Shot -ne '' -and -not [System.IO.Path]::IsPathRooted($Shot)) {
    $Shot = Join-Path $root $Shot
}

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class CmdCtx {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);

    public static string ClassOf(IntPtr h) {
        StringBuilder sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string Title(IntPtr h) {
        StringBuilder sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static List<IntPtr> Children(IntPtr parent) {
        List<IntPtr> list = new List<IntPtr>();
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }

    public static List<IntPtr> TopLevel(uint pid) {
        List<IntPtr> list = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h)) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static POINT Origin(IntPtr h) {
        POINT p; p.X = 0; p.Y = 0;
        ClientToScreen(h, ref p);
        return p;
    }

    public static int Width(IntPtr h) { RECT r; GetClientRect(h, out r); return r.Right - r.Left; }
    public static int Height(IntPtr h) { RECT r; GetClientRect(h, out r); return r.Bottom - r.Top; }
    public static RECT Rect(IntPtr h) { RECT r; GetWindowRect(h, out r); return r; }
}
"@

$proc = Get-Process cmdwatch -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if ($proc -eq $null) { throw 'painel do cmdwatch nao esta em execucao' }

# a janela principal e a que tem o titulo do painel: o MainWindowHandle pode
# apontar para a sombra do menu de contexto (janela auxiliar visivel)
$form = [IntPtr]::Zero
foreach ($w in [CmdCtx]::TopLevel([uint32]$proc.Id)) {
    if ([CmdCtx]::Title($w) -like 'cmdwatch*') { $form = $w; break }
}
if ($form -eq [IntPtr]::Zero) { throw 'janela do painel nao encontrada' }
Write-Host "painel: $form"
$formOrigin = [CmdCtx]::Origin($form)

# a tabela de sessoes fica em (18, 40) em coordenadas de cliente do painel
# (pad 8 + PlotArea 10 ; pad 8 + PlotArea 32)
$table = [IntPtr]::Zero
$best = 999999
foreach ($k in [CmdCtx]::Children($form)) {
    $o = [CmdCtx]::Origin($k)
    $dx = [Math]::Abs(($o.X - $formOrigin.X) - 18)
    $dy = [Math]::Abs(($o.Y - $formOrigin.Y) - 40)
    $d = $dx + $dy
    if ($d -lt $best) { $best = $d; $table = $k }
}
if ($table -eq [IntPtr]::Zero) { throw 'tabela de sessoes nao encontrada' }
Write-Host ("tabela de sessoes: {0}  (desvio {1}px)" -f $table, $best)

# 0) fecha um popup que tenha ficado aberto (o menu perde foco quando o
#    console do agente volta para frente) - senao o clique direito e engolido
foreach ($w in [CmdCtx]::TopLevel([uint32]$proc.Id)) {
    if ($w -eq $form) { continue }
    if ([CmdCtx]::ClassOf($w) -eq 'SysShadow') { continue }
    [CmdCtx]::PostMessage($w, 0x0100, [IntPtr]27, [IntPtr]0) | Out-Null   # VK_ESCAPE
    [CmdCtx]::PostMessage($w, 0x0101, [IntPtr]27, [IntPtr]0) | Out-Null
    Write-Host "popup anterior fechado: $w"
    Start-Sleep -Milliseconds 400
}

# 1) botao direito na linha pedida (cabecalho = 20px, linha = 22px)
$y = 20 + ($Row * 22) + 10
$x = 120
if (-not $SkipClick) {
    [CmdCtx]::SetForegroundWindow($form) | Out-Null
    Start-Sleep -Milliseconds 200

    $lParam = [IntPtr](($y -shl 16) -bor ($x -band 0xFFFF))
    [CmdCtx]::PostMessage($table, 0x0204, [IntPtr]2, $lParam) | Out-Null   # WM_RBUTTONDOWN
    Start-Sleep -Milliseconds 120
    [CmdCtx]::PostMessage($table, 0x0205, [IntPtr]0, $lParam) | Out-Null   # WM_RBUTTONUP
    Start-Sleep -Milliseconds $WaitMs
}

# 2) popup aberto (menu de contexto ou dialogo de confirmacao)
$popups = @()
foreach ($w in [CmdCtx]::TopLevel([uint32]$proc.Id)) {
    if ($w -eq $form) { continue }
    $cls = [CmdCtx]::ClassOf($w)
    if ($cls -eq 'SysShadow') { continue }
    $r = [CmdCtx]::Rect($w)
    $popups += [pscustomobject]@{
        Hwnd = $w
        Class = $cls
        X = $r.Left; Y = $r.Top
        W = $r.Right - $r.Left; H = $r.Bottom - $r.Top
    }
}
foreach ($p in $popups) {
    Write-Host ("popup: {0}  {1}  {2}x{3} @({4},{5})" -f $p.Hwnd, $p.Class, $p.W, $p.H, $p.X, $p.Y)
}

# 3) captura do popup (ou da tela toda, se nao houver popup)
if ($Shot -ne '') {
    if ($popups.Count -gt 0) {
        $p = $popups[0]
        $bmp = New-Object System.Drawing.Bitmap $p.W, $p.H
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $hdc = $g.GetHdc()
        [CmdCtx]::PrintWindow($p.Hwnd, $hdc, 2) | Out-Null
        $g.ReleaseHdc($hdc)
        $g.Dispose()
        $dir = Split-Path -Parent $Shot
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        $bmp.Save($Shot, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Write-Host "captura: $Shot ($($p.W)x$($p.H))"
    } else {
        Write-Host 'nenhum popup aberto'
    }
}

# 4b) clique com o botao esquerdo em um ponto do popup ("x,y" em coordenadas
#     de cliente do popup) - e o caminho que o usuario realmente percorre
if ($ClickAt -ne '' -and $popups.Count -gt 0) {
    $target = $popups[0].Hwnd
    $cx = [int](($ClickAt -split ',')[0].Trim())
    $cy = [int](($ClickAt -split ',')[1].Trim())
    [CmdCtx]::SetForegroundWindow($target) | Out-Null
    Start-Sleep -Milliseconds 150
    $lp = [IntPtr](($cy -shl 16) -bor ($cx -band 0xFFFF))
    [CmdCtx]::PostMessage($target, 0x0201, [IntPtr]1, $lp) | Out-Null   # WM_LBUTTONDOWN
    Start-Sleep -Milliseconds 120
    [CmdCtx]::PostMessage($target, 0x0202, [IntPtr]0, $lp) | Out-Null   # WM_LBUTTONUP
    Write-Host "clique em ($cx,$cy) dentro de $target"
    Start-Sleep -Milliseconds $WaitMs
}

# 4) tecla(s) no popup (VK_DOWN / VK_ENTER) - aceita lista separada por virgula
if ($Vk -ne '' -and $popups.Count -gt 0) {
    $target = $popups[0].Hwnd
    [CmdCtx]::SetForegroundWindow($target) | Out-Null
    Start-Sleep -Milliseconds 150
    foreach ($k in ($Vk -split ',')) {
        $code = [int]$k.Trim()
        [CmdCtx]::PostMessage($target, 0x0100, [IntPtr]$code, [IntPtr]0) | Out-Null   # WM_KEYDOWN
        Start-Sleep -Milliseconds 80
        [CmdCtx]::PostMessage($target, 0x0101, [IntPtr]$code, [IntPtr]0) | Out-Null   # WM_KEYUP
        Write-Host "tecla VK=$code enviada para $target"
        Start-Sleep -Milliseconds 400
    }
    Start-Sleep -Milliseconds $WaitMs
}
