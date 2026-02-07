param(
    [string]$Configuration = "Release",
    [switch]$SkipRestore,
    [switch]$SkipBuild,
    [switch]$SkipPublish,
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
$distRoot = if ($OutputPath) { $OutputPath } else { Join-Path $root "dist\$Configuration" }
$dist = $distRoot
if ($Runtime) {
    $dist = Join-Path $distRoot $Runtime
}

if (!(Test-Path $solution)) { throw "Solution not found" }
if (!(Test-Path $project)) { throw "Project not found" }

if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "dotnet SDK not found." }

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
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
    New-Item -ItemType Directory -Path $dist | Out-Null

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
}
