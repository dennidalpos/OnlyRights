Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "common.ps1")

function Get-ServiceScriptContext {
    param(
        [string]$ScriptRoot,
        [string]$ServiceName = "NtfsAuditWorker"
    )

    $context = Get-RepositoryContext -ScriptRoot $ScriptRoot
    Assert-RepositoryPrerequisites -Context $context

    [pscustomobject]@{
        Repository = $context
        ServiceName = $ServiceName
    }
}

function Resolve-SpecialFolderPath {
    param([System.Environment+SpecialFolder]$Folder)

    $resolved = [Environment]::GetFolderPath($Folder)
    if ([string]::IsNullOrWhiteSpace($resolved)) {
        return $null
    }

    return $resolved
}

function Resolve-ScExecutablePath {
    $systemDir = Resolve-SpecialFolderPath -Folder System
    if ($systemDir) {
        $candidate = Join-Path $systemDir "sc.exe"
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    $windowsDir = Resolve-SpecialFolderPath -Folder Windows
    if ($windowsDir) {
        $sysnative = Join-Path $windowsDir "sysnative\sc.exe"
        if (Test-Path $sysnative) {
            return $sysnative
        }
    }

    return "sc.exe"
}

function Resolve-DotnetHostPath {
    $programFiles = Resolve-SpecialFolderPath -Folder ProgramFiles
    if ($programFiles) {
        $candidate = Join-Path $programFiles "dotnet\dotnet.exe"
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return "dotnet.exe"
}

function Invoke-ServiceExecutable {
    param(
        [string]$FilePath,
        [string]$Arguments,
        [switch]$RunAsAdmin
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = $Arguments
    $startInfo.UseShellExecute = $RunAsAdmin.IsPresent
    $startInfo.CreateNoWindow = -not $RunAsAdmin.IsPresent

    if ($RunAsAdmin) {
        $startInfo.Verb = "runas"
    }
    else {
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
    }

    try {
        $process = [System.Diagnostics.Process]::Start($startInfo)
        if ($null -eq $process) {
            return [pscustomobject]@{
                ExitCode = -1
                Output = ""
                Error = ("Failed to start {0}." -f $FilePath)
            }
        }

        $process.WaitForExit()
        $output = ""
        $error = ""
        if (-not $RunAsAdmin) {
            $output = $process.StandardOutput.ReadToEnd()
            $error = $process.StandardError.ReadToEnd()
        }

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $output
            Error = $error
        }
    }
    catch {
        return [pscustomobject]@{
            ExitCode = -1
            Output = ""
            Error = $_.Exception.Message
        }
    }
}

function Invoke-ScCommand {
    param(
        [string]$Arguments,
        [switch]$AllowFailure
    )

    $scPath = Resolve-ScExecutablePath
    $result = Invoke-ServiceExecutable -FilePath $scPath -Arguments $Arguments
    if ($result.ExitCode -eq 0) {
        return $result
    }

    $errorText = if ([string]::IsNullOrWhiteSpace($result.Error)) { $result.Output } else { $result.Error }
    $needsElevation = $result.ExitCode -eq 5 -or ($errorText -match "accesso negato|access is denied")
    if ($needsElevation) {
        $result = Invoke-ServiceExecutable -FilePath $scPath -Arguments $Arguments -RunAsAdmin
    }

    if (-not $AllowFailure -and $result.ExitCode -ne 0) {
        $message = if ([string]::IsNullOrWhiteSpace($result.Error)) { $result.Output } else { $result.Error }
        if ([string]::IsNullOrWhiteSpace($message)) {
            $message = "Unknown sc.exe failure."
        }

        throw ("sc.exe {0} failed with exit code {1}: {2}" -f $Arguments, $result.ExitCode, $message.Trim())
    }

    return $result
}

function Get-ServiceState {
    param([string]$ServiceName)

    $result = Invoke-ScCommand -Arguments ("query {0}" -f $ServiceName) -AllowFailure
    $combinedOutput = "{0} {1}" -f $result.Output, $result.Error

    if ($result.ExitCode -eq 1060 -or $combinedOutput -match "does not exist|non esiste") {
        return [pscustomobject]@{
            IsInstalled = $false
            IsRunning = $false
        }
    }

    if ($result.ExitCode -ne 0) {
        return [pscustomobject]@{
            IsInstalled = $true
            IsRunning = $false
        }
    }

    return [pscustomobject]@{
        IsInstalled = $true
        IsRunning = ($combinedOutput -match "RUNNING")
    }
}

function Resolve-ServiceCommandPath {
    param(
        $Context,
        [string]$Configuration = "Release",
        [string]$Framework = "net8.0-windows",
        [string]$Runtime,
        [string]$ServiceCommandPath
    )

    $repo = $Context.Repository
    $explicitPath = Resolve-RepositoryRelativePath -RepoRoot $repo.RepoRoot -Path $ServiceCommandPath
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($explicitPath) {
        $candidates.Add($explicitPath)
    }

    $packageRoot = Resolve-StagedOutputRoot -BaseRoot $repo.PackagesRoot -Configuration $Configuration -Runtime $Runtime -Framework $Framework
    $publishRoot = Resolve-StagedOutputRoot -BaseRoot $repo.PublishRoot -Configuration $Configuration -Runtime $Runtime -Framework $Framework
    $buildRoot = Join-Path $repo.BuildRoot ("NtfsAudit.Service\{0}\net8.0-windows" -f $Configuration)

    foreach ($basePath in @(
        $packageRoot,
        $publishRoot,
        $buildRoot,
        (Join-Path $repo.RepoRoot "src\NtfsAudit.Service\bin\$Configuration\net8.0-windows")
    )) {
        if ([string]::IsNullOrWhiteSpace($basePath)) {
            continue
        }

        foreach ($relativePath in @(
            "Service\NtfsAudit.Service.exe",
            "Service\NtfsAudit.Service.dll",
            "NtfsAudit.Service.exe",
            "NtfsAudit.Service.dll"
        )) {
            $candidates.Add((Join-Path $basePath $relativePath))
        }
    }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    return $null
}

function Format-ServiceBinPathForSc {
    param([string]$ServiceCommand)

    if ([string]::IsNullOrWhiteSpace($ServiceCommand)) {
        throw "Service command path is empty."
    }

    $sanitizedCommand = $ServiceCommand.Replace('"', "")
    if ($sanitizedCommand.EndsWith(".exe", [System.StringComparison]::OrdinalIgnoreCase)) {
        return ('"{0}"' -f $sanitizedCommand)
    }

    $dotnetHost = (Resolve-DotnetHostPath).Replace('"', "")
    return ('"\"{0}\" \"{1}\""' -f $dotnetHost, $sanitizedCommand)
}

function Install-WindowsService {
    param(
        $Context,
        [string]$ServiceCommand,
        [string]$Description = "Servizio scansione NTFS Audit"
    )

    $state = Get-ServiceState -ServiceName $Context.ServiceName
    $binPath = Format-ServiceBinPathForSc -ServiceCommand $ServiceCommand
    $createResult = Invoke-ScCommand -Arguments ('create {0} binPath= {1} start= auto' -f $Context.ServiceName, $binPath) -AllowFailure
    if ($createResult.ExitCode -eq 1073 -or $state.IsInstalled) {
        Invoke-ScCommand -Arguments ('config {0} binPath= {1} start= auto' -f $Context.ServiceName, $binPath) | Out-Null
    }
    elseif ($createResult.ExitCode -ne 0) {
        throw ("Service create failed with exit code {0}." -f $createResult.ExitCode)
    }

    Invoke-ScCommand -Arguments ('description {0} "{1}"' -f $Context.ServiceName, $Description) -AllowFailure | Out-Null
}

function Start-WindowsService {
    param([string]$ServiceName)

    $result = Invoke-ScCommand -Arguments ('start {0}' -f $ServiceName) -AllowFailure
    if ($result.ExitCode -ne 0 -and $result.ExitCode -ne 1056) {
        throw ("Service start failed with exit code {0}." -f $result.ExitCode)
    }
}

function Stop-WindowsService {
    param([string]$ServiceName)

    $result = Invoke-ScCommand -Arguments ('stop {0}' -f $ServiceName) -AllowFailure
    if ($result.ExitCode -ne 0 -and $result.ExitCode -ne 1060 -and $result.ExitCode -ne 1062) {
        throw ("Service stop failed with exit code {0}." -f $result.ExitCode)
    }
}

function Uninstall-WindowsService {
    param($Context)

    Stop-WindowsService -ServiceName $Context.ServiceName
    $result = Invoke-ScCommand -Arguments ('delete {0}' -f $Context.ServiceName) -AllowFailure
    if ($result.ExitCode -ne 0 -and $result.ExitCode -ne 1060) {
        throw ("Service delete failed with exit code {0}." -f $result.ExitCode)
    }
}

function Remove-ServiceResiduals {
    $programData = if ($env:ProgramData) { $env:ProgramData } else { [Environment]::GetFolderPath("CommonApplicationData") }
    $localAppData = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { [Environment]::GetFolderPath("LocalApplicationData") }
    $tempRoot = if ($env:TEMP) { $env:TEMP } elseif ($env:TMP) { $env:TMP } else { [System.IO.Path]::GetTempPath() }

    foreach ($path in @(
        (Join-Path $programData "NtfsAudit\jobs"),
        (Join-Path $programData "NtfsAudit\service-status.json"),
        (Join-Path $localAppData "NtfsAudit\Logs"),
        (Join-Path $tempRoot "NtfsAudit\logs"),
        (Join-Path $tempRoot "NtfsAudit\queue")
    )) {
        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        if (Test-Path $path) {
            Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
