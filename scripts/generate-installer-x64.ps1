param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$installerScript = Join-Path $PSScriptRoot "build\build-installer.ps1"
& $installerScript -Configuration Release -Runtime win-x64
