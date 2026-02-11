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

if (!(Test-Path $solution)) { throw "Solution not found" }
if (!(Test-Path $project)) { throw "Project not found" }
if (!(Test-Path $viewerProject)) { throw "Viewer project not found" }
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "dotnet SDK not found." }

if ($RunClean) {
    $cleanScript = Join-Path $PSScriptRoot "clean.ps1"
    if (!(Test-Path $cleanScript)) { throw "clean.ps1 not found." }

    $cleanArgs = @("-Configuration", $Configuration)

    if ($Framework) { $cleanArgs += @("-Framework", $Framework) }
    if ($Runtime) { $cleanArgs += @("-Runtime", $Runtime) }
    if ($TempRoot) { $cleanArgs += @("-TempRoot", $TempRoot) }
    if ($OutputPath) { $cleanArgs += @("-DistRoot", $distRoot) }

    if ($CleanAllTemp) { $cleanArgs += "-CleanAllTemp" }
    if ($CleanImports) { $cleanArgs += "-CleanImports" }
    if ($CleanCache) { $cleanArgs += "-CleanCache" }
    if ($CleanLogs) { $cleanArgs += "-CleanLogs" }
    if ($CleanExports) { $cleanArgs += "-CleanExports" }

    if (-not $CleanDist) { $cleanArgs += "-KeepDist" }
    if (-not $CleanArtifacts) { $cleanArgs += "-KeepArtifacts" }
    if (-not $CleanTemp -and -not $CleanAllTemp) { $cleanArgs += "-KeepTemp" }
    if (-not $CleanImports -and -not $CleanAllTemp) { $cleanArgs += "-KeepImportTemp" }
    if (-not $CleanCache -and -not $CleanAllTemp) { $cleanArgs += "-KeepCache" }

    & $cleanScript @cleanArgs
    if ($LASTEXITCODE -ne 0) { throw "Clean failed." }
}

if ($CleanAllTemp) {
    $CleanTemp = $true
    $CleanImports = $true
    $CleanCache = $true
}

if ($CleanTemp) {
    $baseTemp = Get-TempRoot $TempRoot
    Remove-PathIfExists (Join-Path $baseTemp "NtfsAudit")
}

if ($CleanImports) {
    $baseTemp = Get-TempRoot $TempRoot
    Remove-PathIfExists (Join-Path (Join-Path $baseTemp "NtfsAudit") "imports")
}

if ($CleanCache) {
    $localAppData = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { [Environment]::GetFolderPath("LocalApplicationData") }
    Remove-PathIfExists (Join-Path $localAppData "NtfsAudit\Cache")
}

if ($CleanArtifacts) {
    Remove-PathIfExists (Join-Path $root "artifacts")
}

if ($CleanDist) {
    Remove-PathIfExists $distRoot
}

if ($CleanExports) {
    Remove-ExportFiles @(
        (Join-Path $root "dist"),
        (Join-Path $root "artifacts"),
        (Join-Path $root "exports")
    )

    $baseTemp = Get-TempRoot $TempRoot
    Remove-ExportFiles @((Join-Path (Join-Path $baseTemp "NtfsAudit") "exports"))
}

if ($CleanLogs) {
    $baseTemp = Get-TempRoot $TempRoot
    Remove-PathIfExists (Join-Path (Join-Path $baseTemp "NtfsAudit") "logs")

    $localAppData = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { [Environment]::GetFolderPath("LocalApplicationData") }
    Remove-PathIfExists (Join-Path $localAppData "NtfsAudit\Logs")
}

if (-not $SkipRestore) {
    & dotnet restore $solution --nologo
    if ($LASTEXITCODE -ne 0) { throw "Restore failed." }
}

$buildCompleted = $false
if (-not $SkipBuild) {
    $buildArgs = @("build", $solution, "-c", $Configuration, "--no-restore", "--nologo")
    if ($Framework) {
        $buildArgs += @("-f", $Framework)
    }
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
    $buildCompleted = $true
}

if (-not $SkipTests) {
    $testArgs = @("test", $solution, "-c", $Configuration, "--nologo")
    if ($buildCompleted) {
        $testArgs += "--no-build"
    }
    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

if (-not $SkipPublish) {
    if (-not $SkipPublishClean) {
        Remove-PathIfExists $dist
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

    $publishArgs = @("publish", $project, "-c", $Configuration, "--nologo", "-o", $dist)
    if ($buildCompleted) {
        $publishArgs += "--no-build"
    }
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

        $viewerPublishArgs = @("publish", $viewerProject, "-c", $Configuration, "--nologo", "-o", $viewerDist)
        if ($buildCompleted) {
            $viewerPublishArgs += "--no-build"
        }
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
