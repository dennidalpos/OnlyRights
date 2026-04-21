param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$cleanScript = Join-Path $PSScriptRoot "..\internal\cleanup.ps1"
& $cleanScript -ResetToInitialState
