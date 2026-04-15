param(
    [string]$Configuration = "Release",
    [string]$Framework,
    [string]$Runtime,
    [string]$PlatformTarget,
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$compileScript = Join-Path $PSScriptRoot "compile.ps1"
$compileArgs = @{
    Configuration = $Configuration
}
if ($Framework) {
    $compileArgs.Framework = $Framework
}
if ($Runtime) {
    $compileArgs.Runtime = $Runtime
}
if ($PlatformTarget) {
    $compileArgs.PlatformTarget = $PlatformTarget
}
if ($SkipRestore) {
    $compileArgs.SkipRestore = $true
}

& $compileScript @compileArgs

Write-Host "[NtfsAudit] Build completed." -ForegroundColor Cyan
