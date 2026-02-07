param(
    [string]$Configuration,
    [string]$Framework,
    [string]$Runtime,
    [switch]$KeepDist,
    [switch]$KeepArtifacts,
    [switch]$KeepTemp,
    [switch]$KeepCache
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path ".."

$paths = @(
    (Join-Path $root ".vs"),
    (Join-Path $root "src\NtfsAudit.App\bin"),
    (Join-Path $root "src\NtfsAudit.App\obj")
)

if (-not $KeepDist) {
    if ($Configuration) {
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
    $temp = Join-Path $env:TEMP "NtfsAudit"
    if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }
}

if (-not $KeepCache) {
    $cache = Join-Path $env:LOCALAPPDATA "NtfsAudit\Cache"
    if (Test-Path $cache) { Remove-Item $cache -Recurse -Force }
}
