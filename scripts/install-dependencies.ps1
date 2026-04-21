param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "internal\common.ps1")

$context = Get-RepositoryContext -ScriptRoot $PSScriptRoot
Assert-RepositoryPrerequisites -Context $context

Invoke-DotNetCommand -Arguments @("restore", $context.Solution, "--nologo") -ErrorMessage "Bootstrap failed."

Write-Host "[NtfsAudit] Dependency restore completed." -ForegroundColor Cyan
Write-Host ("  Solution: {0}" -f $context.Solution)
