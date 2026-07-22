$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Source = "$Root\windows\PiSwitch"
$OutDir = "$Root\dist\bin-windows"

Write-Host "Building PiSwitch for Windows..."

# Google Drive can lock files during build — use a local temp copy
$TempDir = "$env:TEMP\piswitch-build-$([System.IO.Path]::GetRandomFileName())"

try {
    Copy-Item -Recurse $Source $TempDir
    dotnet publish "$TempDir\PiSwitch.csproj" -c Release -o $TempDir\out
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }

    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
    Copy-Item "$TempDir\out\PiSwitch.exe" "$OutDir\PiSwitch.exe" -Force

    Write-Host ""
    Write-Host "Build complete"
    Write-Host "Binary:   $OutDir\PiSwitch.exe"
    Write-Host "Setup:    $Root\scripts\setup-windows.ps1"
} finally {
    if (Test-Path $TempDir) { Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue }
}
