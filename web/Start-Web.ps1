$ErrorActionPreference = "Continue"
$root = $PSScriptRoot
$exe = Join-Path $root "Locifab.Web.exe"
$html = Join-Path $root "Locifab.html"

if (-not (Test-Path $exe)) { Write-Host "Locifab.Web.exe not found"; Read-Host "Enter to exit"; exit 1 }
if (-not (Test-Path $html)) { Write-Host "Locifab.html not found"; Read-Host "Enter to exit"; exit 1 }

# Start backend with redirected streams; this keeps it alive in headless mode.
Start-Process -FilePath $exe `
    -ArgumentList "--headless", "--port", "9500", "--fixed-token", "LOCIFAB" `
    -WorkingDirectory $root -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $root "backend.log") `
    -RedirectStandardError (Join-Path $root "backend.err.log")

Start-Sleep -Seconds 2

# Start file-state bridge (timestamps / attributes).
Start-Process -FilePath "powershell.exe" `
    -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $root "FileBridge.ps1") `
    -WorkingDirectory $root -WindowStyle Hidden

Start-Sleep -Seconds 1
Start-Process $html
Write-Host "Locifab web started. Keep this window open, or use Stop-Web.ps1 to stop services."
Start-Sleep -Seconds 4
