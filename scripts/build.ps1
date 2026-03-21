param(
    [string]$Configuration = "Release",
    [string]$Framework,
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$compileScript = Join-Path $PSScriptRoot "compile.ps1"
if ($Framework) {
    if ($SkipRestore) {
        & $compileScript -Configuration $Configuration -Framework $Framework -SkipRestore
    }
    else {
        & $compileScript -Configuration $Configuration -Framework $Framework
    }
}
elseif ($SkipRestore) {
    & $compileScript -Configuration $Configuration -SkipRestore
}
else {
    & $compileScript -Configuration $Configuration
}

Write-Host "[NtfsAudit] Build completed." -ForegroundColor Cyan
