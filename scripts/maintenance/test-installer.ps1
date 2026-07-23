param(
    [Parameter(Mandatory=$true, Position=0)]
    [ValidateSet("Install", "Uninstall", "Upgrade")]
    [string]$Action,

    [string]$InstallerPath,
    [string]$Configuration = "Release",
    [string]$Framework = "net8.0-windows",
    [string]$Runtime = "win-x64",
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

$context = Get-NsisScriptContext -ScriptRoot $PSScriptRoot

switch ($Action) {
    "Install" {
        $resolvedInstallerPath = if ($InstallerPath) {
            Resolve-RepositoryRelativePath -RepoRoot $context.Repository.RepoRoot -Path $InstallerPath
        }
        else {
            $outputRoot = Resolve-InstallerOutputRoot -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -OutputRoot $null
            $versionValue = Resolve-NsisArtifactVersion -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $Version
            $architecture = Resolve-NsisArchitecture -Runtime $Runtime
            Join-Path $outputRoot ("{0}-{1}-{2}.exe" -f $context.InstallerName, $versionValue, $architecture)
        }

        if (-not $SkipBuild -and -not (Test-Path $resolvedInstallerPath)) {
            & (Join-Path $PSScriptRoot "..\build\build-installer.ps1") -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $Version
        }

        if (-not (Test-Path $resolvedInstallerPath)) {
            throw ("NSIS Installer not found: {0}" -f $resolvedInstallerPath)
        }

        $resolvedInstallRoot = Resolve-NsisInstallRoot -Context $context -Runtime $Runtime -InstallRoot $InstallRoot

        $installArgs = @("/S")
        if ($InstallRoot) {
            $installArgs += ("/D={0}" -f $resolvedInstallRoot)
        }

        $process = Start-Process -FilePath $resolvedInstallerPath -ArgumentList $installArgs -Wait -PassThru -NoNewWindow
        if ($process.ExitCode -ne 0) {
            throw ("NSIS install failed with exit code {0}." -f $process.ExitCode)
        }

        $appExe = Join-Path (Join-Path $resolvedInstallRoot "App") "NtfsAudit.App.exe"
        if (-not (Test-Path $appExe)) {
            throw ("Installed app executable not found: {0}" -f $appExe)
        }

        Write-Host "[NtfsAudit] NSIS install test completed." -ForegroundColor Cyan
        Write-Host ("  Installer: {0}" -f $resolvedInstallerPath)
        Write-Host ("  Install root: {0}" -f $resolvedInstallRoot)
    }

    "Uninstall" {
        $resolvedInstallRoot = Resolve-NsisInstallRoot -Context $context -Runtime $Runtime -InstallRoot $InstallRoot
        $uninstaller = Join-Path $resolvedInstallRoot "Uninstall.exe"

        if (Test-Path $uninstaller) {
            $process = Start-Process -FilePath $uninstaller -ArgumentList "/S" -Wait -PassThru -NoNewWindow
            if ($process.ExitCode -ne 0) {
                throw ("NSIS uninstall failed with exit code {0}." -f $process.ExitCode)
            }
        }

        $serviceContext = Get-ServiceScriptContext -ScriptRoot $PSScriptRoot
        $serviceState = Get-ServiceState -ServiceName $serviceContext.ServiceName
        if ($serviceState.IsInstalled) {
            throw ("NSIS uninstall left service '{0}' installed." -f $serviceContext.ServiceName)
        }

        if (Test-Path $resolvedInstallRoot) {
            throw ("NSIS uninstall left install root on disk: {0}" -f $resolvedInstallRoot)
        }

        Write-Host "[NtfsAudit] NSIS uninstall test completed." -ForegroundColor Cyan
        Write-Host ("  Install root: {0}" -f $resolvedInstallRoot)
    }

    "Upgrade" {
        & (Join-Path $PSScriptRoot "..\build\build-installer.ps1") -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $BaseVersion
        & (Join-Path $PSScriptRoot "..\build\build-installer.ps1") -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $UpgradeVersion

        $myPath = $MyInvocation.MyCommand.Path
        & $myPath -Action Install -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $BaseVersion -InstallRoot $InstallRoot -SkipBuild

        $resolvedInstallRoot = Resolve-NsisInstallRoot -Context $context -Runtime $Runtime -InstallRoot $InstallRoot
        $upgradeArchitecture = Resolve-NsisArchitecture -Runtime $Runtime
        $upgradeInstallerPath = Join-Path (Resolve-InstallerOutputRoot -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -OutputRoot $null) ("{0}-{1}-{2}.exe" -f $context.InstallerName, (Normalize-InstallerVersion -Version $UpgradeVersion), $upgradeArchitecture)

        $process = Start-Process -FilePath $upgradeInstallerPath -ArgumentList "/S" -Wait -PassThru -NoNewWindow
        if ($process.ExitCode -ne 0) {
            throw ("NSIS upgrade failed with exit code {0}." -f $process.ExitCode)
        }

        $appExe = Join-Path (Join-Path $resolvedInstallRoot "App") "NtfsAudit.App.exe"
        if (-not (Test-Path $appExe)) {
            throw ("Upgraded app executable not found: {0}" -f $appExe)
        }

        & $myPath -Action Uninstall -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $UpgradeVersion -InstallRoot $InstallRoot

        Write-Host "[NtfsAudit] NSIS upgrade test completed." -ForegroundColor Cyan
        Write-Host ("  Base version: {0}" -f (Normalize-InstallerVersion -Version $BaseVersion))
        Write-Host ("  Upgrade version: {0}" -f (Normalize-InstallerVersion -Version $UpgradeVersion))
        Write-Host ("  Install root: {0}" -f $resolvedInstallRoot)
    }
}
