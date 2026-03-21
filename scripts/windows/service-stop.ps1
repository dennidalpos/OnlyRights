param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "..\helpers\windows-service.ps1")

$context = Get-ServiceScriptContext -ScriptRoot (Join-Path $PSScriptRoot "..")
Stop-WindowsService -ServiceName $context.ServiceName

Write-Host "[NtfsAudit] Service stop completed." -ForegroundColor Cyan
Write-Host ("  Service name: {0}" -f $context.ServiceName)
