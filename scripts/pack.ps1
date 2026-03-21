param(
    [string]$Configuration = "Release",
    [string]$Framework = "net8.0-windows",
    [string]$Runtime,
    [string]$OutputRoot,
    [switch]$SkipRestore,
    [switch]$SkipBuild,
    [switch]$SkipViewer,
    [switch]$SkipService,
    [switch]$SelfContained,
    [switch]$PublishSingleFile,
    [switch]$PublishReadyToRun
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "helpers\common.ps1")

$context = Get-RepositoryContext -ScriptRoot $PSScriptRoot
Assert-RepositoryPrerequisites -Context $context

if (($SelfContained -or $PublishSingleFile -or $PublishReadyToRun) -and -not $Runtime) {
    $Runtime = "win-x64"
}

if ($SelfContained -and -not $Runtime) {
    throw "Runtime required for self-contained packaging."
}

if (-not $SkipRestore) {
    Invoke-DotNetCommand -Arguments @("restore", $context.Solution, "--nologo") -ErrorMessage "Restore failed."
}

$packageRoot = if ($OutputRoot) {
    if ([System.IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot } else { Join-Path $context.RepoRoot $OutputRoot }
} else {
    Resolve-StagedOutputRoot -BaseRoot $context.PackagesRoot -Configuration $Configuration -Runtime $Runtime -Framework $Framework
}

Remove-DirectoryIfExists -Path $packageRoot
Ensure-Directory -Path $packageRoot

function Invoke-PublishProject {
    param(
        [string]$ProjectPath,
        [string]$TargetPath,
        [string]$TargetFramework
    )

    Ensure-Directory -Path $TargetPath

    $publishArgs = @("publish", $ProjectPath, "-c", $Configuration, "--nologo", "-o", $TargetPath, "-f", $TargetFramework)
    if ($SkipRestore) {
        $publishArgs += "--no-restore"
    }
    if ($SkipBuild) {
        $publishArgs += "--no-build"
    }
    if ($Runtime) {
        $publishArgs += @("-r", $Runtime, "--self-contained", $SelfContained.IsPresent.ToString().ToLowerInvariant())
    }
    if ($PublishSingleFile) {
        $publishArgs += "-p:PublishSingleFile=true"
    }
    if ($PublishReadyToRun) {
        $publishArgs += "-p:PublishReadyToRun=true"
    }

    Invoke-DotNetCommand -Arguments $publishArgs -ErrorMessage ("Package publish failed for {0}." -f $ProjectPath)
}

Invoke-PublishProject -ProjectPath $context.AppProject -TargetPath (Join-Path $packageRoot "App") -TargetFramework $Framework

if (-not $SkipViewer) {
    Invoke-PublishProject -ProjectPath $context.ViewerProject -TargetPath (Join-Path $packageRoot "Viewer") -TargetFramework $Framework
}

if (-not $SkipService) {
    $serviceFramework = "net8.0-windows"
    Invoke-PublishProject -ProjectPath $context.ServiceProject -TargetPath (Join-Path $packageRoot "Service") -TargetFramework $serviceFramework
}

Write-Host "[NtfsAudit] Pack completed." -ForegroundColor Cyan
Write-Host ("  Configuration: {0}" -f $Configuration)
Write-Host ("  Packages: {0}" -f $packageRoot)
