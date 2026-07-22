$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$DistExe = "$Root\dist\bin-windows\PiSwitch.exe"

if (-not (Test-Path $DistExe)) {
    Write-Error "PiSwitch.exe not found. Run build-windows.ps1 first."
    exit 1
}

# 1. Initialize config
Write-Host "Initializing config..."
& "$Root\scripts\init-config-windows.ps1"
Write-Host ""

# 2. Install to a stable local path (avoids cloud-sync filesystem issues)
$InstallDir = "$env:LOCALAPPDATA\PiSwitch"
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item $DistExe "$InstallDir\PiSwitch.exe" -Force
Write-Host "Installed to $InstallDir\PiSwitch.exe"

# 3. Create startup shortcut
Write-Host "Setting up auto-start..."
$startupDir = [Environment]::GetFolderPath('Startup')
$ws = New-Object -ComObject WScript.Shell
$shortcut = $ws.CreateShortcut("$startupDir\PiSwitch.lnk")
$shortcut.TargetPath = "$InstallDir\PiSwitch.exe"
$shortcut.WorkingDirectory = $InstallDir
$shortcut.WindowStyle = 7
$shortcut.Description = "PiSwitch pie menu daemon"
# Set PISWITCH_HOME so the exe finds config in the repo
$shortcut.Arguments = ""
$shortcut.Save()
Write-Host "Created startup shortcut: $startupDir\PiSwitch.lnk"

# Write a small config pointing to the repo home
Set-Content "$InstallDir\piswitch-home.txt" $Root
Write-Host ""

# 4. Set PISWITCH_HOME as a user environment variable (persists across reboots)
[Environment]::SetEnvironmentVariable("PISWITCH_HOME", $Root, "User")
Write-Host "Set PISWITCH_HOME=$Root (user environment variable)"
Write-Host ""

# 5. Start the daemon now
Write-Host "Starting PiSwitch..."
$running = Get-Process -Name PiSwitch -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process -Force
    Start-Sleep -Seconds 2
}

$env:PISWITCH_HOME = $Root
Start-Process -FilePath "$InstallDir\PiSwitch.exe" -WindowStyle Hidden
Start-Sleep -Seconds 3
$proc = Get-Process -Name PiSwitch -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host "PiSwitch started (pid=$($proc.Id))"
} else {
    Write-Warning "PiSwitch may not have started. Check $Root\run\piswitch-bootstrap.log"
}

Write-Host ""
Write-Host "Setup complete!"
Write-Host "  Config:   $Root\config\instances\default.json"
Write-Host "  Binary:   $InstallDir\PiSwitch.exe"
Write-Host "  Startup:  $startupDir\PiSwitch.lnk"
Write-Host ""
Write-Host "Edit the config to choose your apps, colors, and labels."
Write-Host "PiSwitch will start automatically on login."
Write-Host ""
Write-Host "To trigger the menu, use one of:"
Write-Host "  - System tray icon (right-click -> Show Menu)"
Write-Host "  - AutoHotkey: see examples\autohotkey\hyper-piswitch.ahk"
Write-Host "  - Re-run PiSwitch.exe (triggers existing daemon)"
