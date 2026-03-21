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
        AppProject = Join-Path $repoRoot "src\NtfsAudit.App\NtfsAudit.App.csproj"
        ViewerProject = Join-Path $repoRoot "src\NtfsAudit.Viewer\NtfsAudit.Viewer.csproj"
        ServiceProject = Join-Path $repoRoot "src\NtfsAudit.Service\NtfsAudit.Service.csproj"
        GlobalJson = Join-Path $repoRoot "global.json"
        ArtifactsRoot = $artifactsRoot
        BuildRoot = Join-Path $artifactsRoot "build"
        TestResultsRoot = Join-Path $artifactsRoot "test-results"
        PackagesRoot = Join-Path $artifactsRoot "packages"
        PublishRoot = Join-Path $artifactsRoot "publish"
        WixTools = Join-Path $repoRoot "tools\wix314-binaries"
        NssmTools = Join-Path $repoRoot "tools\nssm"
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
        @{ Path = $Context.AppProject; Label = "App project" },
        @{ Path = $Context.ViewerProject; Label = "Viewer project" },
        @{ Path = $Context.ServiceProject; Label = "Service project" },
        @{ Path = $Context.GlobalJson; Label = "global.json" }
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
