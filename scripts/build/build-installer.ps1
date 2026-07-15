param(
    [string]$Configuration = "Release",
    [string]$Framework = "net8.0-windows",
    [string]$Runtime,
    [string]$PackageRoot,
    [string]$OutputRoot,
    [string]$Version,
    [switch]$SkipPack
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "..\internal\installer.ps1")

$context = Get-MsiScriptContext -ScriptRoot $PSScriptRoot
Assert-SupportedFramework -Framework $Framework
$resolvedPackageRoot = Resolve-PackageRootForMsi -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -PackageRoot $PackageRoot
if (-not $SkipPack) {
    & (Join-Path $PSScriptRoot "stage-package-layout.ps1") -Configuration $Configuration -Framework $Framework -Runtime $Runtime
}

if (-not (Test-Path $resolvedPackageRoot)) {
    throw ("Package root not found: {0}" -f $resolvedPackageRoot)
}

$resolvedOutputRoot = Resolve-MsiOutputRoot -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -OutputRoot $OutputRoot
$msiRoot = Join-Path $resolvedOutputRoot "msi"
Ensure-Directory -Path $resolvedOutputRoot
Remove-DirectoryIfExists -Path $msiRoot
Ensure-Directory -Path $msiRoot

$candlePath = Join-Path $context.Repository.WixTools "candle.exe"
$lightPath = Join-Path $context.Repository.WixTools "light.exe"
if (-not (Test-Path $candlePath) -or -not (Test-Path $lightPath)) {
    throw ("WiX toolchain not found under {0}." -f $context.Repository.WixTools)
}

foreach ($appType in @("App", "Viewer")) {
    $context = Get-MsiScriptContext -ScriptRoot $PSScriptRoot -AppType $appType
    $resolvedVersion = Resolve-MsiArtifactVersion -Context $context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $Version -PackageRoot $resolvedPackageRoot
    $msiArchitecture = Resolve-MsiArchitecture -Runtime $Runtime
    $wxsPath = Join-Path $msiRoot ("OnlyRights.NtfsAudit.{0}.wxs" -f $appType)
    $wixObjPath = Join-Path $msiRoot ("OnlyRights.NtfsAudit.{0}.wixobj" -f $appType)
    $msiPath = Join-Path $resolvedOutputRoot ("{0}-{1}-{2}.msi" -f $context.InstallerName, $resolvedVersion, $msiArchitecture)

    New-MsiSource -Context $context -PackageRoot $resolvedPackageRoot -Version $resolvedVersion -Architecture $msiArchitecture -SourcePath $wxsPath

    & $candlePath -nologo -arch $msiArchitecture -out $wixObjPath $wxsPath
    if ($LASTEXITCODE -ne 0) {
        throw ("WiX candle.exe failed for {0}." -f $appType)
    }

    & $lightPath -nologo -sval -out $msiPath $wixObjPath
    if ($LASTEXITCODE -ne 0) {
        throw ("WiX light.exe failed for {0}." -f $appType)
    }

    Write-Host ("[NtfsAudit] {0} MSI build completed." -f $appType) -ForegroundColor Cyan
    Write-Host ("  Packages: {0}" -f $resolvedPackageRoot)
    Write-Host ("  MSI: {0}" -f $msiPath)
    Write-Host ("  Version: {0}" -f $resolvedVersion)
    Write-Host ("  Architecture: {0}" -f $msiArchitecture)
}
