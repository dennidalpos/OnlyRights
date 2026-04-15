param(
    [string]$Configuration = "Release",
    [string]$Framework = "net8.0-windows",
    [string]$Runtime,
    [string]$ServiceCommandPath,
    [switch]$SkipStart
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "..\helpers\windows-service.ps1")

$context = Get-ServiceScriptContext -ScriptRoot (Join-Path $PSScriptRoot "..")
$serviceCommand = Resolve-ServiceCommandPath -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -ServiceCommandPath $ServiceCommandPath
if (-not $serviceCommand) {
    throw "NtfsAudit.Service executable not found. Run scripts/pack.ps1 or scripts/build.ps1 first, or pass -ServiceCommandPath."
}

Install-WindowsService -Context $context -ServiceCommand $serviceCommand
if (-not $SkipStart) {
    Start-WindowsService -ServiceName $context.ServiceName
}

Write-Host "[NtfsAudit] Service install completed." -ForegroundColor Cyan
Write-Host ("  Service name: {0}" -f $context.ServiceName)
Write-Host ("  Command: {0}" -f $serviceCommand)
