$ErrorActionPreference = "Continue"
Add-Type -AssemblyName System.Windows.Forms
$base = Join-Path $env:TEMP "cmdw-tests"
$exe  = "D:\projetos-pessoal\CMDWatcher\out\cmdwatch.exe"
$logs = Join-Path $base "logs"
New-Item -ItemType Directory -Force -Path (Join-Path $base "repro"), $logs | Out-Null
Get-Process cmdwatch -EA SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "=== SEARCH script_pai / git in old logs ==="
Get-ChildItem $logs -Filter *.jsonl | ForEach-Object {
  $name = $_.Name
  foreach ($line in [IO.File]::ReadLines($_.FullName)) {
    if ($line.Contains("script_pai") -or ($line.Contains("git.exe") -and $line.Contains("window_"))) {
      $n = [Math]::Min(360, $line.Length)
      Write-Host ("[{0}] {1}" -f $name, $line.Substring(0, $n))
    }
  }
}

Write-Host "=== SEARCH hide events ==="
Get-ChildItem $logs -Filter *.jsonl | ForEach-Object {
  $name = $_.Name
  foreach ($line in [IO.File]::ReadLines($_.FullName)) {
    if ($line.Contains("CMDW-HIDE") -or $line.Contains("HIDE-TEST") -or ($line.Contains("window_hide") -and $line.Contains("cmdw"))) {
      $n = [Math]::Min(300, $line.Length)
      Write-Host ("[{0}] {1}" -f $name, $line.Substring(0, $n))
    }
  }
}

Write-Host "=== sessionId samples after-d1d3d4 ==="
$pathSid = Join-Path $logs "after-d1d3d4.jsonl"
if (Test-Path $pathSid) {
  $count = 0
  foreach ($line in [IO.File]::ReadLines($pathSid)) {
    if ($line -match '"sessionId"\s*:\s*(\d+)') {
      Write-Host ("sessionId={0}" -f $Matches[1])
      $count++
      if ($count -ge 15) { break }
    }
  }
}

# d1 re-verify
$py = Join-Path $base "repro\script_pai.py"
$js = Join-Path $base "repro\launch_pai.js"
@(
'import subprocess, time',
'subprocess.run(["git", "--version"], capture_output=True)',
'time.sleep(0.3)'
) | Set-Content -Path $py -Encoding ASCII
@(
'const {spawn}=require("child_process");',
'const path=require("path");',
'const py=path.join(__dirname,"script_pai.py");',
'const child=spawn("python",[py],{detached:true,windowsHide:true,stdio:"ignore"});',
'child.unref();',
'setTimeout(()=>process.exit(0),500);'
) | Set-Content -Path $js -Encoding ASCII

$log = Join-Path $logs "verify-d1.jsonl"
Remove-Item $log -Force -EA SilentlyContinue
$mon = Start-Process -FilePath $exe -ArgumentList @("monitor","--log",$log) -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logs "verify-d1-out.txt") -RedirectStandardError (Join-Path $logs "verify-d1-err.txt")
Start-Sleep -Seconds 2
node $js
Start-Sleep -Seconds 5
Stop-Process -Id $mon.Id -Force -EA SilentlyContinue
Start-Sleep -Milliseconds 400
Write-Host "=== VERIFY d1 ==="
if (Test-Path $log) {
  foreach ($line in [IO.File]::ReadLines($log)) {
    if ($line.Contains("script_pai") -or $line.Contains("git.exe") -or ($line.Contains("window_open") -and $line.Contains('"chain"'))) {
      Write-Host $line.Substring(0, [Math]::Min(420, $line.Length))
      Write-Host "-----"
    }
  }
} else { Write-Host "NO LOG" }

# d3
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinApiHide {
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
}
"@
$log3 = Join-Path $logs "verify-d3.jsonl"
Remove-Item $log3 -Force -EA SilentlyContinue
$mon3 = Start-Process -FilePath $exe -ArgumentList @("monitor","--log",$log3) -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logs "verify-d3-out.txt") -RedirectStandardError (Join-Path $logs "verify-d3-err.txt")
Start-Sleep -Seconds 2
$f = New-Object System.Windows.Forms.Form
$f.Text = "CMDW-HIDE-TEST"
$f.Width = 320; $f.Height = 120
$f.Show()
[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Seconds 2
[WinApiHide]::ShowWindow($f.Handle, 0)
Start-Sleep -Seconds 6
[WinApiHide]::ShowWindow($f.Handle, 5)
Start-Sleep -Seconds 2
$f.Close(); $f.Dispose()
Start-Sleep -Seconds 1
Stop-Process -Id $mon3.Id -Force -EA SilentlyContinue
Write-Host "=== VERIFY d3 CMDW-HIDE-TEST ==="
if (Test-Path $log3) {
  foreach ($line in [IO.File]::ReadLines($log3)) {
    if ($line.Contains("CMDW-HIDE-TEST")) {
      if ($line -match '"type"\s*:\s*"([^"]+)"') {
        Write-Host ("type={0}" -f $Matches[1])
      }
    }
  }
}
