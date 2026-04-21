param(
    [string]$MsiPath,
    [string]$Configuration = "Release",
    [string]$Framework = "net8.0-windows",
    [string]$Runtime,
    [string]$Version,
    [string]$InstallRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "..\internal\installer.ps1")
. (Join-Path $PSScriptRoot "..\internal\windows-service.ps1")

$context = Get-MsiScriptContext -ScriptRoot $PSScriptRoot
$resolvedMsiPath = if ($MsiPath) {
    Resolve-RepositoryRelativePath -RepoRoot $context.Repository.RepoRoot -Path $MsiPath
}
else {
    $outputRoot = Resolve-MsiOutputRoot -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -OutputRoot $null
    $versionValue = Resolve-MsiArtifactVersion -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $Version
    $architecture = Resolve-MsiArchitecture -Runtime $Runtime
    Join-Path $outputRoot ("{0}-{1}-{2}.msi" -f $context.InstallerName, $versionValue, $architecture)
}

if (-not (Test-Path $resolvedMsiPath)) {
    throw ("MSI not found: {0}" -f $resolvedMsiPath)
}

$resolvedInstallRoot = Resolve-MsiInstallRoot -Context $context -Runtime $Runtime -InstallRoot $InstallRoot

$logRoot = Join-Path $context.Repository.ArtifactsRoot "logs"
Ensure-Directory -Path $logRoot
$logPath = Join-Path $logRoot "msi-uninstall.log"

$exitCode = Invoke-Msiexec -Arguments @("/x", $resolvedMsiPath, "/qn", "/norestart", "/l*v", $logPath)
if ($exitCode -ne 0) {
    throw ("MSI uninstall failed with exit code {0}. See {1}." -f $exitCode, $logPath)
}

$serviceContext = Get-ServiceScriptContext -ScriptRoot $PSScriptRoot
$serviceState = Get-ServiceState -ServiceName $serviceContext.ServiceName
if ($serviceState.IsInstalled) {
    throw ("MSI uninstall left service '{0}' installed." -f $serviceContext.ServiceName)
}

if (Test-Path $resolvedInstallRoot) {
    throw ("MSI uninstall left install root on disk: {0}" -f $resolvedInstallRoot)
}

Write-Host "[NtfsAudit] MSI uninstall test completed." -ForegroundColor Cyan
Write-Host ("  MSI: {0}" -f $resolvedMsiPath)
Write-Host ("  Install root: {0}" -f $resolvedInstallRoot)
Write-Host ("  Log: {0}" -f $logPath)
