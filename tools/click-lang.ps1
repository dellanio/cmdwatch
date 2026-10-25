# Envia um clique real no seletor de idioma do painel (teste ponta a ponta).
#   .\tools\click-lang.ps1
#
# Enumera as janelas filhas do painel, localiza o controle NeonToggle pelo
# tamanho e dispara WM_LBUTTONDOWN/WM_LBUTTONUP com coordenadas de cliente.

param(
    [string]$Segment = 'EN'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class CmdClick {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);

    public static List<IntPtr> Children(IntPtr parent) {
        List<IntPtr> list = new List<IntPtr>();
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }

    public static string ClassOf(IntPtr h) {
        StringBuilder sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static int Width(IntPtr h) { RECT r; GetClientRect(h, out r); return r.Right - r.Left; }
    public static int Height(IntPtr h) { RECT r; GetClientRect(h, out r); return r.Bottom - r.Top; }
}
"@

$proc = Get-Process cmdwatch -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if ($proc -eq $null) { throw 'painel do cmdwatch nao esta em execucao' }

$form = $proc.MainWindowHandle
$kids = [CmdClick]::Children($form)

Write-Host "janelas filhas: $($kids.Count)"
foreach ($k in $kids) {
    Write-Host ("  {0}  {1}x{2}  {3}" -f [CmdClick]::ClassOf($k), [CmdClick]::Width($k), [CmdClick]::Height($k), $k)
}

# o seletor de idioma tem 150x24 (definido em DashboardForm.LayoutDashboard)
$toggle = $null
foreach ($k in $kids) {
    if ([CmdClick]::Width($k) -eq 150 -and [CmdClick]::Height($k) -eq 24) { $toggle = $k; break }
}
if ($toggle -eq [IntPtr]::Zero) { throw 'controle do seletor de idioma nao encontrado' }

Write-Host "seletor: $toggle"

# o caption "IDIOMA"/"LANGUAGE" ocupa a esquerda; o clique vai no centro do
# segmento pedido (PT-BR = primeiro, EN = segundo)
$cfg = Join-Path $env:APPDATA 'cmdwatch\ui.cfg'
$caption = 'IDIOMA'
if ((Test-Path $cfg) -and ((Get-Content $cfg -Raw) -match 'lang=en')) { $caption = 'LANGUAGE' }

$font = New-Object System.Drawing.Font 'Segoe UI', 8.75
$bmp = New-Object System.Drawing.Bitmap 1, 1
$g = [System.Drawing.Graphics]::FromImage($bmp)
$capW = [System.Windows.Forms.TextRenderer]::MeasureText($g, $caption, $font).Width + 10
$g.Dispose(); $bmp.Dispose(); $font.Dispose()

$segW = [int]((150 - $capW - 2) / 2)
$x = if ($Segment -eq 'EN') { $capW + $segW + [int]($segW / 2) } else { $capW + [int]($segW / 2) }
$y = 12

Write-Host "caption '$caption' -> $capW px; segmentos de $segW px"

[CmdClick]::SetForegroundWindow($form) | Out-Null
Start-Sleep -Milliseconds 200

$lParam = [IntPtr](($y -shl 16) -bor ($x -band 0xFFFF))
[CmdClick]::PostMessage($toggle, 0x0201, [IntPtr]1, $lParam) | Out-Null   # WM_LBUTTONDOWN
Start-Sleep -Milliseconds 120
[CmdClick]::PostMessage($toggle, 0x0202, [IntPtr]0, $lParam) | Out-Null   # WM_LBUTTONUP

Write-Host "clique enviado em '$Segment' (x=$x, y=$y)"
