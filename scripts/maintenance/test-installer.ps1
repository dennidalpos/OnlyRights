param(
    [Parameter(Mandatory=$true, Position=0)]
    [ValidateSet("Install", "Uninstall", "Upgrade")]
    [string]$Action,

    [string]$MsiPath,
    [string]$Configuration = "Release",
    [string]$Framework = "net8.0-windows",
    [string]$Runtime,
    [string]$Version,
    [string]$InstallRoot,
    [switch]$SkipBuild,

    # Upgrade parameters
    [string]$BaseVersion = "1.0.0",
    [string]$UpgradeVersion = "1.0.1"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "..\internal\installer.ps1")
. (Join-Path $PSScriptRoot "..\internal\windows-service.ps1")

$context = Get-MsiScriptContext -ScriptRoot $PSScriptRoot

switch ($Action) {
    "Install" {
        $resolvedMsiPath = if ($MsiPath) {
            Resolve-RepositoryRelativePath -RepoRoot $context.Repository.RepoRoot -Path $MsiPath
        }
        else {
            $outputRoot = Resolve-MsiOutputRoot -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -OutputRoot $null
            $versionValue = Resolve-MsiArtifactVersion -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $Version
            $architecture = Resolve-MsiArchitecture -Runtime $Runtime
            Join-Path $outputRoot ("{0}-{1}-{2}.msi" -f $context.InstallerName, $versionValue, $architecture)
        }

        if (-not $SkipBuild -and -not (Test-Path $resolvedMsiPath)) {
            & (Join-Path $PSScriptRoot "..\build\build-installer.ps1") -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $Version
        }

        if (-not (Test-Path $resolvedMsiPath)) {
            throw ("MSI not found: {0}" -f $resolvedMsiPath)
        }

        $resolvedInstallRoot = Resolve-MsiInstallRoot -Context $context -Runtime $Runtime -InstallRoot $InstallRoot

        $logRoot = Join-Path $context.Repository.ArtifactsRoot "logs"
        Ensure-Directory -Path $logRoot
        $logPath = Join-Path $logRoot "msi-install.log"

        $installFolderArgument = Format-MsiPropertyArgument -Name "INSTALLFOLDER" -Value $resolvedInstallRoot
        $exitCode = Invoke-Msiexec -Arguments @("/i", $resolvedMsiPath, "/qn", "/norestart", $installFolderArgument, "/l*v", $logPath)
        if ($exitCode -ne 0) {
            throw ("MSI install failed with exit code {0}. See {1}." -f $exitCode, $logPath)
        }

        $appExe = Join-Path (Join-Path $resolvedInstallRoot "App") "NtfsAudit.App.exe"
        if (-not (Test-Path $appExe)) {
            throw ("Installed app executable not found: {0}" -f $appExe)
        }

        Write-Host "[NtfsAudit] MSI install test completed." -ForegroundColor Cyan
        Write-Host ("  MSI: {0}" -f $resolvedMsiPath)
        Write-Host ("  Install root: {0}" -f $resolvedInstallRoot)
        Write-Host ("  Log: {0}" -f $logPath)
    }

    "Uninstall" {
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
    }

    "Upgrade" {
        & (Join-Path $PSScriptRoot "..\build\build-installer.ps1") -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $BaseVersion
        & (Join-Path $PSScriptRoot "..\build\build-installer.ps1") -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $UpgradeVersion
        
        $myPath = $MyInvocation.MyCommand.Path
        & $myPath -Action Install -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $BaseVersion -InstallRoot $InstallRoot -SkipBuild

        $resolvedInstallRoot = Resolve-MsiInstallRoot -Context $context -Runtime $Runtime -InstallRoot $InstallRoot

        $upgradeArchitecture = Resolve-MsiArchitecture -Runtime $Runtime
        $upgradeMsiPath = Join-Path (Resolve-MsiOutputRoot -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -OutputRoot $null) ("{0}-{1}-{2}.msi" -f $context.InstallerName, (Normalize-MsiVersion -Version $UpgradeVersion), $upgradeArchitecture)
        $logRoot = Join-Path $context.Repository.ArtifactsRoot "logs"
        Ensure-Directory -Path $logRoot
        $logPath = Join-Path $logRoot "msi-upgrade.log"

        $installFolderArgument = Format-MsiPropertyArgument -Name "INSTALLFOLDER" -Value $resolvedInstallRoot
        $exitCode = Invoke-Msiexec -Arguments @("/i", $upgradeMsiPath, "/qn", "/norestart", $installFolderArgument, "/l*v", $logPath)
        if ($exitCode -ne 0) {
            throw ("MSI upgrade failed with exit code {0}. See {1}." -f $exitCode, $logPath)
        }

        $appExe = Join-Path (Join-Path $resolvedInstallRoot "App") "NtfsAudit.App.exe"
        if (-not (Test-Path $appExe)) {
            throw ("Upgraded app executable not found: {0}" -f $appExe)
        }

        & $myPath -Action Uninstall -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $UpgradeVersion -InstallRoot $InstallRoot

        Write-Host "[NtfsAudit] MSI upgrade test completed." -ForegroundColor Cyan
        Write-Host ("  Base version: {0}" -f (Normalize-MsiVersion -Version $BaseVersion))
        Write-Host ("  Upgrade version: {0}" -f (Normalize-MsiVersion -Version $UpgradeVersion))
        Write-Host ("  Install root: {0}" -f $resolvedInstallRoot)
    }
}
