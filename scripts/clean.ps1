param(
    [string]$Configuration,
    [string]$Framework,
    [string]$Runtime,
    [string]$OutputPath,
    [string]$TempRoot,
    [switch]$KeepDist,
    [switch]$KeepArtifacts,
    [switch]$KeepTemp,
    [switch]$KeepImportTemp,
    [switch]$KeepCache
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path ".."
$distRoot = if ($OutputPath) {
    if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $root $OutputPath }
} else {
    Join-Path $root "dist"
}

$paths = @(
    (Join-Path $root ".vs"),
    (Join-Path $root "src\NtfsAudit.App\bin"),
    (Join-Path $root "src\NtfsAudit.App\obj"),
    (Join-Path $root "src\NtfsAudit.Viewer\bin"),
    (Join-Path $root "src\NtfsAudit.Viewer\obj")
)

if (-not $KeepDist) {
    if ($OutputPath) {
        $paths += $distRoot
        if ($Runtime) {
            $paths += (Join-Path $distRoot $Runtime)
        }
        if ($Framework) {
            $paths += (Join-Path $distRoot $Framework)
            if ($Runtime) {
                $paths += (Join-Path $distRoot $Runtime $Framework)
            }
        }
    } elseif ($Configuration) {
        if ($Runtime -and $Framework) {
            $paths += (Join-Path $distRoot "$Configuration\$Runtime\$Framework")
        } elseif ($Runtime) {
            $paths += (Join-Path $distRoot "$Configuration\$Runtime")
        } elseif ($Framework) {
            $paths += (Join-Path $distRoot "$Configuration\$Framework")
        } else {
            $paths += (Join-Path $distRoot $Configuration)
        }
    } else {
        $paths += $distRoot
    }
}

if (-not $KeepArtifacts) {
    $paths += (Join-Path $root "artifacts")
}

foreach ($path in $paths) {
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

if (-not $KeepTemp) {
    $baseTemp = if ($TempRoot) { $TempRoot } else { $env:TEMP }
    $temp = Join-Path $baseTemp "NtfsAudit"
    if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }
}
elseif (-not $KeepImportTemp) {
    $baseTemp = if ($TempRoot) { $TempRoot } else { $env:TEMP }
    $importTemp = Join-Path $baseTemp "NtfsAudit\\imports"
    if (Test-Path $importTemp) { Remove-Item $importTemp -Recurse -Force }
}

if (-not $KeepCache) {
    $cache = Join-Path $env:LOCALAPPDATA "NtfsAudit\Cache"
    if (Test-Path $cache) { Remove-Item $cache -Recurse -Force }
}
