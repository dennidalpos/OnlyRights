param(
    [switch]$RequireOptionalTools
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "helpers\common.ps1")

function Get-InstalledSdkVersions {
    $installedSdks = @()
    foreach ($sdkLine in (& dotnet --list-sdks)) {
        if ([string]::IsNullOrWhiteSpace($sdkLine)) {
            continue
        }

        $version = ($sdkLine -split "\s+\[", 2)[0].Trim()
        if (-not [string]::IsNullOrWhiteSpace($version)) {
            $installedSdks += $version
        }
    }

    return $installedSdks
}

function Get-SelectedSdkVersion {
    return (& dotnet --version).Trim()
}

function Test-WindowsDesktopSdkAvailable {
    param([string]$SelectedSdkVersion)

    if ([string]::IsNullOrWhiteSpace($SelectedSdkVersion)) {
        return $false
    }

    $sdkRoot = Split-Path (Get-Command dotnet).Source -Parent
    $sdkPath = Join-Path $sdkRoot ("sdk\{0}\Sdks\Microsoft.NET.Sdk.WindowsDesktop" -f $SelectedSdkVersion)
    return (Test-Path $sdkPath)
}

function Test-ExecutablePath {
    param([string]$Path)

    return -not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path $Path)
}

function New-CheckResult {
    param(
        [string]$Label,
        [bool]$Passed,
        [string]$Failure,
        [bool]$Required
    )

    return [pscustomobject]@{
        Label = $Label
        Passed = $Passed
        Failure = $Failure
        Required = $Required
    }
}

$context = Get-RepositoryContext -ScriptRoot $PSScriptRoot
Assert-RepositoryPrerequisites -Context $context

$requiredSdkVersion = Get-RequiredSdkVersion -Context $context
$sdkInstalled = Test-SdkFeatureBandInstalled -RequiredVersion $requiredSdkVersion
$currentDotnetVersion = Get-SelectedSdkVersion
$installedSdkVersions = Get-InstalledSdkVersions
$desktopSdkAvailable = Test-WindowsDesktopSdkAvailable -SelectedSdkVersion $currentDotnetVersion
$dotnetHostPath = (Get-Command dotnet).Source
$scExecutablePath = Join-Path ([Environment]::GetFolderPath("System")) "sc.exe"
$msiexecPath = Join-Path ([Environment]::GetFolderPath("System")) "msiexec.exe"
$wixCandlePath = Join-Path $context.WixTools "candle.exe"
$wixLightPath = Join-Path $context.WixTools "light.exe"

$checks = @(
    (New-CheckResult -Label "SDK feature band available" -Passed $sdkInstalled -Failure ("Required SDK feature band not installed for global.json version {0}." -f $requiredSdkVersion) -Required $true),
    (New-CheckResult -Label "Selected SDK WindowsDesktop support" -Passed $desktopSdkAvailable -Failure ("Selected SDK {0} does not expose Microsoft.NET.Sdk.WindowsDesktop." -f $currentDotnetVersion) -Required $true),
    (New-CheckResult -Label "dotnet host" -Passed (Test-ExecutablePath $dotnetHostPath) -Failure ("dotnet host not found: {0}" -f $dotnetHostPath) -Required $true),
    (New-CheckResult -Label "sc.exe (Windows service management)" -Passed (Test-ExecutablePath $scExecutablePath) -Failure ("sc.exe not found: {0}" -f $scExecutablePath) -Required $false),
    (New-CheckResult -Label "msiexec.exe (MSI install tests)" -Passed (Test-ExecutablePath $msiexecPath) -Failure ("msiexec.exe not found: {0}" -f $msiexecPath) -Required $false),
    (New-CheckResult -Label "WiX candle.exe (MSI build)" -Passed (Test-ExecutablePath $wixCandlePath) -Failure ("WiX candle.exe not found: {0}" -f $wixCandlePath) -Required $false),
    (New-CheckResult -Label "WiX light.exe (MSI build)" -Passed (Test-ExecutablePath $wixLightPath) -Failure ("WiX light.exe not found: {0}" -f $wixLightPath) -Required $false)
)

$requiredChecks = @($checks | Where-Object { $_.Required })
$optionalChecks = @($checks | Where-Object { -not $_.Required })

Write-Host "[NtfsAudit] Doctor summary" -ForegroundColor Cyan
Write-Host ("  Profile: {0}" -f $(if ($RequireOptionalTools) { "extended" } else { "initial-setup" }))
Write-Host ("  OS: Windows")
Write-Host ("  dotnet --version: {0}" -f $currentDotnetVersion)
Write-Host ("  global.json sdk.version: {0}" -f $requiredSdkVersion)
Write-Host ("  Installed SDKs: {0}" -f ($installedSdkVersions -join ", "))

Write-Host "  Required checks:"
foreach ($check in $requiredChecks) {
    $status = if ($check.Passed) { "OK" } else { "FAIL" }
    Write-Host ("    [{0}] {1}" -f $status, $check.Label)
}

Write-Host "  Optional capabilities:"
foreach ($check in $optionalChecks) {
    $status = if ($check.Passed) { "OK" } else { "WARN" }
    Write-Host ("    [{0}] {1}" -f $status, $check.Label)
}

$failedRequiredChecks = @($requiredChecks | Where-Object { -not $_.Passed })
if ($failedRequiredChecks.Count -gt 0) {
    throw (($failedRequiredChecks | ForEach-Object { $_.Failure }) -join [Environment]::NewLine)
}

$failedOptionalChecks = @($optionalChecks | Where-Object { -not $_.Passed })
if ($failedOptionalChecks.Count -gt 0) {
    if ($RequireOptionalTools) {
        throw (($failedOptionalChecks | ForEach-Object { $_.Failure }) -join [Environment]::NewLine)
    }

    Write-Warning "Some optional capabilities are not available. Base setup/build can continue, but service/MSI flows may be unavailable until these checks pass."
}
