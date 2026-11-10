Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "common.ps1")

function Get-NsisScriptContext {
    param(
        [string]$ScriptRoot,
        [string]$AppType = "App"
    )

    $context = Get-RepositoryContext -ScriptRoot $ScriptRoot
    Assert-RepositoryPrerequisites -Context $context

    if ($AppType -eq "Viewer") {
        return [pscustomobject]@{
            Repository = $context
            AppType = "Viewer"
            InstallerName = "OnlyRights-NtfsAudit-Viewer"
            Manufacturer = "OnlyRights"
            ProductName = "OnlyRights NtfsAudit Viewer"
            DefaultInstallSubDir = "OnlyRights\NtfsAuditViewer"
            IconPath = Join-Path $context.RepoRoot "src\NtfsAudit.App\Assets\OnlyRights.ico"
        }
    }

    return [pscustomobject]@{
        Repository = $context
        AppType = "App"
        InstallerName = "OnlyRights-NtfsAudit"
        Manufacturer = "OnlyRights"
        ProductName = "OnlyRights NtfsAudit"
        DefaultInstallSubDir = "OnlyRights\NtfsAudit"
        IconPath = Join-Path $context.RepoRoot "src\NtfsAudit.App\Assets\OnlyRights.ico"
    }
}

function Get-MsiScriptContext {
    param(
        [string]$ScriptRoot,
        [string]$AppType = "App"
    )
    return Get-NsisScriptContext -ScriptRoot $ScriptRoot -AppType $AppType
}

function Resolve-NsisArchitecture {
    param([string]$Runtime)

    if ([string]::IsNullOrWhiteSpace($Runtime) -or $Runtime.ToLowerInvariant() -eq "win-x64") {
        return "x64"
    }

    throw ("Unsupported runtime '{0}'. Only 'win-x64' is supported." -f $Runtime)
}

function Resolve-MsiArchitecture {
    param([string]$Runtime)
    return Resolve-NsisArchitecture -Runtime $Runtime
}

function Resolve-NsisInstallRoot {
    param(
        $Context,
        [string]$Runtime = "win-x64",
        [string]$InstallRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($InstallRoot)) {
        return (Resolve-RepositoryRelativePath -RepoRoot $Context.Repository.RepoRoot -Path $InstallRoot)
    }

    $programFilesRoot = if ($env:ProgramW6432) { $env:ProgramW6432 } else { [Environment]::GetFolderPath("ProgramFiles") }
    return (Join-Path $programFilesRoot $Context.DefaultInstallSubDir)
}

function Resolve-MsiInstallRoot {
    param(
        $Context,
        [string]$Runtime = "win-x64",
        [string]$InstallRoot
    )
    return Resolve-NsisInstallRoot -Context $Context -Runtime $Runtime -InstallRoot $InstallRoot
}

function Normalize-InstallerVersion {
    param([string]$Version)

    $parts = ($Version -split "[^0-9]") | Where-Object { $_ -ne "" }
    while ($parts.Count -lt 3) {
        $parts += "0"
    }

    return ("{0}.{1}.{2}" -f [int]$parts[0], [int]$parts[1], [int]$parts[2])
}

function Normalize-MsiVersion {
    param([string]$Version)
    return Normalize-InstallerVersion -Version $Version
}

function Resolve-InstallerVersion {
    param(
        [string]$Version,
        [string]$AppExecutablePath,
        $RepositoryContext
    )

    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        return (Normalize-InstallerVersion -Version $Version)
    }

    if ($null -ne $RepositoryContext) {
        $repositoryVersion = Resolve-RepositoryVersion -Context $RepositoryContext
        if (-not [string]::IsNullOrWhiteSpace($repositoryVersion)) {
            return (Normalize-InstallerVersion -Version $repositoryVersion)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($AppExecutablePath) -and (Test-Path $AppExecutablePath)) {
        try {
            $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($AppExecutablePath).Version
            if ($null -ne $assemblyVersion) {
                return (Normalize-InstallerVersion -Version $assemblyVersion.ToString())
            }
        }
        catch {
        }

        $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($AppExecutablePath).FileVersion
        if (-not [string]::IsNullOrWhiteSpace($fileVersion)) {
            return (Normalize-InstallerVersion -Version $fileVersion)
        }
    }

    return "1.0.0"
}

