param(
    [ValidateSet("win-x64", "win-x86")]
    [string]$Runtime,

    [string]$Configuration = "Release",

    [switch]$Installer
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$buildScript = Join-Path $PSScriptRoot "internal\build.ps1"
$buildArgs = @{
    Configuration = $Configuration
}
if ($Runtime) {
    $buildArgs["Runtime"] = $Runtime
}
& $buildScript @buildArgs

if ($Installer) {
    $installerScript = Join-Path $PSScriptRoot "build\build-installer.ps1"
    $installerArgs = @{
        Configuration = $Configuration
    }
    if ($Runtime) {
        $installerArgs["Runtime"] = $Runtime
    }
    & $installerScript @installerArgs
}
