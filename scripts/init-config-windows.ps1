$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Examples = "$Root\config\examples"
$Instances = "$Root\config\instances"

New-Item -ItemType Directory -Path $Instances -Force | Out-Null

function Copy-IfMissing($Name, $SourceName) {
    if (-not $SourceName) { $SourceName = $Name }
    $src = "$Examples\$SourceName.json"
    $dst = "$Instances\$Name.json"

    if (-not (Test-Path $src)) {
        Write-Warning "Missing example: $src"
        return
    }
    if (Test-Path $dst) {
        Write-Host "Kept existing $dst"
    } else {
        Copy-Item $src $dst
        Write-Host "Created $dst"
    }
}

function Ensure-T3CodeFirst($Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }

    try {
        $config = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        $apps = @($config.apps) |
            ForEach-Object { [string]$_ } |
            Where-Object { $_ -and $_ -notlike 'Antigravity' -and $_ -notlike 'T3 Code' }
        $newApps = @('T3 Code') + $apps
        if ($newApps.Count -gt 8) { $newApps = $newApps[0..7] }

        $oldApps = @($config.apps) | ForEach-Object { [string]$_ }
        $changed = $oldApps.Count -ne $newApps.Count
        if (-not $changed) {
            for ($i = 0; $i -lt $newApps.Count; $i++) {
                if ($oldApps[$i] -cne $newApps[$i]) { $changed = $true; break }
            }
        }

        if ($changed) {
            $config.apps = $newApps
            $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Path -Encoding UTF8
            Write-Host "Migrated ${Path}: T3 Code is now app 1"
        }
    } catch {
        Write-Warning "Could not migrate ${Path}: $($_.Exception.Message)"
    }
}

# Promote the legacy filename before creating a new default. ConfigService still
# reads config.json for compatibility, but default.json wins when both exist; creating
# a fresh default first would silently hide the user's old menu instead of migrating it.
$DefaultPath = "$Instances\default.json"
$LegacyDefaultPath = "$Instances\config.json"
if (-not (Test-Path -LiteralPath $DefaultPath) -and (Test-Path -LiteralPath $LegacyDefaultPath)) {
    Copy-Item -LiteralPath $LegacyDefaultPath -Destination $DefaultPath
    Write-Host "Promoted legacy $LegacyDefaultPath to $DefaultPath"
}

# Use Windows-specific default if available, otherwise fall back to shared default
if (Test-Path "$Examples\default-windows.json") {
    Copy-IfMissing "default" "default-windows"
} else {
    Copy-IfMissing "default" "default"
}
Ensure-T3CodeFirst $DefaultPath
Copy-IfMissing "messaging" "messaging"
Copy-IfMissing "explorer-groups" "explorer-groups"
