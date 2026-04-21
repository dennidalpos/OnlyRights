<#
 OnlyRights
 Copyright (c) 2026 Danny Perondi
 All rights reserved.

 Proprietary and confidential.
 Viewing is permitted only for reference, evaluation, or internal review.
 Unauthorized copying, modification, distribution, sublicensing,
 commercial use, or reuse of this file is prohibited without prior
 written permission from Danny Perondi.
#>
param(
    [string]$Configuration,
    [string]$Framework,
    [string]$Runtime,
    [string]$TempRoot,
    [string]$DistRoot,
    [switch]$CleanAllTemp,
    [switch]$KeepDist,
    [switch]$KeepArtifacts,
    [switch]$KeepTemp,
    [switch]$KeepImportTemp,
    [switch]$KeepCache,
    [switch]$ImportsOnly,
    [switch]$CacheOnly,
    [switch]$CleanImports,
    [switch]$CleanCache,
    [switch]$CleanLogs,
    [switch]$CleanExports,
    [switch]$CleanServiceJobs,
    [switch]$CleanScanData,
    [switch]$CleanAnalysisImports,
    [switch]$CleanAnalysisExports,
    [switch]$CleanAnalysisWorkspace,
    [switch]$CleanImportExportData,
    [switch]$CleanOperationalData,
    [switch]$ResetToInitialState
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "common.ps1")

$context = Get-RepositoryContext -ScriptRoot $PSScriptRoot
$root = $context.RepoRoot

function Get-TempRoot {
    param([string]$PreferredRoot)
    if ($PreferredRoot) {
        if ([System.IO.Path]::IsPathRooted($PreferredRoot)) { return $PreferredRoot }
        return (Join-Path $root $PreferredRoot)
    }
    if ($env:TEMP) { return $env:TEMP }
    if ($env:TMP) { return $env:TMP }
    return [System.IO.Path]::GetTempPath()
}

function Get-NtfsAuditTempRoot {
    param([string]$PreferredRoot)
    return (Join-Path (Get-TempRoot $PreferredRoot) "NtfsAudit")
}

function Remove-PathIfExists {
    param([string]$PathToRemove)
    if (-not [string]::IsNullOrWhiteSpace($PathToRemove) -and (Test-Path $PathToRemove)) {
        Remove-Item $PathToRemove -Recurse -Force
    }
}

function Get-BuildOutputPaths {
    $result = New-Object System.Collections.Generic.List[string]

    foreach ($searchRoot in @(
        (Join-Path $root "src"),
        (Join-Path $root "tests")
    )) {
        if (-not (Test-Path $searchRoot)) {
            continue
        }

        Get-ChildItem -Path $searchRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -in @("bin", "obj") } |
            ForEach-Object { [void]$result.Add($_.FullName) }
    }

    return $result | Sort-Object -Unique
}

function Clear-NtfsAuditTempRoot {
    param(
        [string]$PreferredRoot,
        [bool]$PreserveImports = $false,
        [bool]$PreserveExports = $false
    )

    $tempRoot = Get-NtfsAuditTempRoot $PreferredRoot
    if (-not (Test-Path $tempRoot)) {
        return
    }

    $preservedNames = @()
    if ($PreserveImports) { $preservedNames += "imports" }
    if ($PreserveExports) { $preservedNames += "exports" }

    if ($preservedNames.Count -eq 0) {
        Remove-PathIfExists $tempRoot
        return
    }

    Get-ChildItem -Path $tempRoot -Force -ErrorAction SilentlyContinue |
        Where-Object { $preservedNames -notcontains $_.Name } |
        ForEach-Object { Remove-PathIfExists $_.FullName }

    $remainingEntries = @(Get-ChildItem -Path $tempRoot -Force -ErrorAction SilentlyContinue)
    if ($remainingEntries.Count -eq 0) {
        Remove-PathIfExists $tempRoot
    }
}

function Remove-ServiceJobs {
    $programData = if ($env:ProgramData) { $env:ProgramData } else { [Environment]::GetFolderPath("CommonApplicationData") }
    if ([string]::IsNullOrWhiteSpace($programData)) { return }
    $jobsPath = Join-Path $programData "NtfsAudit\jobs"
    if (Test-Path $jobsPath) {
        Get-ChildItem -Path $jobsPath -Filter "job_*.json" -File -ErrorAction SilentlyContinue |
            ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
    }
}

function Remove-ExportFiles {
    param([string[]]$BasePaths)
    foreach ($basePath in $BasePaths) {
        if (-not (Test-Path $basePath)) { continue }
        Get-ChildItem -Path $basePath -Include *.xlsx, *.ntaudit -File -Recurse -ErrorAction SilentlyContinue |
            ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
    }
}

if ($CleanImports) {
    $ImportsOnly = $true
}

if ($CleanCache) {
    $CacheOnly = $true
}

if ($CleanAllTemp) {
    $KeepTemp = $false
    $KeepImportTemp = $false
    $KeepCache = $false
    $CleanLogs = $true
    $CleanServiceJobs = $true
    $CleanScanData = $true
}

if ($CleanAnalysisWorkspace) {
    $CleanAnalysisImports = $true
    $CleanAnalysisExports = $true
}

if ($CleanImportExportData) {
    $CleanImports = $true
    $CleanExports = $true
    $CleanAnalysisImports = $true
    $CleanAnalysisExports = $true
}

