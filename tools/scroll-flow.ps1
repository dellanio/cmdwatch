# Rola o fluxo do painel para cima e captura a tela (para conferir as linhas de chamada).
#   .\tools\scroll-flow.ps1 -Out out\shot-chamadas.png -Notches 40

param(
    [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'out\shot-chamadas.png'),
    [int]$Notches = 40
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class CmdScroll {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);

    public static List<IntPtr> Children(IntPtr parent) {
        List<IntPtr> list = new List<IntPtr>();
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
    public static int Width(IntPtr h) { RECT r; GetClientRect(h, out r); return r.Right - r.Left; }
    public static int Height(IntPtr h) { RECT r; GetClientRect(h, out r); return r.Bottom - r.Top; }
}
"@

$proc = Get-Process cmdwatch -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if ($proc -eq $null) { throw 'painel do cmdwatch nao esta em execucao' }
$form = $proc.MainWindowHandle

# o fluxo e o controle filho mais alto (o painel de sessoes fica com ~1464x220)
$flow = $null
foreach ($k in [CmdScroll]::Children($form)) {
    $w = [CmdScroll]::Width($k); $h = [CmdScroll]::Height($k)
    if ($h -gt 300 -and $w -gt 600) { $flow = $k }
}
if ($flow -eq $null) { throw 'controle do fluxo nao encontrado' }

# WM_MOUSEWHEEL: delta positivo = rola para cima (3 = 120px por entalhe)
for ($i = 0; $i -lt $Notches; $i++) {
    $wParam = [IntPtr](120 -shl 16)
    [CmdScroll]::PostMessage($flow, 0x020A, $wParam, [IntPtr]0x00090000) | Out-Null
    Start-Sleep -Milliseconds 30
}
Start-Sleep -Milliseconds 600

[CmdScroll]::ShowWindow($form, 9) | Out-Null
[CmdScroll]::SetWindowPos($form, [IntPtr]::Zero, 40, 20, 1440, 880, 0x0040) | Out-Null
Start-Sleep -Milliseconds 700

$r = New-Object CmdScroll+RECT
[CmdScroll]::GetWindowRect($form, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[CmdScroll]::PrintWindow($form, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host ("captura: " + $Out)
