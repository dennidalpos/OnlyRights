param()

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
$nssmExecutablePath = if ([Environment]::Is64BitOperatingSystem) {
    Join-Path $context.NssmTools "win64\nssm.exe"
}
else {
    Join-Path $context.NssmTools "win32\nssm.exe"
}

$checks = @(
    [pscustomobject]@{ Label = "SDK feature band available"; Passed = $sdkInstalled; Failure = ("Required SDK feature band not installed for global.json version {0}." -f $requiredSdkVersion) },
    [pscustomobject]@{ Label = "Selected SDK WindowsDesktop support"; Passed = $desktopSdkAvailable; Failure = ("Selected SDK {0} does not expose Microsoft.NET.Sdk.WindowsDesktop." -f $currentDotnetVersion) },
    [pscustomobject]@{ Label = "dotnet host"; Passed = (Test-ExecutablePath $dotnetHostPath); Failure = ("dotnet host not found: {0}" -f $dotnetHostPath) },
    [pscustomobject]@{ Label = "sc.exe"; Passed = (Test-ExecutablePath $scExecutablePath); Failure = ("sc.exe not found: {0}" -f $scExecutablePath) },
    [pscustomobject]@{ Label = "msiexec.exe"; Passed = (Test-ExecutablePath $msiexecPath); Failure = ("msiexec.exe not found: {0}" -f $msiexecPath) },
    [pscustomobject]@{ Label = "WiX candle.exe"; Passed = (Test-ExecutablePath $wixCandlePath); Failure = ("WiX candle.exe not found: {0}" -f $wixCandlePath) },
    [pscustomobject]@{ Label = "WiX light.exe"; Passed = (Test-ExecutablePath $wixLightPath); Failure = ("WiX light.exe not found: {0}" -f $wixLightPath) },
    [pscustomobject]@{ Label = "NSSM fallback executable"; Passed = (Test-ExecutablePath $nssmExecutablePath); Failure = ("NSSM executable not found: {0}" -f $nssmExecutablePath) }
)

Write-Host "[NtfsAudit] Doctor summary" -ForegroundColor Cyan
Write-Host ("  OS: Windows")
Write-Host ("  dotnet --version: {0}" -f $currentDotnetVersion)
Write-Host ("  global.json sdk.version: {0}" -f $requiredSdkVersion)
Write-Host ("  Installed SDKs: {0}" -f ($installedSdkVersions -join ", "))
foreach ($check in $checks) {
    Write-Host ("  {0}: {1}" -f $check.Label, $check.Passed)
}

$failedChecks = @($checks | Where-Object { -not $_.Passed })
if ($failedChecks.Count -gt 0) {
    throw (($failedChecks | ForEach-Object { $_.Failure }) -join [Environment]::NewLine)
}
