param(
    [string]$BaseVersion = "1.0.0",
    [string]$UpgradeVersion = "1.0.1",
    [string]$Configuration = "Release",
    [string]$Framework = "net8.0-windows",
    [string]$Runtime,
    [string]$InstallRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "..\helpers\msi.ps1")

$context = Get-MsiScriptContext -ScriptRoot (Join-Path $PSScriptRoot "..")

& (Join-Path $PSScriptRoot "msi-build.ps1") -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $BaseVersion
& (Join-Path $PSScriptRoot "msi-build.ps1") -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $UpgradeVersion
& (Join-Path $PSScriptRoot "msi-install-test.ps1") -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $BaseVersion -InstallRoot $InstallRoot -SkipBuild

$resolvedInstallRoot = Resolve-MsiInstallRoot -Context $context -Runtime $Runtime -InstallRoot $InstallRoot

$upgradeArchitecture = Resolve-MsiArchitecture -Runtime $Runtime
$upgradeMsiPath = Join-Path (Resolve-MsiOutputRoot -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -OutputRoot $null) ("{0}-{1}-{2}.msi" -f $context.InstallerName, (Normalize-MsiVersion -Version $UpgradeVersion), $upgradeArchitecture)
$logRoot = Join-Path $context.Repository.ArtifactsRoot "logs"
Ensure-Directory -Path $logRoot
$logPath = Join-Path $logRoot "msi-upgrade.log"

$exitCode = Invoke-Msiexec -Arguments @("/i", $upgradeMsiPath, "/qn", "/norestart", "INSTALLFOLDER=$resolvedInstallRoot", "/l*v", $logPath)
if ($exitCode -ne 0) {
    throw ("MSI upgrade failed with exit code {0}. See {1}." -f $exitCode, $logPath)
}

$appExe = Join-Path (Join-Path $resolvedInstallRoot "App") "NtfsAudit.App.exe"
if (-not (Test-Path $appExe)) {
    throw ("Upgraded app executable not found: {0}" -f $appExe)
}

& (Join-Path $PSScriptRoot "msi-uninstall-test.ps1") -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $UpgradeVersion -InstallRoot $InstallRoot

Write-Host "[NtfsAudit] MSI upgrade test completed." -ForegroundColor Cyan
Write-Host ("  Base version: {0}" -f (Normalize-MsiVersion -Version $BaseVersion))
Write-Host ("  Upgrade version: {0}" -f (Normalize-MsiVersion -Version $UpgradeVersion))
Write-Host ("  Install root: {0}" -f $resolvedInstallRoot)
