param(
    [string]$Configuration = "Release",
    [string]$Framework,
    [switch]$SkipBuild,
    [switch]$RequireOptionalTools
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$bootstrapScript = Join-Path $PSScriptRoot "bootstrap.ps1"
$doctorScript = Join-Path $PSScriptRoot "doctor.ps1"
$buildScript = Join-Path $PSScriptRoot "build.ps1"

Write-Host "[NtfsAudit] Initial setup" -ForegroundColor Cyan
Write-Host "  Step 1/3: bootstrap"
& $bootstrapScript

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
    $buildParams = @{
        Configuration = $Configuration
        SkipRestore = $true
    }

    if ($Framework) {
        $buildParams.Framework = $Framework
    }

    & $buildScript @buildParams
}

Write-Host "[NtfsAudit] Initial setup completed." -ForegroundColor Cyan
Write-Host ("  Configuration: {0}" -f $Configuration)
if ($Framework) {
    Write-Host ("  Framework: {0}" -f $Framework)
}
Write-Host ("  Build executed: {0}" -f (-not $SkipBuild))
