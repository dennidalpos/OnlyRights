param(
    [switch]$OperationalData,
    [switch]$ImportExportData,
    [switch]$ResetState
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$cleanupScript = Join-Path $PSScriptRoot "internal\cleanup.ps1"
$cleanupArgs = @{}
if ($OperationalData) { $cleanupArgs["CleanOperationalData"] = $true }
if ($ImportExportData) { $cleanupArgs["CleanImportExportData"] = $true }
if ($ResetState) { $cleanupArgs["ResetToInitialState"] = $true }

& $cleanupScript @cleanupArgs
