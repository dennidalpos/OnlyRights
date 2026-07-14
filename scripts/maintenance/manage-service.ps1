param(
    [Parameter(Mandatory=$true, Position=0)]
    [ValidateSet("Install", "Uninstall", "Start", "Stop", "Cleanup")]
    [string]$Action,

    [string]$Configuration = "Release",
    [string]$Framework = "net8.0-windows",
    [string]$Runtime,
    [string]$ServiceCommandPath,
    [switch]$SkipStart,
    [switch]$SkipResidualCleanup
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "..\internal\windows-service.ps1")
$context = Get-ServiceScriptContext -ScriptRoot $PSScriptRoot

switch ($Action) {
    "Install" {
        $serviceCommand = Resolve-ServiceCommandPath -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -ServiceCommandPath $ServiceCommandPath
        if (-not $serviceCommand) {
            throw "NtfsAudit.Service executable not found. Run scripts\build\stage-package-layout.ps1 or scripts\build.ps1 first, or pass -ServiceCommandPath."
        }
        Install-WindowsService -Context $context -ServiceCommand $serviceCommand
        if (-not $SkipStart) {
            Start-WindowsService -ServiceName $context.ServiceName
        }
        Write-Host "[NtfsAudit] Service install completed." -ForegroundColor Cyan
        Write-Host ("  Service name: {0}" -f $context.ServiceName)
        Write-Host ("  Command: {0}" -f $serviceCommand)
    }
    "Uninstall" {
        Uninstall-WindowsService -Context $context
        if (-not $SkipResidualCleanup) {
            Remove-ServiceResiduals
        }
        Write-Host "[NtfsAudit] Service uninstall completed." -ForegroundColor Cyan
        Write-Host ("  Service name: {0}" -f $context.ServiceName)
        Write-Host ("  Residual cleanup skipped: {0}" -f $SkipResidualCleanup.IsPresent)
    }
    "Start" {
        Start-WindowsService -ServiceName $context.ServiceName
        Write-Host "[NtfsAudit] Service start completed." -ForegroundColor Cyan
        Write-Host ("  Service name: {0}" -f $context.ServiceName)
    }
    "Stop" {
        Stop-WindowsService -ServiceName $context.ServiceName
        Write-Host "[NtfsAudit] Service stop completed." -ForegroundColor Cyan
        Write-Host ("  Service name: {0}" -f $context.ServiceName)
    }
    "Cleanup" {
        Remove-ServiceResiduals
        Write-Host "[NtfsAudit] Service residual cleanup completed." -ForegroundColor Cyan
    }
}
