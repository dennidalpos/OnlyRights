param(
    [switch]$UseNssmFallback,
    [switch]$SkipResidualCleanup
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "..\helpers\windows-service.ps1")

$context = Get-ServiceScriptContext -ScriptRoot (Join-Path $PSScriptRoot "..")
Uninstall-WindowsService -Context $context -UseNssmFallback:$UseNssmFallback
if (-not $SkipResidualCleanup) {
    Remove-ServiceResiduals
}

Write-Host "[NtfsAudit] Service uninstall completed." -ForegroundColor Cyan
Write-Host ("  Service name: {0}" -f $context.ServiceName)
Write-Host ("  Residual cleanup skipped: {0}" -f $SkipResidualCleanup.IsPresent)
