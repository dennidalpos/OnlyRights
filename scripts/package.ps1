param(
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",

    [string]$Configuration = "Release",
    [string]$Framework = "net8.0-windows",
    [string]$OutputRoot,
    [string]$Version,
    [switch]$SkipRestore,
    [switch]$SkipBuild,
    [switch]$SkipViewer,
    [switch]$SkipService,
    [switch]$SelfContained,
    [switch]$PublishSingleFile,
    [switch]$PublishReadyToRun,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$stageScript = Join-Path $PSScriptRoot "build\stage-package-layout.ps1"
$stageArgs = @{
    Configuration = $Configuration
    Framework     = $Framework
}

if ($Runtime) { $stageArgs["Runtime"] = $Runtime }
if ($OutputRoot) { $stageArgs["OutputRoot"] = $OutputRoot }
if ($SkipRestore) { $stageArgs["SkipRestore"] = $true }
if ($SkipBuild) { $stageArgs["SkipBuild"] = $true }
if ($SkipViewer) { $stageArgs["SkipViewer"] = $true }
if ($SkipService) { $stageArgs["SkipService"] = $true }
if ($SelfContained) { $stageArgs["SelfContained"] = $true }
if ($PublishSingleFile) { $stageArgs["PublishSingleFile"] = $true }
if ($PublishReadyToRun) { $stageArgs["PublishReadyToRun"] = $true }

& $stageScript @stageArgs

if (-not $SkipInstaller) {
    $installerScript = Join-Path $PSScriptRoot "build\build-installer.ps1"
    $installerArgs = @{
        Configuration = $Configuration
        Framework     = $Framework
        SkipPack      = $true
    }
    if ($Runtime) { $installerArgs["Runtime"] = $Runtime }
    if ($OutputRoot) { $installerArgs["OutputRoot"] = $OutputRoot }
    if ($Version) { $installerArgs["Version"] = $Version }

    & $installerScript @installerArgs
}
