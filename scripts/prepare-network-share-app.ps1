param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "internal\common.ps1")

$context = Get-RepositoryContext -ScriptRoot $PSScriptRoot
$stageScript = Join-Path $PSScriptRoot "build\stage-package-layout.ps1"
& $stageScript -Configuration Release

$sourceRoot = Resolve-StagedOutputRoot -BaseRoot $context.PackagesRoot -Configuration "Release" -Framework "net8.0-windows"
$publishRoot = Resolve-StagedOutputRoot -BaseRoot $context.PublishRoot -Configuration "Release" -Framework "net8.0-windows"

Remove-DirectoryIfExists -Path $publishRoot
Ensure-Directory -Path $publishRoot
Copy-Item -Path (Join-Path $sourceRoot "*") -Destination $publishRoot -Recurse -Force

Write-Host "[NtfsAudit] Network share app folder prepared." -ForegroundColor Cyan
Write-Host ("  Source packages: {0}" -f $sourceRoot)
Write-Host ("  Shared app folder: {0}" -f $publishRoot)
