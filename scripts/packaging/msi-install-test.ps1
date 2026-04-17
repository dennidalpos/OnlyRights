param(
    [string]$MsiPath,
    [string]$Configuration = "Release",
    [string]$Framework = "net8.0-windows",
    [string]$Runtime,
    [string]$Version,
    [string]$InstallRoot,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "..\helpers\msi.ps1")

$context = Get-MsiScriptContext -ScriptRoot (Join-Path $PSScriptRoot "..")
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
    & (Join-Path $PSScriptRoot "msi-build.ps1") -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $Version
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
