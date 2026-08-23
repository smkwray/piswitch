$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot

function Assert-True($Condition, $Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
}

$examplePath = Join-Path $Root "config\examples\default-windows.json"
$example = Get-Content -LiteralPath $examplePath -Raw | ConvertFrom-Json
Assert-True (@($example.apps)[0] -eq "T3 Code") "default Windows config must put T3 Code in slot 1"
Assert-True (-not (@($example.apps) -contains "Antigravity")) "default Windows config must not retain Antigravity"

$launcher = Get-Content -LiteralPath (Join-Path $Root "windows\PiSwitch\Services\AppLauncher.cs") -Raw
Assert-True ($launcher.Contains('StartsWith("T3 Code", StringComparison.OrdinalIgnoreCase)')) "launcher must match the T3 Code process family"
Assert-True ($launcher.Contains("FindT3CodeExecutable")) "launcher must have a cold-launch fallback"
Assert-True ($launcher.Contains("ScoreT3CodeVariant")) "launcher must select a deterministic T3 variant"

$appSource = Get-Content -LiteralPath (Join-Path $Root "windows\PiSwitch\App.xaml.cs") -Raw
Assert-True ($appSource.Contains('args[i] == "--start-only"')) "daemon must have an idempotent startup mode"
Assert-True ($appSource.Contains('var localConfigDir = Path.Combine(exeDir, "config", "instances");')) "daemon must resolve the installed fallback config"
Assert-True ($appSource.Contains('if (Directory.Exists(localConfigDir))')) "daemon fallback must require a real local config directory"

$setup = Get-Content -LiteralPath (Join-Path $Root "scripts\setup-windows.ps1") -Raw
Assert-True ($setup.Contains("piswitch-home.txt")) "setup must persist the project home"
Assert-True ($setup.Contains("Synchronized fallback config")) "setup must refresh the local fallback config"
Assert-True ($setup.Contains('$shortcut.Arguments = "--start-only"')) "startup shortcut must not re-open an existing menu"

# Exercise the real config migration against an isolated old-style config.
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("piswitch-config-test-" + [IO.Path]::GetRandomFileName())
try {
    New-Item -ItemType Directory -Path "$tempRoot\scripts", "$tempRoot\config\examples", "$tempRoot\config\instances" -Force | Out-Null
    Copy-Item (Join-Path $Root "scripts\init-config-windows.ps1") "$tempRoot\scripts\init-config-windows.ps1"
    Copy-Item $examplePath "$tempRoot\config\examples\default-windows.json"
    Copy-Item (Join-Path $Root "config\examples\messaging.json") "$tempRoot\config\examples\messaging.json"
    Copy-Item (Join-Path $Root "config\examples\explorer-groups.json") "$tempRoot\config\examples\explorer-groups.json"
    Set-Content -LiteralPath "$tempRoot\config\instances\default.json" -Value @'
{
  "apps": ["Antigravity", "ChatGPT", "Claude"],
  "paths": {}
}
'@

    & "$tempRoot\scripts\init-config-windows.ps1" | Out-Null
    $migrated = Get-Content -LiteralPath "$tempRoot\config\instances\default.json" -Raw | ConvertFrom-Json
    Assert-True (@($migrated.apps)[0] -eq "T3 Code") "existing config migration must move T3 Code to slot 1"
    Assert-True (-not (@($migrated.apps) -contains "Antigravity")) "existing config migration must remove Antigravity"
} finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}


# Exercise the legacy config.json filename. Creating a fresh default.json first
# would shadow this file because ConfigService prefers default.json.
$legacyRoot = Join-Path ([IO.Path]::GetTempPath()) ("piswitch-legacy-config-test-" + [IO.Path]::GetRandomFileName())
try {
    New-Item -ItemType Directory -Path "$legacyRoot\scripts", "$legacyRoot\config\examples", "$legacyRoot\config\instances" -Force | Out-Null
    Copy-Item (Join-Path $Root "scripts\init-config-windows.ps1") "$legacyRoot\scripts\init-config-windows.ps1"
    Copy-Item $examplePath "$legacyRoot\config\examples\default-windows.json"
    Copy-Item (Join-Path $Root "config\examples\messaging.json") "$legacyRoot\config\examples\messaging.json"
    Copy-Item (Join-Path $Root "config\examples\explorer-groups.json") "$legacyRoot\config\examples\explorer-groups.json"
    Set-Content -LiteralPath "$legacyRoot\config\instances\config.json" -Value @'
{
  "apps": ["Antigravity", "ChatGPT", "Claude"],
  "labels": {"ChatGPT": "My Chat"}
}
'@

    & "$legacyRoot\scripts\init-config-windows.ps1" | Out-Null
    Assert-True (Test-Path -LiteralPath "$legacyRoot\config\instances\default.json") "legacy config.json must be promoted to default.json"
    $legacyMigrated = Get-Content -LiteralPath "$legacyRoot\config\instances\default.json" -Raw | ConvertFrom-Json
    Assert-True (@($legacyMigrated.apps)[0] -eq "T3 Code") "legacy config migration must put T3 Code in slot 1"
    Assert-True (-not (@($legacyMigrated.apps) -contains "Antigravity")) "legacy config migration must remove Antigravity"
    Assert-True ($legacyMigrated.labels.ChatGPT -eq "My Chat") "legacy config migration must preserve custom fields"
} finally {
    if (Test-Path -LiteralPath $legacyRoot) {
        Remove-Item -LiteralPath $legacyRoot -Recurse -Force
    }
}

Write-Host "Windows PiSwitch focused checks passed."
