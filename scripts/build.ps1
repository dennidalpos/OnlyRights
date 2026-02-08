param(
    [string]$Configuration = "Release",
    [switch]$SkipRestore,
    [switch]$SkipBuild,
    [switch]$SkipPublish,
    [switch]$SkipViewerPublish,
    [switch]$SkipPublishClean,
    [switch]$CleanTemp,
    [switch]$CleanCache,
    [string]$Framework,
    [string]$OutputPath,
    [string]$Runtime,
    [switch]$SelfContained,
    [switch]$PublishSingleFile,
    [switch]$PublishReadyToRun
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path ".."
$solution = Join-Path $root "NtfsAudit.sln"
$project = Join-Path $root "src\NtfsAudit.App\NtfsAudit.App.csproj"
$viewerProject = Join-Path $root "src\NtfsAudit.Viewer\NtfsAudit.Viewer.csproj"
$distRoot = if ($OutputPath) {
    $resolved = if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $root $OutputPath }
    [System.IO.Path]::GetFullPath($resolved)
} else {
    Join-Path $root "dist\$Configuration"
}
$dist = $distRoot
if ($Runtime) {
    $dist = Join-Path $distRoot $Runtime
}
if ($Framework) {
    $dist = Join-Path $dist $Framework
}

if (!(Test-Path $solution)) { throw "Solution not found" }
if (!(Test-Path $project)) { throw "Project not found" }
if (!(Test-Path $viewerProject)) { throw "Viewer project not found" }

if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "dotnet SDK not found." }

if ($CleanTemp) {
    $temp = Join-Path $env:TEMP "NtfsAudit"
    if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }
}

if ($CleanCache) {
    $cache = Join-Path $env:LOCALAPPDATA "NtfsAudit\Cache"
    if (Test-Path $cache) { Remove-Item $cache -Recurse -Force }
}

if (-not $SkipRestore) {
    & dotnet restore $solution --nologo
    if ($LASTEXITCODE -ne 0) { throw "Restore failed." }
}

if (-not $SkipBuild) {
    $buildArgs = @("build", $solution, "-c", $Configuration, "--no-restore", "--nologo")
    if ($Framework) {
        $buildArgs += @("-f", $Framework)
    }
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

if (-not $SkipPublish) {
    if (-not $SkipPublishClean) {
        if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
    }
    if (!(Test-Path $dist)) {
        New-Item -ItemType Directory -Path $dist | Out-Null
    }

    if (-not $Framework) {
        $Framework = "net8.0-windows"
    }
    if (-not $Runtime -and ($SelfContained -or $PublishSingleFile -or $PublishReadyToRun)) {
        $Runtime = "win-x64"
    }
    if ($SelfContained -and -not $Runtime) {
        throw "Runtime required for self-contained publish."
    }

    $publishArgs = @("publish", $project, "-c", $Configuration, "--no-build", "--nologo", "-o", $dist)
    if ($Framework) {
        $publishArgs += @("-f", $Framework)
    }
    if ($Runtime) {
        $publishArgs += @("-r", $Runtime)
        $publishArgs += @("--self-contained", $SelfContained.IsPresent.ToString().ToLowerInvariant())
    }
    if ($PublishSingleFile) {
        $publishArgs += "-p:PublishSingleFile=true"
    }
    if ($PublishReadyToRun) {
        $publishArgs += "-p:PublishReadyToRun=true"
    }

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

    if (-not $SkipViewerPublish) {
        $viewerDist = Join-Path $dist "Viewer"
        if (!(Test-Path $viewerDist)) {
            New-Item -ItemType Directory -Path $viewerDist | Out-Null
        }

        $viewerPublishArgs = @("publish", $viewerProject, "-c", $Configuration, "--no-build", "--nologo", "-o", $viewerDist)
        if ($Framework) {
            $viewerPublishArgs += @("-f", $Framework)
        }
        if ($Runtime) {
            $viewerPublishArgs += @("-r", $Runtime)
            $viewerPublishArgs += @("--self-contained", $SelfContained.IsPresent.ToString().ToLowerInvariant())
        }
        if ($PublishSingleFile) {
            $viewerPublishArgs += "-p:PublishSingleFile=true"
        }
        if ($PublishReadyToRun) {
            $viewerPublishArgs += "-p:PublishReadyToRun=true"
        }

        & dotnet @viewerPublishArgs
        if ($LASTEXITCODE -ne 0) { throw "Viewer publish failed." }
    }
}
