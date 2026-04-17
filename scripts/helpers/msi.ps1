Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "common.ps1")

function Get-MsiScriptContext {
    param([string]$ScriptRoot)

    $context = Get-RepositoryContext -ScriptRoot $ScriptRoot
    Assert-RepositoryPrerequisites -Context $context

    [pscustomobject]@{
        Repository = $context
        InstallerName = "OnlyRights-NtfsAudit"
        UpgradeCode = "{7C4212A8-0B3D-420F-8D64-22E20AAE8F59}"
        Manufacturer = "OnlyRights"
        ProductName = "OnlyRights NtfsAudit"
        DefaultInstallRoot = "OnlyRights\\NtfsAudit"
        IconPath = Join-Path $context.RepoRoot "src\NtfsAudit.App\Assets\OnlyRights.ico"
    }
}

function Resolve-MsiArchitecture {
    param([string]$Runtime)

    if ([string]::IsNullOrWhiteSpace($Runtime)) {
        return "x64"
    }

    switch ($Runtime.ToLowerInvariant()) {
        "win-x86" { return "x86" }
        "win-x64" { return "x64" }
        default { throw ("Unsupported MSI runtime '{0}'. Supported runtimes: win-x86, win-x64." -f $Runtime) }
    }
}

function Resolve-MsiInstallRoot {
    param(
        $Context,
        [string]$Runtime,
        [string]$InstallRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($InstallRoot)) {
        return (Resolve-RepositoryRelativePath -RepoRoot $Context.Repository.RepoRoot -Path $InstallRoot)
    }

    $architecture = Resolve-MsiArchitecture -Runtime $Runtime
    if ($architecture -eq "x86") {
        $programFilesRoot = if (${env:ProgramFiles(x86)}) { ${env:ProgramFiles(x86)} } else { [Environment]::GetFolderPath("ProgramFiles") }
    }
    else {
        $programFilesRoot = if ($env:ProgramW6432) { $env:ProgramW6432 } else { [Environment]::GetFolderPath("ProgramFiles") }
    }

    return (Join-Path $programFilesRoot $Context.DefaultInstallRoot)
}

function Normalize-MsiVersion {
    param([string]$Version)

    $parts = ($Version -split "[^0-9]") | Where-Object { $_ -ne "" }
    while ($parts.Count -lt 3) {
        $parts += "0"
    }

    return ("{0}.{1}.{2}" -f [int]$parts[0], [int]$parts[1], [int]$parts[2])
}

function Resolve-MsiVersion {
    param(
        [string]$Version,
        [string]$AppExecutablePath,
        $RepositoryContext
    )

    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        return (Normalize-MsiVersion -Version $Version)
    }

    if ($null -ne $RepositoryContext) {
        $repositoryVersion = Resolve-RepositoryVersion -Context $RepositoryContext
        if (-not [string]::IsNullOrWhiteSpace($repositoryVersion)) {
            return (Normalize-MsiVersion -Version $repositoryVersion)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($AppExecutablePath) -and (Test-Path $AppExecutablePath)) {
        try {
            $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($AppExecutablePath).Version
            if ($null -ne $assemblyVersion) {
                return (Normalize-MsiVersion -Version $assemblyVersion.ToString())
            }
        }
        catch {
        }

        $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($AppExecutablePath).FileVersion
        if (-not [string]::IsNullOrWhiteSpace($fileVersion)) {
            return (Normalize-MsiVersion -Version $fileVersion)
        }
    }

    return "1.0.0"
}

function Resolve-MsiArtifactVersion {
    param(
        $Context,
        [string]$Configuration = "Release",
        [string]$Framework = "net8.0-windows",
        [string]$Runtime,
        [string]$Version,
        [string]$PackageRoot
    )

    $resolvedPackageRoot = Resolve-PackageRootForMsi -Context $Context -Configuration $Configuration -Framework $Framework -Runtime $Runtime -PackageRoot $PackageRoot
    $appExecutable = Join-Path $resolvedPackageRoot "App\NtfsAudit.App.exe"
    return Resolve-MsiVersion -Version $Version -AppExecutablePath $appExecutable -RepositoryContext $Context.Repository
}

