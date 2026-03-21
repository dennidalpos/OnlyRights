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
    }
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
        [string]$AppExecutablePath
    )

    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        return (Normalize-MsiVersion -Version $Version)
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
        [string]$SourcePath
    )

    $builder = New-Object System.Text.StringBuilder
    $componentIds = New-Object System.Collections.Generic.List[string]
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
    [void]$builder.AppendLine('    <Package InstallerVersion="500" Compressed="yes" InstallScope="perUser" InstallPrivileges="limited" />')
    [void]$builder.AppendLine('    <MajorUpgrade DowngradeErrorMessage="A newer version of OnlyRights NtfsAudit is already installed." />')
    [void]$builder.AppendLine('    <MediaTemplate EmbedCab="yes" />')
    [void]$builder.AppendLine('    <Property Id="ARPNOMODIFY" Value="1" />')
    [void]$builder.AppendLine('    <Directory Id="TARGETDIR" Name="SourceDir">')
    [void]$builder.AppendLine('      <Directory Id="LocalAppDataFolder">')
    [void]$builder.AppendLine('        <Directory Id="CompanyFolder" Name="OnlyRights">')
    [void]$builder.AppendLine('          <Directory Id="INSTALLFOLDER" Name="NtfsAudit">')

    Add-MsiDirectoryContent -Builder $builder -RootPath $PackageRoot -CurrentPath $PackageRoot -DirectoryId "INSTALLFOLDER" -IndentLevel 5 -ComponentIds $componentIds -Files $files

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
        [object[]]$Files
    )

    $indent = ('  ' * $IndentLevel)
    foreach ($file in ($Files | Where-Object { [System.IO.Path]::GetDirectoryName($_.FullName) -eq $CurrentPath } | Sort-Object Name)) {
        $relativePath = Get-RelativePathCompat -BasePath $RootPath -TargetPath $file.FullName
        $componentId = Convert-ToSafeId -Prefix "Cmp" -Value $relativePath
        $fileId = Convert-ToSafeId -Prefix "Fil" -Value $relativePath
        $removeId = Convert-ToSafeId -Prefix "Rm" -Value $relativePath

        [void]$Builder.AppendLine(('{0}<Component Id="{1}" Guid="{2}">' -f $indent, $componentId, (New-StableGuid -Value $relativePath)))
        [void]$Builder.AppendLine(('{0}  <File Id="{1}" Source="{2}" Name="{3}" KeyPath="yes" />' -f $indent, $fileId, (Escape-XmlValue $file.FullName), (Escape-XmlValue $file.Name)))
        [void]$Builder.AppendLine(('{0}  <RemoveFolder Id="{1}" Directory="{2}" On="uninstall" />' -f $indent, $removeId, $DirectoryId))
        [void]$Builder.AppendLine(('{0}</Component>' -f $indent))
        $ComponentIds.Add($componentId) | Out-Null
    }

    foreach ($directory in (Get-ChildItem -Path $CurrentPath -Directory | Where-Object { $_.Name -notin @("installer", "msi") } | Sort-Object Name)) {
        $relativeDirectory = Get-RelativePathCompat -BasePath $RootPath -TargetPath $directory.FullName
        $childDirectoryId = Convert-ToSafeId -Prefix "Dir" -Value $relativeDirectory
        [void]$Builder.AppendLine(('{0}<Directory Id="{1}" Name="{2}">' -f $indent, $childDirectoryId, (Escape-XmlValue $directory.Name)))
        Add-MsiDirectoryContent -Builder $Builder -RootPath $RootPath -CurrentPath $directory.FullName -DirectoryId $childDirectoryId -IndentLevel ($IndentLevel + 1) -ComponentIds $ComponentIds -Files $Files
        [void]$Builder.AppendLine(('{0}</Directory>' -f $indent))
    }
}