if ($CleanOperationalData) {
    $CleanCache = $true
    $CleanScanData = $true
    $CleanServiceJobs = $true
    $CleanLogs = $true
}

if ($ResetToInitialState) {
    $KeepDist = $false
    $KeepArtifacts = $false
    $KeepTemp = $false
    $KeepImportTemp = $false
    $KeepCache = $false
    $CleanAllTemp = $true
    $CleanOperationalData = $true
    $CleanImportExportData = $true
    $CleanAnalysisWorkspace = $true
    $CleanImports = $true
    $CleanCache = $true
    $CleanLogs = $true
    $CleanExports = $true
    $CleanServiceJobs = $true
    $CleanScanData = $true
    $CleanAnalysisImports = $true
    $CleanAnalysisExports = $true
}

$preserveAnalysisImports = -not ($CleanImports -or $CleanAnalysisImports)
$preserveAnalysisExports = -not ($CleanExports -or $CleanAnalysisExports)

if ($CleanServiceJobs) {
    Remove-ServiceJobs
}

if ($CleanScanData) {
    Clear-NtfsAuditTempRoot -PreferredRoot $TempRoot -PreserveImports $preserveAnalysisImports -PreserveExports $preserveAnalysisExports

    $programData = if ($env:ProgramData) { $env:ProgramData } else { [Environment]::GetFolderPath("CommonApplicationData") }
    if (-not [string]::IsNullOrWhiteSpace($programData)) {
        Remove-PathIfExists (Join-Path $programData "NtfsAudit\service-status.json")
    }
}


if ($CleanAnalysisImports) {
    $baseTemp = Get-TempRoot $TempRoot
    Remove-PathIfExists (Join-Path (Join-Path $baseTemp "NtfsAudit") "imports")
}

if ($CleanAnalysisExports) {
    $baseTemp = Get-TempRoot $TempRoot
    Remove-PathIfExists (Join-Path (Join-Path $baseTemp "NtfsAudit") "exports")
}

if ($CleanExports) {
    $ImportsOnly = $false
    $CacheOnly = $false
    Remove-ExportFiles @(
        (Join-Path $root "dist"),
        (Join-Path $root "artifacts"),
        (Join-Path $root "exports")
    )

    $baseTemp = Get-TempRoot $TempRoot
    Remove-ExportFiles @(
        (Join-Path (Join-Path $baseTemp "NtfsAudit") "exports")
    )
}

$paths = @(
    (Join-Path $root ".vs"),
    (Join-Path $root "TestResults"),
    (Join-Path $root "build"),
    (Join-Path $root "out"),
    (Join-Path $root "publish"),
    (Join-Path $root "tmp"),
    (Join-Path $root "exports")
)
$paths += Get-BuildOutputPaths

if ($ImportsOnly -or $CacheOnly) {
    $paths = @()
    $KeepDist = $true
    $KeepArtifacts = $true
    $KeepTemp = $true
    $KeepImportTemp = -not $ImportsOnly
    $KeepCache = -not $CacheOnly
}

if (-not $KeepDist) {
    if ($DistRoot) {
        $distPath = if ([System.IO.Path]::IsPathRooted($DistRoot)) { $DistRoot } else { Join-Path $root $DistRoot }
        $paths += $distPath
    }
    elseif ($Configuration) {
        if ($Runtime -and $Framework) {
            $paths += (Join-Path $root "dist\$Configuration\$Runtime\$Framework")
        }
        elseif ($Runtime) {
            $paths += (Join-Path $root "dist\$Configuration\$Runtime")
        }
        elseif ($Framework) {
            $paths += (Join-Path $root "dist\$Configuration\$Framework")
        }
        else {
            $paths += (Join-Path $root "dist\$Configuration")
        }
    }
    else {
        $paths += (Join-Path $root "dist")
    }
}

if (-not $KeepArtifacts) {
    $paths += (Join-Path $root "artifacts")
}

foreach ($path in $paths) {
    Remove-PathIfExists $path
}

$baseTemp = Get-TempRoot $TempRoot
if (-not $KeepTemp) {
    Clear-NtfsAuditTempRoot -PreferredRoot $TempRoot -PreserveImports ($KeepImportTemp -or $preserveAnalysisImports) -PreserveExports $preserveAnalysisExports
}
elseif (-not $KeepImportTemp) {
    $importTemp = Join-Path (Join-Path $baseTemp "NtfsAudit") "imports"
    Remove-PathIfExists $importTemp
}

if (-not $KeepCache) {
    $localAppData = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { [Environment]::GetFolderPath("LocalApplicationData") }
    $cache = Join-Path $localAppData "NtfsAudit\Cache"
    Remove-PathIfExists $cache
}

if ($CleanLogs) {
    $tempLogs = Join-Path (Join-Path $baseTemp "NtfsAudit") "logs"
    Remove-PathIfExists $tempLogs
    $localAppData = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { [Environment]::GetFolderPath("LocalApplicationData") }
    $appLogs = Join-Path $localAppData "NtfsAudit\Logs"
    Remove-PathIfExists $appLogs
}


Write-Host "[NtfsAudit] Clean completed." -ForegroundColor Cyan
if ($ResetToInitialState) {
    Write-Host "  Profile: initial-state"
    Write-Host "  Result: repository riportato a stato sorgente-only (senza rimuovere modifiche git tracciate)"
}
