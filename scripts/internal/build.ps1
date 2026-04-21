param(
    [string]$Configuration = "Release",
    [string]$Framework,
    [string]$Runtime,
    [string]$PlatformTarget,
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "common.ps1")

$context = Get-RepositoryContext -ScriptRoot $PSScriptRoot
Assert-RepositoryPrerequisites -Context $context
Assert-SupportedFramework -Framework $Framework
$resolvedPlatformTarget = Resolve-PlatformTarget -Runtime $Runtime -PlatformTarget $PlatformTarget
$platformBuildArgs = Get-PlatformTargetBuildArgument -PlatformTarget $resolvedPlatformTarget

if (-not $SkipRestore) {
    $restoreArgs = @("restore", $context.Solution, "--nologo")
    if ($Runtime) {
        $restoreArgs += @("-r", $Runtime)
    }
    $restoreArgs += $platformBuildArgs
    Invoke-DotNetCommand -Arguments $restoreArgs -ErrorMessage "Restore failed."
}

function Test-ProjectSupportsFramework {
    param(
        [string]$ProjectPath,
        [string]$TargetFramework
    )

    [xml]$project = Get-Content -Path $ProjectPath -Raw
    $frameworkValues = @()
    foreach ($node in $project.SelectNodes("//TargetFramework")) {
        $frameworkValues += [string]$node.InnerText
    }
    foreach ($node in $project.SelectNodes("//TargetFrameworks")) {
        $frameworkValues += ([string]$node.InnerText -split ";")
    }

    return @($frameworkValues | Where-Object { $_ -eq $TargetFramework }).Count -gt 0
}

$allProjects = @(
    $context.CoreProject,
    $context.AppProject,
    $context.ViewerProject,
    $context.ServiceProject,
    (Join-Path $context.RepoRoot "tests\NtfsAudit.App.Tests\NtfsAudit.App.Tests.csproj")
)

if ($Framework) {
    $projects = @($allProjects | Where-Object { Test-ProjectSupportsFramework -ProjectPath $_ -TargetFramework $Framework })

    if ($projects.Count -eq 0) {
        throw ("No projects support target framework {0}." -f $Framework)
    }

    foreach ($project in $projects) {
        $buildArgs = @("build", $project, "-c", $Configuration, "--nologo", "--no-restore", "-f", $Framework)
        if ($Runtime) {
            $buildArgs += @("-r", $Runtime)
        }
        $buildArgs += $platformBuildArgs
        Invoke-DotNetCommand -Arguments $buildArgs -ErrorMessage "Compile failed."
    }
}
elseif ($Runtime) {
    foreach ($project in $allProjects) {
        $buildArgs = @("build", $project, "-c", $Configuration, "--nologo", "--no-restore", "-r", $Runtime)
        $buildArgs += $platformBuildArgs
        Invoke-DotNetCommand -Arguments $buildArgs -ErrorMessage "Compile failed."
    }
}
else {
    $buildArgs = @("build", $context.Solution, "-c", $Configuration, "--nologo", "--no-restore")
    $buildArgs += $platformBuildArgs
    Invoke-DotNetCommand -Arguments $buildArgs -ErrorMessage "Compile failed."
}

Write-Host "[NtfsAudit] Build completed." -ForegroundColor Cyan
Write-Host ("  Configuration: {0}" -f $Configuration)
if ($Runtime) {
    Write-Host ("  Runtime: {0}" -f $Runtime)
}
if ($resolvedPlatformTarget) {
    Write-Host ("  Platform target: {0}" -f $resolvedPlatformTarget)
}
Write-Host ("  Build outputs: {0}" -f $context.BuildRoot)
