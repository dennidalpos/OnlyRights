param(
    [string]$Configuration = "Release",
    [string]$Framework = "net8.0-windows",
    [string]$Runtime,
    [string]$PlatformTarget,
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
$resolvedPlatformTarget = Resolve-PlatformTarget -Runtime $Runtime -PlatformTarget $PlatformTarget
$platformBuildArgs = Get-PlatformTargetBuildArgument -PlatformTarget $resolvedPlatformTarget

if (($SelfContained -or $PublishSingleFile -or $PublishReadyToRun) -and -not $Runtime) {
    throw "Runtime required for self-contained, single-file, or ReadyToRun packaging. Use -Runtime win-x64 or -Runtime win-x86."
}

if (-not $SkipRestore) {
    $restoreArgs = @("restore", $context.Solution, "--nologo")
    if ($Runtime) {
        $restoreArgs += @("-r", $Runtime)
    }
    $restoreArgs += $platformBuildArgs

    Invoke-DotNetCommand -Arguments $restoreArgs -ErrorMessage "Restore failed."
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
    $publishArgs += $platformBuildArgs
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
if ($Runtime) {
    Write-Host ("  Runtime: {0}" -f $Runtime)
}
if ($resolvedPlatformTarget) {
    Write-Host ("  Platform target: {0}" -f $resolvedPlatformTarget)
}
Write-Host ("  Packages: {0}" -f $packageRoot)
