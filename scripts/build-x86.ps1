param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$buildScript = Join-Path $PSScriptRoot "internal\build.ps1"
& $buildScript -Configuration Release -Runtime win-x86
