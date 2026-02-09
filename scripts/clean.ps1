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
    [switch]$CleanCache
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path ".."

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

$paths = @(
    (Join-Path $root ".vs"),
    (Join-Path $root "src\NtfsAudit.App\bin"),
    (Join-Path $root "src\NtfsAudit.App\obj"),
    (Join-Path $root "src\NtfsAudit.Viewer\bin"),
    (Join-Path $root "src\NtfsAudit.Viewer\obj")
)
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
    } elseif ($Configuration) {
        if ($Runtime -and $Framework) {
            $paths += (Join-Path $root "dist\$Configuration\$Runtime\$Framework")
        } elseif ($Runtime) {
            $paths += (Join-Path $root "dist\$Configuration\$Runtime")
        } elseif ($Framework) {
            $paths += (Join-Path $root "dist\$Configuration\$Framework")
        } else {
            $paths += (Join-Path $root "dist\$Configuration")
        }
    } else {
        $paths += (Join-Path $root "dist")
    }
}

if (-not $KeepArtifacts) {
    $paths += (Join-Path $root "artifacts")
}

foreach ($path in $paths) {
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

if (-not $KeepTemp) {
    $baseTemp = Get-TempRoot $TempRoot
    $temp = Join-Path $baseTemp "NtfsAudit"
    if (Test-Path $temp) {
        if ($KeepImportTemp) {
            Get-ChildItem $temp | Where-Object { $_.Name -ne "imports" } | ForEach-Object {
                Remove-Item $_.FullName -Recurse -Force
            }
        } else {
            Remove-Item $temp -Recurse -Force
        }
    }
}
elseif (-not $KeepImportTemp) {
    $baseTemp = Get-TempRoot $TempRoot
    $importTemp = Join-Path (Join-Path $baseTemp "NtfsAudit") "imports"
    if (Test-Path $importTemp) { Remove-Item $importTemp -Recurse -Force }
}

if (-not $KeepCache) {
    $localAppData = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { [Environment]::GetFolderPath("LocalApplicationData") }
    $cache = Join-Path $localAppData "NtfsAudit\Cache"
    if (Test-Path $cache) { Remove-Item $cache -Recurse -Force }
}
