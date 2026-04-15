param(
    [string]$Configuration = "Release",
    [string]$Framework,
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "helpers\common.ps1")

$context = Get-RepositoryContext -ScriptRoot $PSScriptRoot
Assert-RepositoryPrerequisites -Context $context

if (-not $SkipRestore) {
    Invoke-DotNetCommand -Arguments @("restore", $context.Solution, "--nologo") -ErrorMessage "Restore failed."
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

if ($Framework) {
    $projects = @(
        $context.AppProject,
        $context.ViewerProject,
        $context.ServiceProject,
        (Join-Path $context.RepoRoot "tests\NtfsAudit.App.Tests\NtfsAudit.App.Tests.csproj")
    ) | Where-Object { Test-ProjectSupportsFramework -ProjectPath $_ -TargetFramework $Framework }

    if ($projects.Count -eq 0) {
        throw ("No projects support target framework {0}." -f $Framework)
    }

    foreach ($project in $projects) {
        Invoke-DotNetCommand -Arguments @("build", $project, "-c", $Configuration, "--nologo", "--no-restore", "-f", $Framework) -ErrorMessage "Compile failed."
    }
}
else {
    Invoke-DotNetCommand -Arguments @("build", $context.Solution, "-c", $Configuration, "--nologo", "--no-restore") -ErrorMessage "Compile failed."
}

Write-Host "[NtfsAudit] Compile completed." -ForegroundColor Cyan
Write-Host ("  Configuration: {0}" -f $Configuration)
Write-Host ("  Build outputs: {0}" -f $context.BuildRoot)
