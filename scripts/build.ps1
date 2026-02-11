param(
    [string]$Configuration = "Release",
    [switch]$SkipRestore,
    [switch]$SkipBuild,
    [switch]$SkipPublish,
    [switch]$SkipViewerPublish,
    [switch]$SkipPublishClean,
    [switch]$CleanAllTemp,
    [switch]$CleanTemp,
    [switch]$CleanImports,
    [switch]$CleanCache,
    [switch]$CleanDist,
    [switch]$CleanArtifacts,
    [string]$TempRoot,
    [string]$Framework,
    [string]$OutputPath,
    [string]$Runtime,
    [switch]$SelfContained,
    [switch]$PublishSingleFile,
    [switch]$PublishReadyToRun,
    [switch]$RunClean,
    [switch]$SkipTests,
    [switch]$CleanLogs,
    [switch]$CleanExports
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$solution = Join-Path $root "NtfsAudit.sln"
$project = Join-Path $root "src\NtfsAudit.App\NtfsAudit.App.csproj"
$viewerProject = Join-Path $root "src\NtfsAudit.Viewer\NtfsAudit.Viewer.csproj"
$distRoot = if ($OutputPath) {
    if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $root $OutputPath }
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

if ($RunClean) {
    $cleanScript = Join-Path $PSScriptRoot "clean.ps1"
    if (!(Test-Path $cleanScript)) { throw "clean.ps1 not found." }
    $cleanArgs = @()
    if ($CleanAllTemp) { $cleanArgs += "-CleanAllTemp" }
    if ($CleanTemp) { $cleanArgs += "-KeepImportTemp" }
    if ($CleanImports) { $cleanArgs += "-CleanImports" }
    if ($CleanCache) { $cleanArgs += "-CleanCache" }
    if ($CleanDist) { $cleanArgs += "-Configuration"; $cleanArgs += $Configuration }
    if ($CleanArtifacts) { } else { $cleanArgs += "-KeepArtifacts" }
    if ($CleanLogs) { $cleanArgs += "-CleanLogs" }
    if ($CleanExports) { $cleanArgs += "-CleanExports" }
    & $cleanScript @cleanArgs
    if ($LASTEXITCODE -ne 0) { throw "Clean failed." }
}

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

if ($CleanAllTemp) {
    $CleanTemp = $true
    $CleanImports = $true
    $CleanCache = $true
}

if ($CleanTemp) {
    $baseTemp = Get-TempRoot $TempRoot
    $temp = Join-Path $baseTemp "NtfsAudit"
    if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }
}

if ($CleanImports) {
    $baseTemp = Get-TempRoot $TempRoot
    $importTemp = Join-Path (Join-Path $baseTemp "NtfsAudit") "imports"
    if (Test-Path $importTemp) { Remove-Item $importTemp -Recurse -Force }
}

if ($CleanCache) {
    $localAppData = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { [Environment]::GetFolderPath("LocalApplicationData") }
    $cache = Join-Path $localAppData "NtfsAudit\Cache"
    if (Test-Path $cache) { Remove-Item $cache -Recurse -Force }
}

if ($CleanArtifacts) {
    $artifactsPath = Join-Path $root "artifacts"
    if (Test-Path $artifactsPath) { Remove-Item $artifactsPath -Recurse -Force }
}

if ($CleanDist) {
    if (Test-Path $distRoot) { Remove-Item $distRoot -Recurse -Force }
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

if (-not $SkipTests) {
    & dotnet test $solution -c $Configuration --no-build --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
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
