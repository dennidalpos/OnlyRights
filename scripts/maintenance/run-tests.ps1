param(
    [string]$Configuration = "Release",
    [string]$Framework,
    [switch]$SkipRestore,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "..\internal\common.ps1")

$context = Get-RepositoryContext -ScriptRoot $PSScriptRoot
Assert-RepositoryPrerequisites -Context $context
Assert-SupportedFramework -Framework $Framework
Ensure-Directory -Path $context.TestResultsRoot

if (-not $SkipRestore) {
    Invoke-DotNetCommand -Arguments @("restore", $context.Solution, "--nologo") -ErrorMessage "Restore failed."
}

$testArgs = @("test", $context.Solution, "-c", $Configuration, "--nologo", "--results-directory", $context.TestResultsRoot)
if ($SkipRestore) {
    $testArgs += "--no-restore"
}
if ($SkipBuild) {
    $testArgs += "--no-build"
}
if ($Framework) {
    $testArgs += @("-f", $Framework)
}

Invoke-DotNetCommand -Arguments $testArgs -ErrorMessage "Tests failed."

Write-Host "[NtfsAudit] Test completed." -ForegroundColor Cyan
Write-Host ("  Configuration: {0}" -f $Configuration)
Write-Host ("  Test results: {0}" -f $context.TestResultsRoot)
