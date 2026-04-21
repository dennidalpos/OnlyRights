param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "..\internal\windows-service.ps1")

$context = Get-ServiceScriptContext -ScriptRoot $PSScriptRoot
Stop-WindowsService -ServiceName $context.ServiceName

Write-Host "[NtfsAudit] Service stop completed." -ForegroundColor Cyan
Write-Host ("  Service name: {0}" -f $context.ServiceName)