function Resolve-PackageRootForMsi {
    param(
        $Context,
        [string]$Configuration = "Release",
        [string]$Framework = "net8.0-windows",
        [string]$Runtime,
        [string]$PackageRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($PackageRoot)) {
        return (Resolve-RepositoryRelativePath -RepoRoot $Context.Repository.RepoRoot -Path $PackageRoot)
    }

    return (Resolve-StagedOutputRoot -BaseRoot $Context.Repository.PackagesRoot -Configuration $Configuration -Runtime $Runtime -Framework $Framework)
}

function Resolve-MsiOutputRoot {
    param(
        $Context,
        [string]$Configuration = "Release",
        [string]$Framework = "net8.0-windows",
        [string]$Runtime,
        [string]$OutputRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($OutputRoot)) {
        return (Resolve-RepositoryRelativePath -RepoRoot $Context.Repository.RepoRoot -Path $OutputRoot)
    }

    return (Join-Path (Resolve-StagedOutputRoot -BaseRoot $Context.Repository.PackagesRoot -Configuration $Configuration -Runtime $Runtime -Framework $Framework) "installer")
}

function New-StableGuid {
    param([string]$Value)

    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
        $hash = $md5.ComputeHash($bytes)
        return (New-Object System.Guid @(,$hash)).ToString().ToUpperInvariant()
    }
    finally {
        $md5.Dispose()
    }
}

function Convert-ToSafeId {
    param(
        [string]$Prefix,
        [string]$Value
    )

    return ("{0}_{1}" -f $Prefix, (New-StableGuid -Value $Value).Replace("-", ""))
}

function Escape-XmlValue {
    param([string]$Value)

    return [System.Security.SecurityElement]::Escape($Value)
}

