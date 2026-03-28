param(
    [string]$TempRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$cleanScript = Join-Path $PSScriptRoot "clean.ps1"
$params = @{
    ResetToInitialState = $true
}

if ($TempRoot) {
    $params.TempRoot = $TempRoot
}

& $cleanScript @params
