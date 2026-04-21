param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$cleanupScript = Join-Path $PSScriptRoot "..\internal\cleanup.ps1"
& $cleanupScript -CleanOperationalData
