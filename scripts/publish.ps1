param(
    [string]$Configuration = "Release",
    [string]$Framework = "net8.0-windows",
    [string]$Runtime,
    [string]$PackageRoot,
    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "helpers\common.ps1")

$context = Get-RepositoryContext -ScriptRoot $PSScriptRoot
Assert-RepositoryPrerequisites -Context $context
Assert-SupportedWindowsRuntime -Runtime $Runtime

$sourceRoot = if ($PackageRoot) {
    if ([System.IO.Path]::IsPathRooted($PackageRoot)) { $PackageRoot } else { Join-Path $context.RepoRoot $PackageRoot }
} else {
    Resolve-StagedOutputRoot -BaseRoot $context.PackagesRoot -Configuration $Configuration -Runtime $Runtime -Framework $Framework
}

if (-not (Test-Path $sourceRoot)) {
    throw ("Package root not found: {0}. Run scripts/pack.ps1 first." -f $sourceRoot)
}

$publishRoot = if ($OutputRoot) {
    if ([System.IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot } else { Join-Path $context.RepoRoot $OutputRoot }
} else {
    Resolve-StagedOutputRoot -BaseRoot $context.PublishRoot -Configuration $Configuration -Runtime $Runtime -Framework $Framework
}

Remove-DirectoryIfExists -Path $publishRoot
Ensure-Directory -Path $publishRoot

Copy-Item -Path (Join-Path $sourceRoot "*") -Destination $publishRoot -Recurse -Force

Write-Host "[NtfsAudit] Publish completed." -ForegroundColor Cyan
Write-Host ("  Source packages: {0}" -f $sourceRoot)
Write-Host ("  Publish output: {0}" -f $publishRoot)
