param(
    [switch]$KeepDist,
    [switch]$KeepTemp,
    [switch]$KeepCache
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path ".."

$paths = @(
    (Join-Path $root ".vs"),
    (Join-Path $root "src\NtfsAudit.App\bin"),
    (Join-Path $root "src\NtfsAudit.App\obj"),
    (Join-Path $root "artifacts")
)

if (-not $KeepDist) {
    $paths += (Join-Path $root "dist")
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
