# capture.ps1 - captura a janela do painel do cmdwatch em PNG.
#
#   .\tools\capture.ps1 -Out docs\dashboard.png
#
# Usa PrintWindow (flag 2 = PW_RENDERFULLCONTENT): renderiza a janela direto
# no DC, sem roubar o foco e sem capturar outras janelas da area de trabalho.

param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'out\cmdwatch.exe'),
    [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'out\dashboard.png'),
    [int]$WaitSeconds = 3,
    [switch]$Restart
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class CmdWin {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@

if (-not (Test-Path $Exe)) { throw "exe nao encontrado: $Exe" }

# usa o painel ja em execucao (preserva as sessoes ja observadas); so
# reinicia a aplicacao quando pedido ou quando nao ha nenhuma rodando
$proc = $null
if (-not $Restart) {
    $proc = Get-Process cmdwatch -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
}
if ($proc -eq $null) {
    Get-Process cmdwatch -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
    $proc = Start-Process -FilePath $Exe -PassThru
}

$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 250
    $proc.Refresh()
    if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $proc.MainWindowHandle; break }
}
if ($hwnd -eq [IntPtr]::Zero) { throw 'janela do painel nao apareceu' }

Start-Sleep -Seconds $WaitSeconds

# posiciona a janela em um tamanho estavel (a captura fica reproduzivel)
[CmdWin]::ShowWindow($hwnd, 9) | Out-Null
[CmdWin]::SetWindowPos($hwnd, [IntPtr]::Zero, 40, 20, 1440, 880, 0x0040) | Out-Null
Start-Sleep -Milliseconds 700

$r = New-Object CmdWin+RECT
[CmdWin]::GetWindowRect($hwnd, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top
if ($w -le 0 -or $h -le 0) { $proc.Kill(); throw 'janela sem dimensoes' }

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[CmdWin]::PrintWindow($hwnd, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc)
$g.Dispose()

$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Write-Host ("captura: " + $Out + " (" + $w + "x" + $h + ")")