function Resolve-NsisArtifactVersion {
    param(
        $Context,
        [string]$Configuration = "Release",
        [string]$Framework = "net8.0-windows",
        [string]$Runtime = "win-x64",
        [string]$Version,
        [string]$PackageRoot
    )

    $resolvedPackageRoot = Resolve-PackageRootForInstaller -Context $Context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -PackageRoot $PackageRoot
    $exeSubpath = if ($Context.AppType -eq "Viewer") { "Viewer\NtfsAudit.Viewer.exe" } else { "App\NtfsAudit.App.exe" }
    $appExecutable = Join-Path $resolvedPackageRoot $exeSubpath
    return Resolve-InstallerVersion -Version $Version -AppExecutablePath $appExecutable -RepositoryContext $Context.Repository
}

function Resolve-MsiArtifactVersion {
    param(
        $Context,
        [string]$Configuration = "Release",
        [string]$Framework = "net8.0-windows",
        [string]$Runtime = "win-x64",
        [string]$Version,
        [string]$PackageRoot
    )
    return Resolve-NsisArtifactVersion -Context $Context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -Version $Version -PackageRoot $PackageRoot
}

function Resolve-PackageRootForInstaller {
    param(
        $Context,
        [string]$Configuration = "Release",
        [string]$Framework = "net8.0-windows",
        [string]$Runtime = "win-x64",
        [string]$PackageRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($PackageRoot)) {
        return (Resolve-RepositoryRelativePath -RepoRoot $Context.Repository.RepoRoot -Path $PackageRoot)
    }

    return (Resolve-StagedOutputRoot -BaseRoot $Context.Repository.PackagesRoot -Configuration $Configuration -Runtime $Runtime -Framework $Framework)
}

function Resolve-PackageRootForMsi {
    param(
        $Context,
        [string]$Configuration = "Release",
        [string]$Framework = "net8.0-windows",
        [string]$Runtime = "win-x64",
        [string]$PackageRoot
    )
    return Resolve-PackageRootForInstaller -Context $Context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -PackageRoot $PackageRoot
}

function Resolve-InstallerOutputRoot {
    param(
        $Context,
        [string]$Configuration = "Release",
        [string]$Framework = "net8.0-windows",
        [string]$Runtime = "win-x64",
        [string]$OutputRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($OutputRoot)) {
        return (Resolve-RepositoryRelativePath -RepoRoot $Context.Repository.RepoRoot -Path $OutputRoot)
    }

    return (Join-Path (Resolve-StagedOutputRoot -BaseRoot $Context.Repository.PackagesRoot -Configuration $Configuration -Runtime $Runtime -Framework $Framework) "installer")
}

function Resolve-MsiOutputRoot {
    param(
        $Context,
        [string]$Configuration = "Release",
        [string]$Framework = "net8.0-windows",
        [string]$Runtime = "win-x64",
        [string]$OutputRoot
    )
    return Resolve-InstallerOutputRoot -Context $Context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -OutputRoot $OutputRoot
}