function Get-RelativePathCompat {
    param(
        [string]$BasePath,
        [string]$TargetPath
    )

    $baseFullPath = [System.IO.Path]::GetFullPath($BasePath)
    $targetFullPath = [System.IO.Path]::GetFullPath($TargetPath)
    if (-not $baseFullPath.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString(), [System.StringComparison]::Ordinal)) {
        $baseFullPath += [System.IO.Path]::DirectorySeparatorChar
    }

    $baseUri = New-Object System.Uri($baseFullPath)
    $targetUri = New-Object System.Uri($targetFullPath)
    $relativeUri = $baseUri.MakeRelativeUri($targetUri)
    $relativePath = [System.Uri]::UnescapeDataString($relativeUri.ToString())
    return $relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function New-MsiSource {
    param(
        $Context,
        [string]$PackageRoot,
        [string]$Version,
        [string]$Architecture = "x64",
        [string]$SourcePath
    )

    $builder = New-Object System.Text.StringBuilder
    $componentIds = New-Object System.Collections.Generic.List[string]
    $hasProductIcon = Test-Path $Context.IconPath
    $files = Get-ChildItem -Path $PackageRoot -File -Recurse |
        Where-Object {
            $relativePath = Get-RelativePathCompat -BasePath $PackageRoot -TargetPath $_.FullName
            $pathSegments = $relativePath -split "[\\/]"
            $topLevel = if ($pathSegments.Length -gt 0) { $pathSegments[0] } else { "" }
            $topLevel -notin @("installer", "msi") -and $_.Extension -notin @(".msi", ".wixpdb", ".wixobj", ".wxs")
        } |
        Sort-Object FullName

    [void]$builder.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
    [void]$builder.AppendLine('<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi">')
    [void]$builder.AppendLine(('  <Product Id="*" Name="{0}" Language="1033" Version="{1}" Manufacturer="{2}" UpgradeCode="{3}">' -f (Escape-XmlValue $Context.ProductName), $Version, (Escape-XmlValue $Context.Manufacturer), $Context.UpgradeCode))
    [void]$builder.AppendLine(('    <Package InstallerVersion="500" Compressed="yes" InstallScope="perMachine" InstallPrivileges="elevated" Platform="{0}" />' -f (Escape-XmlValue $Architecture)))
    [void]$builder.AppendLine('    <MajorUpgrade DowngradeErrorMessage="A newer version of OnlyRights NtfsAudit is already installed." />')
    [void]$builder.AppendLine('    <MediaTemplate EmbedCab="yes" />')
    [void]$builder.AppendLine('    <Property Id="ARPNOMODIFY" Value="1" />')
    if ($hasProductIcon) {
        [void]$builder.AppendLine(('    <Icon Id="OnlyRightsIcon.ico" SourceFile="{0}" />' -f (Escape-XmlValue $Context.IconPath)))
        [void]$builder.AppendLine('    <Property Id="ARPPRODUCTICON" Value="OnlyRightsIcon.ico" />')
    }
    [void]$builder.AppendLine('    <Directory Id="TARGETDIR" Name="SourceDir">')
    [void]$builder.AppendLine('      <Directory Id="ProgramMenuFolder">')
    [void]$builder.AppendLine('        <Directory Id="ApplicationProgramsFolder" Name="OnlyRights" />')
    [void]$builder.AppendLine('      </Directory>')
    [void]$builder.AppendLine('      <Directory Id="DesktopFolder" />')
    $programFilesFolderId = if ($Architecture -eq "x64") { "ProgramFiles64Folder" } else { "ProgramFilesFolder" }
    [void]$builder.AppendLine(('      <Directory Id="{0}">' -f $programFilesFolderId))
    [void]$builder.AppendLine('        <Directory Id="CompanyFolder" Name="OnlyRights">')
    [void]$builder.AppendLine('          <Directory Id="INSTALLFOLDER" Name="NtfsAudit">')

    Add-MsiDirectoryContent -Builder $builder -RootPath $PackageRoot -CurrentPath $PackageRoot -DirectoryId "INSTALLFOLDER" -IndentLevel 5 -ComponentIds $componentIds -Files $files -HasProductIcon:$hasProductIcon

    [void]$builder.AppendLine('          </Directory>')
    [void]$builder.AppendLine('        </Directory>')
    [void]$builder.AppendLine('      </Directory>')
    [void]$builder.AppendLine('    </Directory>')
    [void]$builder.AppendLine('    <Feature Id="MainFeature" Title="OnlyRights NtfsAudit" Level="1">')
    foreach ($componentId in ($componentIds | Sort-Object)) {
        [void]$builder.AppendLine(('      <ComponentRef Id="{0}" />' -f $componentId))
    }
    [void]$builder.AppendLine('    </Feature>')
    [void]$builder.AppendLine('  </Product>')
    [void]$builder.AppendLine('</Wix>')

    [System.IO.File]::WriteAllText($SourcePath, $builder.ToString(), [System.Text.UTF8Encoding]::new($false))
}

function Invoke-Msiexec {
    param([string[]]$Arguments)

    $process = Start-Process -FilePath "msiexec.exe" -ArgumentList $Arguments -Wait -PassThru -NoNewWindow
    return $process.ExitCode
}

