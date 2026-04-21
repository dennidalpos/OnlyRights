param(
    [switch]$SkipBuild,
    [switch]$RequireOptionalTools
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptsRoot = Split-Path $PSScriptRoot -Parent
$installDependenciesScript = Join-Path $scriptsRoot "install-dependencies.ps1"
$doctorScript = Join-Path $scriptsRoot "maintenance\check-prerequisites.ps1"
$buildScript = Join-Path $scriptsRoot "build.ps1"

Write-Warning "scripts\\legacy\\setup.ps1 is retained for compatibility. Prefer the documented root scripts under scripts\\."
Write-Host "[NtfsAudit] Legacy setup" -ForegroundColor Cyan
Write-Host "  Step 1/3: bootstrap"
& $installDependenciesScript

Write-Host "  Step 2/3: doctor"
if ($RequireOptionalTools) {
    & $doctorScript -RequireOptionalTools
}
else {
    & $doctorScript
}

if ($SkipBuild) {
    Write-Host "  Step 3/3: build skipped"
}
else {
    Write-Host "  Step 3/3: build"
    & $buildScript
}

Write-Host "[NtfsAudit] Legacy setup completed." -ForegroundColor Cyan
Write-Host ("  Build executed: {0}" -f (-not $SkipBuild))
