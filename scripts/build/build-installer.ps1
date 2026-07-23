param(
    [string]$Configuration = "Release",
    [string]$Framework = "net8.0-windows",
    [string]$Runtime = "win-x64",
    [string]$PackageRoot,
    [string]$OutputRoot,
    [string]$Version,
    [switch]$SkipPack
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "..\internal\installer.ps1")

$makensisPath = Resolve-NsisCompilerPath
$context = Get-NsisScriptContext -ScriptRoot $PSScriptRoot
Assert-SupportedFramework -Framework $Framework
$resolvedPackageRoot = Resolve-PackageRootForInstaller -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -PackageRoot $PackageRoot
if (-not $SkipPack) {
    & (Join-Path $PSScriptRoot "stage-package-layout.ps1") -Configuration $Configuration -Framework $Framework -Runtime $Runtime
}

if (-not (Test-Path $resolvedPackageRoot)) {
    throw ("Package root not found: {0}" -f $resolvedPackageRoot)
}

$resolvedOutputRoot = Resolve-InstallerOutputRoot -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -OutputRoot $OutputRoot
$nsiStagingRoot = Join-Path $resolvedOutputRoot "nsis"
Ensure-Directory -Path $resolvedOutputRoot
Remove-DirectoryIfExists -Path $nsiStagingRoot
Ensure-Directory -Path $nsiStagingRoot

foreach ($appType in @("App", "Viewer")) {
    $context = Get-NsisScriptContext -ScriptRoot $PSScriptRoot -AppType $appType
    $resolvedVersion = Resolve-NsisArtifactVersion -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $Version -PackageRoot $resolvedPackageRoot
    $architecture = Resolve-NsisArchitecture -Runtime $Runtime
    $nsiPath = Join-Path $nsiStagingRoot ("OnlyRights.NtfsAudit.{0}.nsi" -f $appType)
    $installerPath = Join-Path $resolvedOutputRoot ("{0}-{1}-{2}.exe" -f $context.InstallerName, $resolvedVersion, $architecture)

    New-NsisSource -Context $context -PackageRoot $resolvedPackageRoot -Version $resolvedVersion -OutInstallerPath $installerPath -NsiPath $nsiPath

    & $makensisPath /V2 $nsiPath
    if ($LASTEXITCODE -ne 0) {
        throw ("NSIS compilation failed for {0}." -f $appType)
    }

    Write-Host ("[NtfsAudit] {0} NSIS installer build completed." -f $appType) -ForegroundColor Cyan
    Write-Host ("  Packages: {0}" -f $resolvedPackageRoot)
    Write-Host ("  Installer: {0}" -f $installerPath)
    Write-Host ("  Version: {0}" -f $resolvedVersion)
    Write-Host ("  Architecture: {0}" -f $architecture)
}
