param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "..\helpers\windows-service.ps1")

$context = Get-ServiceScriptContext -ScriptRoot (Join-Path $PSScriptRoot "..")
try {
    Uninstall-WindowsService -Context $context
}
catch {
    Write-Warning $_.Exception.Message
}

Remove-ServiceResiduals

Write-Host "[NtfsAudit] Service cleanup completed." -ForegroundColor Cyan
Write-Host ("  Service name: {0}" -f $context.ServiceName)