function Add-MsiDirectoryContent {
    param(
        [System.Text.StringBuilder]$Builder,
        [string]$RootPath,
        [string]$CurrentPath,
        [string]$DirectoryId,
        [int]$IndentLevel,
        [System.Collections.Generic.List[string]]$ComponentIds,
        [object[]]$Files,
        [bool]$HasProductIcon
    )

    $indent = ('  ' * $IndentLevel)
    $resolvedFiles = foreach ($entry in @($Files)) {
        if ($entry -is [System.Array]) {
            foreach ($nestedEntry in $entry) {
                if ($nestedEntry -is [System.IO.FileInfo]) {
                    $nestedEntry
                }
            }
            continue
        }

        if ($entry -is [System.IO.FileInfo]) {
            $entry
        }
    }

    foreach ($file in ($resolvedFiles | Where-Object { [System.IO.Path]::GetDirectoryName($_.FullName) -eq $CurrentPath } | Sort-Object Name)) {
        $relativePath = Get-RelativePathCompat -BasePath $RootPath -TargetPath $file.FullName
        $componentId = Convert-ToSafeId -Prefix "Cmp" -Value $relativePath
        $fileId = Convert-ToSafeId -Prefix "Fil" -Value $relativePath
        $removeId = Convert-ToSafeId -Prefix "Rm" -Value $relativePath
        $isMainAppExecutable = $relativePath -eq (Join-Path "App" "NtfsAudit.App.exe")
        $isServiceExecutable = $relativePath -eq (Join-Path "Service" "NtfsAudit.Service.exe")

        [void]$Builder.AppendLine(('{0}<Component Id="{1}" Guid="{2}">' -f $indent, $componentId, (New-StableGuid -Value $relativePath)))
        if ($isMainAppExecutable) {
            [void]$Builder.AppendLine(('{0}  <File Id="{1}" Source="{2}" Name="{3}" KeyPath="yes">' -f $indent, $fileId, (Escape-XmlValue $file.FullName), (Escape-XmlValue $file.Name)))
            $shortcutIcon = if ($HasProductIcon) { ' Icon="OnlyRightsIcon.ico"' } else { "" }
            [void]$Builder.AppendLine(('{0}    <Shortcut Id="StartMenuShortcut" Directory="ApplicationProgramsFolder" Name="OnlyRights NtfsAudit" WorkingDirectory="INSTALLFOLDER"{1} Advertise="yes" />' -f $indent, $shortcutIcon))
            [void]$Builder.AppendLine(('{0}    <Shortcut Id="DesktopShortcut" Directory="DesktopFolder" Name="OnlyRights NtfsAudit" WorkingDirectory="INSTALLFOLDER"{1} Advertise="yes" />' -f $indent, $shortcutIcon))
            [void]$Builder.AppendLine(('{0}  </File>' -f $indent))
            [void]$Builder.AppendLine(('{0}  <RemoveFolder Id="RemoveApplicationProgramsFolder" Directory="ApplicationProgramsFolder" On="uninstall" />' -f $indent))
        }
        else {
            [void]$Builder.AppendLine(('{0}  <File Id="{1}" Source="{2}" Name="{3}" KeyPath="yes" />' -f $indent, $fileId, (Escape-XmlValue $file.FullName), (Escape-XmlValue $file.Name)))
        }
        if ($isServiceExecutable) {
            [void]$Builder.AppendLine(('{0}  <ServiceInstall Id="NtfsAuditWorkerInstall" Name="NtfsAuditWorker" DisplayName="OnlyRights NtfsAudit Worker" Description="Background scheduler and scan worker for OnlyRights NtfsAudit." Start="auto" Type="ownProcess" ErrorControl="normal" Vital="yes" Account="LocalSystem" />' -f $indent))
            [void]$Builder.AppendLine(('{0}  <ServiceControl Id="NtfsAuditWorkerControl" Name="NtfsAuditWorker" Start="install" Stop="both" Remove="uninstall" Wait="yes" />' -f $indent))
        }
        [void]$Builder.AppendLine(('{0}  <RemoveFolder Id="{1}" Directory="{2}" On="uninstall" />' -f $indent, $removeId, $DirectoryId))
        [void]$Builder.AppendLine(('{0}</Component>' -f $indent))
        $ComponentIds.Add($componentId) | Out-Null
    }

    foreach ($directory in (Get-ChildItem -Path $CurrentPath -Directory | Where-Object { $_.Name -notin @("installer", "msi") } | Sort-Object Name)) {
        $relativeDirectory = Get-RelativePathCompat -BasePath $RootPath -TargetPath $directory.FullName
        $childDirectoryId = Convert-ToSafeId -Prefix "Dir" -Value $relativeDirectory
        [void]$Builder.AppendLine(('{0}<Directory Id="{1}" Name="{2}">' -f $indent, $childDirectoryId, (Escape-XmlValue $directory.Name)))
        Add-MsiDirectoryContent -Builder $Builder -RootPath $RootPath -CurrentPath $directory.FullName -DirectoryId $childDirectoryId -IndentLevel ($IndentLevel + 1) -ComponentIds $ComponentIds -Files $Files -HasProductIcon:$HasProductIcon
        [void]$Builder.AppendLine(('{0}</Directory>' -f $indent))
    }
}
