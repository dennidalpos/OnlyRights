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
    [switch]$CleanExports
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")

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

function Remove-PathIfExists {
    param([string]$PathToRemove)
    if (-not [string]::IsNullOrWhiteSpace($PathToRemove) -and (Test-Path $PathToRemove)) {
        Remove-Item $PathToRemove -Recurse -Force
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
    (Join-Path $root "src\NtfsAudit.App\bin"),
    (Join-Path $root "src\NtfsAudit.App\obj"),
    (Join-Path $root "src\NtfsAudit.Viewer\bin"),
    (Join-Path $root "src\NtfsAudit.Viewer\obj")
)

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
    $temp = Join-Path $baseTemp "NtfsAudit"
    if (Test-Path $temp) {
        if ($KeepImportTemp) {
            Get-ChildItem $temp | Where-Object { $_.Name -ne "imports" } | ForEach-Object {
                Remove-PathIfExists $_.FullName
            }
        }
        else {
            Remove-PathIfExists $temp
        }
    }
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
