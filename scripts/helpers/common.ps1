Set-StrictMode -Version Latest

function Test-WindowsHost {
    return [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)
}

function Get-RepositoryContext {
    param([string]$ScriptRoot)

    $repoRoot = Resolve-Path (Join-Path $ScriptRoot "..")
    $artifactsRoot = Join-Path $repoRoot "artifacts"

    [pscustomobject]@{
        RepoRoot = $repoRoot
        Solution = Join-Path $repoRoot "NtfsAudit.sln"
        CoreProject = Join-Path $repoRoot "src\NtfsAudit.Core\NtfsAudit.Core.csproj"
        AppProject = Join-Path $repoRoot "src\NtfsAudit.App\NtfsAudit.App.csproj"
        ViewerProject = Join-Path $repoRoot "src\NtfsAudit.Viewer\NtfsAudit.Viewer.csproj"
        ServiceProject = Join-Path $repoRoot "src\NtfsAudit.Service\NtfsAudit.Service.csproj"
        GlobalJson = Join-Path $repoRoot "global.json"
        DirectoryBuildProps = Join-Path $repoRoot "Directory.Build.props"
        ArtifactsRoot = $artifactsRoot
        BuildRoot = Join-Path $artifactsRoot "build"
        TestResultsRoot = Join-Path $artifactsRoot "test-results"
        PackagesRoot = Join-Path $artifactsRoot "packages"
        PublishRoot = Join-Path $artifactsRoot "publish"
        WixTools = Join-Path $repoRoot "tools\wix314-binaries"
    }
}

function Assert-RepositoryPrerequisites {
    param($Context)

    if (-not (Test-WindowsHost)) {
        throw "This repository is Windows-only."
    }

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "dotnet SDK not found."
    }

    foreach ($requiredPath in @(
        @{ Path = $Context.Solution; Label = "Solution" },
        @{ Path = $Context.CoreProject; Label = "Core project" },
        @{ Path = $Context.AppProject; Label = "App project" },
        @{ Path = $Context.ViewerProject; Label = "Viewer project" },
        @{ Path = $Context.ServiceProject; Label = "Service project" },
        @{ Path = $Context.GlobalJson; Label = "global.json" },
        @{ Path = $Context.DirectoryBuildProps; Label = "Directory.Build.props" }
    )) {
        if (-not (Test-Path $requiredPath.Path)) {
            throw ("{0} not found: {1}" -f $requiredPath.Label, $requiredPath.Path)
        }
    }
}

function Ensure-Directory {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Remove-DirectoryIfExists {
    param([string]$Path)

    if (Test-Path $Path) {
        Remove-Item -Path $Path -Recurse -Force
    }
}

function Invoke-DotNetCommand {
    param(
        [string[]]$Arguments,
        [string]$ErrorMessage
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw $ErrorMessage
    }
}

function Assert-SupportedWindowsRuntime {
    param([string]$Runtime)

    if ([string]::IsNullOrWhiteSpace($Runtime)) {
        return
    }

    if ($Runtime -notin @("win-x86", "win-x64")) {
        throw ("Unsupported runtime '{0}'. Supported Windows runtimes: win-x86, win-x64." -f $Runtime)
    }
}

function Assert-SupportedPlatformTarget {
    param([string]$PlatformTarget)

    if ([string]::IsNullOrWhiteSpace($PlatformTarget)) {
        return
    }

    if ($PlatformTarget -notin @("AnyCPU", "x86", "x64")) {
        throw ("Unsupported platform target '{0}'. Supported platform targets: AnyCPU, x86, x64." -f $PlatformTarget)
    }
}

function Resolve-PlatformTarget {
    param(
        [string]$Runtime,
        [string]$PlatformTarget
    )

    Assert-SupportedWindowsRuntime -Runtime $Runtime
    Assert-SupportedPlatformTarget -PlatformTarget $PlatformTarget

    if (-not [string]::IsNullOrWhiteSpace($PlatformTarget)) {
        if (($Runtime -eq "win-x86" -and $PlatformTarget -eq "x64") -or ($Runtime -eq "win-x64" -and $PlatformTarget -eq "x86")) {
            throw ("Runtime '{0}' is incompatible with platform target '{1}'." -f $Runtime, $PlatformTarget)
        }

        return $PlatformTarget
    }

    switch ($Runtime) {
        "win-x86" { return "x86" }
        "win-x64" { return "x64" }
        default { return $null }
    }
}

function Get-PlatformTargetBuildArgument {
    param([string]$PlatformTarget)

    if ([string]::IsNullOrWhiteSpace($PlatformTarget) -or $PlatformTarget -eq "AnyCPU") {
        return @()
    }

    return @("-p:PlatformTarget=$PlatformTarget")
}

function Get-RequiredSdkVersion {
    param($Context)

    $globalJson = Get-Content -Path $Context.GlobalJson -Raw | ConvertFrom-Json
    return [string]$globalJson.sdk.version
}

function Test-SdkFeatureBandInstalled {
    param(
        [string]$RequiredVersion
    )

    $parts = $RequiredVersion.Split(".")
    if ($parts.Length -lt 2) {
        return $false
    }

    $featurePrefix = "{0}.{1}." -f $parts[0], $parts[1]
    $installedSdks = & dotnet --list-sdks
    foreach ($sdk in $installedSdks) {
        if ($sdk.StartsWith($featurePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Resolve-StagedOutputRoot {
    param(
        [string]$BaseRoot,
        [string]$Configuration,
        [string]$Runtime,
        [string]$Framework
    )

    $resolved = Join-Path $BaseRoot $Configuration
    if ($Runtime) {
        $resolved = Join-Path $resolved $Runtime
    }
    if ($Framework) {
        $resolved = Join-Path $resolved $Framework
    }

    return $resolved
}

function Resolve-RepositoryRelativePath {
    param(
        [string]$RepoRoot,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return (Join-Path $RepoRoot $Path)
}

function Resolve-RepositoryVersion {
    param($Context)

    if ($null -eq $Context -or [string]::IsNullOrWhiteSpace($Context.DirectoryBuildProps) -or -not (Test-Path $Context.DirectoryBuildProps)) {
        return $null
    }

    try {
        [xml]$props = Get-Content -Path $Context.DirectoryBuildProps -Raw
        $propertyNames = @("OnlyRightsVersion", "Version", "VersionPrefix")
        foreach ($propertyName in $propertyNames) {
            foreach ($propertyGroup in @($props.Project.PropertyGroup)) {
                if ($null -eq $propertyGroup) {
                    continue
                }

                $value = $propertyGroup.$propertyName
                if (-not [string]::IsNullOrWhiteSpace($value)) {
                    return $value.Trim()
                }
            }
        }
    }
    catch {
    }

    return $null
}
