# test-close-session.ps1 - teste ponta a ponta do "Fechar" pelo menu de contexto.
#
#   .\tools\test-close-session.ps1 [-Segment EN]
#
# Fluxo em UM unico processo (o menu de contexto perde o foco quando o console
# do agente volta para frente):
#   painel -> script -> botao direito -> captura do menu -> clica em "Fechar"
#   -> captura do dialogo -> confirma -> captura do painel -> confere se os
#   processos (script + filhos) morreu.

param(
    [string]$Segment = 'PT-BR',
    [string]$Tag = '',
    [switch]$Cancel,
    [switch]$Closed
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$exe  = Join-Path $root 'out\cmdwatch.exe'
$test = Join-Path $root '.build\calltest'
if ($Tag -eq '') { $Tag = $Segment.ToLower().Replace('-', '') }

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class CmdT {
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
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);

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

function Get-Popups([uint32]$pidProc, [IntPtr]$form) {
    $list = @()
    foreach ($w in [CmdT]::TopLevel($pidProc)) {
        if ($w -eq $form) { continue }
        if ([CmdT]::ClassOf($w) -eq 'SysShadow') { continue }
        $list += $w
    }
    return $list
}

function Save-Window([IntPtr]$hwnd, [string]$file) {
    $r = [CmdT]::Rect($hwnd)
    $w = $r.Right - $r.Left
    $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [CmdT]::PrintWindow($hwnd, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  captura: $file ($w x $h)"
}

function Send-Click([IntPtr]$hwnd, [int]$x, [int]$y) {
    $lp = [IntPtr](($y -shl 16) -bor ($x -band 0xFFFF))
    [CmdT]::PostMessage($hwnd, 0x0201, [IntPtr]1, $lp) | Out-Null
    Start-Sleep -Milliseconds 120
    [CmdT]::PostMessage($hwnd, 0x0202, [IntPtr]0, $lp) | Out-Null
}

# ---------------------------------------------------------------- 1. painel
# o idioma vem do %APPDATA%\cmdwatch\ui.cfg - precisa existir ANTES de o
# painel subir
$cfg = Join-Path $env:APPDATA 'cmdwatch\ui.cfg'
Set-Content -Path $cfg -Value ("lang=" + $(if ($Segment -eq 'EN') { 'en' } else { 'pt-br' })) -Encoding ASCII

Get-Process cmdwatch -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800
$panel = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 3
$proc = Get-Process -Id $panel.Id

$form = [IntPtr]::Zero
foreach ($w in [CmdT]::TopLevel([uint32]$proc.Id)) {
    if ([CmdT]::Title($w) -like 'cmdwatch*') { $form = $w; break }
}
if ($form -eq [IntPtr]::Zero) { throw 'janela do painel nao encontrada' }
Write-Host "painel PID $($proc.Id) / janela $form"

# ---------------------------------------------------------------- 2. script
$script = Start-Process -FilePath 'cmd.exe' `
    -ArgumentList '/c', (Join-Path $test 'demo-longo.cmd') `
    -WorkingDirectory $test -WindowStyle Minimized -PassThru
Write-Host "script cmd.exe PID $($script.Id)"
Start-Sleep -Seconds 4

$filhos = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($script.Id)" |
             Select-Object -ExpandProperty ProcessId)
Write-Host "filhos antes: $($filhos -join ', ')"

if ($Closed) {
    # mata o script: o painel deve marcar a sessao como encerrada
    Stop-Process -Id $script.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
    Write-Host 'script encerrado de fora (sessao deve ficar fechada)'
}

# ---------------------------------------------------------------- 3. menu
$formOrigin = [CmdT]::Origin($form)
$table = [IntPtr]::Zero
$best = 999999
foreach ($k in [CmdT]::Children($form)) {
    $o = [CmdT]::Origin($k)
    $d = [Math]::Abs(($o.X - $formOrigin.X) - 18) + [Math]::Abs(($o.Y - $formOrigin.Y) - 40)
    if ($d -lt $best) { $best = $d; $table = $k }
}
Write-Host "tabela de sessoes: $table (desvio $best px)"

[CmdT]::SetForegroundWindow($form) | Out-Null
Start-Sleep -Milliseconds 300
$lp = [IntPtr](((20 + 10) -shl 16) -bor 120)
[CmdT]::PostMessage($table, 0x0204, [IntPtr]2, $lp) | Out-Null    # WM_RBUTTONDOWN
Start-Sleep -Milliseconds 150
[CmdT]::PostMessage($table, 0x0205, [IntPtr]0, $lp) | Out-Null    # WM_RBUTTONUP
Start-Sleep -Milliseconds 900

$popups = Get-Popups ([uint32]$proc.Id) $form
if ($popups.Count -eq 0) { throw 'o menu de contexto nao abriu' }
$menu = $popups[0]
Write-Host "menu: $menu ($([CmdT]::Width($menu))x$([CmdT]::Height($menu)))"
Save-Window $menu (Join-Path $root "out\menu-$Tag.png")

# hover no item: o fundo precisa ficar ciano
$lpMove = [IntPtr]((43 -shl 16) -bor 30)
[CmdT]::PostMessage($menu, 0x0200, [IntPtr]0, $lpMove) | Out-Null    # WM_MOUSEMOVE
Start-Sleep -Milliseconds 500
Save-Window $menu (Join-Path $root "out\menu-hover-$Tag.png")

if ($Closed) {
    # sessao ja encerrada: o item "Fechar" tem que estar desabilitado
    Write-Host 'modo Closed: so o menu (nada e clicado)'
    Save-Window $form (Join-Path $root "out\painel-$Tag.png")
    Write-Host "fim do teste"
    return
}

# ---------------------------------------------------------------- 4. "Fechar"
[CmdT]::SetForegroundWindow($menu) | Out-Null
Start-Sleep -Milliseconds 200
Send-Click $menu 30 43
Start-Sleep -Milliseconds 1400

$popups = Get-Popups ([uint32]$proc.Id) $form
$dialogo = [IntPtr]::Zero
foreach ($w in $popups) {
    if ([CmdT]::Width($w) -gt 300) { $dialogo = $w; break }
}
if ($dialogo -eq [IntPtr]::Zero) { throw 'o dialogo de confirmacao nao abriu' }
$dw = [CmdT]::Width($dialogo); $dh = [CmdT]::Height($dialogo)
Write-Host "dialogo: $dialogo ($dw x $dh)"
Save-Window $dialogo (Join-Path $root "out\dialogo-$Tag.png")

# ---------------------------------------------------------------- 5. confirmar
# botao "Fechar"/"Terminate": canto inferior direito (112 x 30, margem 20)
if ($Cancel) {
    # botao "Cancelar"/"Cancel": a esquerda do de confirmar
    Send-Click $dialogo ($dw - 20 - 112 - 10 - 56) ($dh - 20 - 15)
    Write-Host 'clicou em Cancelar'
}
else {
    Send-Click $dialogo ($dw - 20 - 56) ($dh - 20 - 15)
}
Start-Sleep -Milliseconds 2000

# ---------------------------------------------------------------- 6. conferir
$vivo = Get-Process -Id $script.Id -ErrorAction SilentlyContinue
if ($Cancel) {
    if ($vivo) { Write-Host "OK: Cancelar NAO encerrou o script (PID $($script.Id) vivo)" -ForegroundColor Green }
    else { Write-Host "FALHA: Cancelar encerrou o script!" -ForegroundColor Red }
    foreach ($f in $filhos) {
        $p = Get-Process -Id $f -ErrorAction SilentlyContinue
        if ($p) { Write-Host "OK: filho PID $f continua vivo" -ForegroundColor Green }
        else { Write-Host "FALHA: Cancelar matou o filho PID $f" -ForegroundColor Red }
    }
}
else {
    if ($vivo) { Write-Host "FALHA: o script PID $($script.Id) continua vivo" -ForegroundColor Red }
    else { Write-Host "OK: script PID $($script.Id) encerrado" -ForegroundColor Green }

    foreach ($f in $filhos) {
        $p = Get-Process -Id $f -ErrorAction SilentlyContinue
        if ($p) { Write-Host "FALHA: o filho PID $f ($($p.ProcessName)) continua vivo" -ForegroundColor Red }
        else { Write-Host "OK: filho PID $f encerrado" -ForegroundColor Green }
    }
}

Save-Window $form (Join-Path $root "out\painel-$Tag.png")
Write-Host "fim do teste"
