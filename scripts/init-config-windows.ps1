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

# Use Windows-specific default if available, otherwise fall back to shared default
if (Test-Path "$Examples\default-windows.json") {
    Copy-IfMissing "default" "default-windows"
} else {
    Copy-IfMissing "default" "default"
}
Copy-IfMissing "messaging" "messaging"
Copy-IfMissing "explorer-groups" "explorer-groups"
