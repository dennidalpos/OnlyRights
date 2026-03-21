param(
    [string]$Configuration = "Release",
    [string]$Framework,
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "helpers\common.ps1")

$context = Get-RepositoryContext -ScriptRoot $PSScriptRoot
Assert-RepositoryPrerequisites -Context $context

if (-not $SkipRestore) {
    Invoke-DotNetCommand -Arguments @("restore", $context.Solution, "--nologo") -ErrorMessage "Restore failed."
}

$buildArgs = @("build", $context.Solution, "-c", $Configuration, "--nologo", "--no-restore")
if ($Framework) {
    $buildArgs += @("-f", $Framework)
}

Invoke-DotNetCommand -Arguments $buildArgs -ErrorMessage "Compile failed."

Write-Host "[NtfsAudit] Compile completed." -ForegroundColor Cyan
Write-Host ("  Configuration: {0}" -f $Configuration)
Write-Host ("  Build outputs: {0}" -f $context.BuildRoot)