function New-NsisSource {
    param(
        $Context,
        [string]$PackageRoot,
        [string]$Version,
        [string]$OutInstallerPath,
        [string]$NsiPath
    )

    $builder = New-Object System.Text.StringBuilder
    $appType = $Context.AppType
    $productName = $Context.ProductName
    $manufacturer = $Context.Manufacturer
    $defaultSubDir = $Context.DefaultInstallSubDir
    $iconPath = $Context.IconPath
    $hasIcon = Test-Path $iconPath

    [void]$builder.AppendLine('!include "MUI2.nsh"')
    [void]$builder.AppendLine('Unicode true')
    [void]$builder.AppendLine('RequestExecutionLevel admin')
    [void]$builder.AppendLine(('OutFile "{0}"' -f ($OutInstallerPath -replace '\\', '\\\\')))
    [void]$builder.AppendLine(('InstallDir "$PROGRAMFILES64\{0}"' -f $defaultSubDir))
    [void]$builder.AppendLine(('InstallDirRegKey HKLM "Software\{0}" "InstallDir"' -f $Context.InstallerName))
    [void]$builder.AppendLine(('Name "{0}"' -f $productName))
    [void]$builder.AppendLine(('BrandingText "{0}"' -f $manufacturer))
    [void]$builder.AppendLine('')

    if ($hasIcon) {
        [void]$builder.AppendLine(('!define MUI_ICON "{0}"' -f ($iconPath -replace '\\', '\\\\')))
        [void]$builder.AppendLine(('!define MUI_UNICON "{0}"' -f ($iconPath -replace '\\', '\\\\')))
    }

    [void]$builder.AppendLine('!define MUI_ABORTWARNING')
    [void]$builder.AppendLine('!insertmacro MUI_PAGE_WELCOME')
    [void]$builder.AppendLine('!insertmacro MUI_PAGE_DIRECTORY')
    [void]$builder.AppendLine('!insertmacro MUI_PAGE_INSTFILES')
    [void]$builder.AppendLine('!insertmacro MUI_PAGE_FINISH')
    [void]$builder.AppendLine('')
    [void]$builder.AppendLine('!insertmacro MUI_UNPAGE_CONFIRM')
    [void]$builder.AppendLine('!insertmacro MUI_UNPAGE_INSTFILES')
    [void]$builder.AppendLine('')
    [void]$builder.AppendLine('!insertmacro MUI_LANGUAGE "English"')
    [void]$builder.AppendLine('')

    # Section Install
    [void]$builder.AppendLine('Section "MainSection" SEC01')
    [void]$builder.AppendLine('    SetOutPath "$INSTDIR"')

    $shortcutIconPath = if ($hasIcon) { $iconPath -replace '\\', '\\\\' } else { if ($appType -eq "Viewer") { '`$INSTDIR\Viewer\NtfsAudit.Viewer.exe' } else { '`$INSTDIR\App\NtfsAudit.App.exe' } }

    if ($appType -eq "App") {
        [void]$builder.AppendLine("    ExecWait 'sc.exe stop NtfsAuditWorker'")
        [void]$builder.AppendLine(('    SetOutPath "$INSTDIR\App"'))
        [void]$builder.AppendLine(('    File /r "{0}\App\*.*"' -f ($PackageRoot -replace '\\', '\\\\')))
        [void]$builder.AppendLine(('    SetOutPath "$INSTDIR\Service"'))
        [void]$builder.AppendLine(('    File /r "{0}\Service\*.*"' -f ($PackageRoot -replace '\\', '\\\\')))
        [void]$builder.AppendLine('')
        [void]$builder.AppendLine("    ExecWait 'sc.exe create NtfsAuditWorker binPath= `"`"\`"`$INSTDIR\Service\NtfsAudit.Service.exe\`"`"`" start= auto DisplayName= `"OnlyRights NtfsAudit Worker`"'")
        [void]$builder.AppendLine("    ExecWait 'sc.exe start NtfsAuditWorker'")
        [void]$builder.AppendLine('')
        [void]$builder.AppendLine('    CreateDirectory "$SMPROGRAMS\OnlyRights"')
        [void]$builder.AppendLine(('    CreateShortcut "$SMPROGRAMS\OnlyRights\OnlyRights NtfsAudit.lnk" "$INSTDIR\App\NtfsAudit.App.exe" "" "{0}" 0' -f $shortcutIconPath))
        [void]$builder.AppendLine(('    CreateShortcut "$DESKTOP\OnlyRights NtfsAudit.lnk" "$INSTDIR\App\NtfsAudit.App.exe" "" "{0}" 0' -f $shortcutIconPath))
    }
    else {
        [void]$builder.AppendLine(('    SetOutPath "$INSTDIR\Viewer"'))
        [void]$builder.AppendLine(('    File /r "{0}\Viewer\*.*"' -f ($PackageRoot -replace '\\', '\\\\')))
        [void]$builder.AppendLine('')
        [void]$builder.AppendLine('    CreateDirectory "$SMPROGRAMS\OnlyRights"')
        [void]$builder.AppendLine(('    CreateShortcut "$SMPROGRAMS\OnlyRights\OnlyRights NtfsAudit Viewer.lnk" "$INSTDIR\Viewer\NtfsAudit.Viewer.exe" "" "{0}" 0' -f $shortcutIconPath))
        [void]$builder.AppendLine(('    CreateShortcut "$DESKTOP\OnlyRights NtfsAudit Viewer.lnk" "$INSTDIR\Viewer\NtfsAudit.Viewer.exe" "" "{0}" 0' -f $shortcutIconPath))
    }

    [void]$builder.AppendLine('')
    [void]$builder.AppendLine('    WriteUninstaller "$INSTDIR\Uninstall.exe"')
    [void]$builder.AppendLine('')
    [void]$builder.AppendLine(('    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\{0}" "DisplayName" "{1}"' -f $Context.InstallerName, $productName))
    [void]$builder.AppendLine(('    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\{0}" "Publisher" "{1}"' -f $Context.InstallerName, $manufacturer))
    [void]$builder.AppendLine(('    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\{0}" "DisplayVersion" "{1}"' -f $Context.InstallerName, $Version))
    [void]$builder.AppendLine(('    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\{0}" "UninstallString" ''"$INSTDIR\Uninstall.exe"''' -f $Context.InstallerName))
    [void]$builder.AppendLine(('    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\{0}" "InstallLocation" "$INSTDIR"' -f $Context.InstallerName))
    $appExeReg = if ($appType -eq "Viewer") { '`$INSTDIR\Viewer\NtfsAudit.Viewer.exe' } else { '`$INSTDIR\App\NtfsAudit.App.exe' }
    [void]$builder.AppendLine(('    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\{0}" "DisplayIcon" "{1}"' -f $Context.InstallerName, $appExeReg))
    [void]$builder.AppendLine(('    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\{0}" "NoModify" 1' -f $Context.InstallerName))
    [void]$builder.AppendLine(('    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\{0}" "NoRepair" 1' -f $Context.InstallerName))
    [void]$builder.AppendLine('SectionEnd')
    [void]$builder.AppendLine('')

    # Section Uninstall
    [void]$builder.AppendLine('Section "Uninstall"')
    if ($appType -eq "App") {
        [void]$builder.AppendLine("    ExecWait 'sc.exe stop NtfsAuditWorker'")
        [void]$builder.AppendLine("    ExecWait 'sc.exe delete NtfsAuditWorker'")
        [void]$builder.AppendLine('    Delete "$SMPROGRAMS\OnlyRights\OnlyRights NtfsAudit.lnk"')
        [void]$builder.AppendLine('    Delete "$DESKTOP\OnlyRights NtfsAudit.lnk"')
    }
    else {
        [void]$builder.AppendLine('    Delete "$SMPROGRAMS\OnlyRights\OnlyRights NtfsAudit Viewer.lnk"')
        [void]$builder.AppendLine('    Delete "$DESKTOP\OnlyRights NtfsAudit Viewer.lnk"')
    }
    [void]$builder.AppendLine('    RMDir "$SMPROGRAMS\OnlyRights"')
    [void]$builder.AppendLine('    RMDir /r "$INSTDIR"')
    [void]$builder.AppendLine(('    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\{0}"' -f $Context.InstallerName))
    [void]$builder.AppendLine(('    DeleteRegKey HKLM "Software\{0}"' -f $Context.InstallerName))
    [void]$builder.AppendLine('SectionEnd')

    [System.IO.File]::WriteAllText($NsiPath, $builder.ToString(), [System.Text.UTF8Encoding]::new($false))
}
