# OnlyRights

![OnlyRights NtfsAudit mark](docs/assets/onlyrights-mark.svg)

OnlyRights is the Windows-first repository for the `NtfsAudit` suite: a WPF desktop application, a read-only archive viewer, and an optional Windows Service host for NTFS and SMB permission auditing on Windows-managed filesystem paths.

The legal project name is **OnlyRights**. The shipped application components use the technical name **NtfsAudit**.

## Product Overview

The repository contains three Windows-target components:

- `NtfsAudit.App`: the main WPF application for scans, filtering, export, and archive reopen.
- `NtfsAudit.Viewer`: a read-only WPF viewer for `.ntaudit` archives.
- `NtfsAudit.Service`: an optional Windows Service host for background scan jobs.

## Verified Feature Set

- Scan Windows-visible filesystem roots including local paths, UNC paths, DFS namespaces, mapped drives, extended Windows paths, and SMB-exposed NAS paths.
- Read NTFS ACLs and optionally collect SMB share permissions.
- Resolve principals and identities, calculate effective permissions, and compute risk/baseline data.
- Export Excel reports and `.ntaudit` analysis archives.
- Reopen archives in the main app or the read-only viewer.
- Protect stored scan credentials with Windows DPAPI-backed handling.
- Install and monitor the optional `NtfsAuditWorker` Windows Service.
- Create persisted service-backed scan schedules from the app Settings surface.

The repository does not evidence cross-platform runtime support, a web UI, or public HTTP APIs.

## Windows-First Setup

Requirements:

- Windows
- .NET SDK 8 compatible with [global.json](global.json)
- `Microsoft.NET.Sdk.WindowsDesktop` support in the selected SDK

Initial setup from the repository root:

```powershell
pwsh -ExecutionPolicy Bypass -File .\scripts\setup.ps1
```

Run the main application:

```powershell
dotnet run --project .\src\NtfsAudit.App\NtfsAudit.App.csproj -f net8.0-windows
```

Run the read-only viewer:

```powershell
dotnet run --project .\src\NtfsAudit.Viewer\NtfsAudit.Viewer.csproj -f net8.0-windows
```

Build, test, packaging, MSI, service, and CI-facing commands are kept in the technical operations documentation.

## Current Status

- The current workspace verification pass executed `pwsh -File .\scripts\doctor.ps1`, `pwsh -File .\scripts\build.ps1 -Configuration Release`, `pwsh -File .\scripts\test.ps1 -Configuration Release -SkipRestore`, `pwsh -File .\scripts\pack.ps1 -Configuration Release -SkipRestore -SkipBuild`, `pwsh -File .\scripts\pack.ps1 -Configuration Release -Runtime win-x64`, `pwsh -File .\scripts\pack.ps1 -Configuration Release -Runtime win-x86`, `pwsh -File .\scripts\publish.ps1 -Configuration Release`, `pwsh -File .\scripts\packaging\msi-build.ps1 -Configuration Release -SkipPack`, `pwsh -File .\scripts\packaging\msi-build.ps1 -Configuration Release -Runtime win-x86 -SkipPack`, `pwsh -File .\scripts\windows\service-install.ps1 -Configuration Release`, `pwsh -File .\scripts\windows\service-uninstall.ps1`, `pwsh -File .\scripts\packaging\msi-install-test.ps1 -Configuration Release -InstallRoot artifacts\publish\msi-smoke\basic -SkipBuild`, `pwsh -File .\scripts\packaging\msi-uninstall-test.ps1 -Configuration Release -InstallRoot artifacts\publish\msi-smoke\basic`, and `pwsh -File .\scripts\packaging\msi-upgrade-test.ps1 -Configuration Release -InstallRoot artifacts\publish\msi-smoke\upgrade` successfully on April 21, 2026.
- `pwsh -File .\scripts\test.ps1 -Configuration Release -SkipRestore` passed 160 tests in this workspace on April 21, 2026.
- Repository-aligned residual work is tracked in [PROJECT_STATUS.json](PROJECT_STATUS.json) when real open tasks remain.

## Technical Documentation

- [Architecture](docs/architecture.md)
- [Operations and CI-facing commands](docs/development/operations.md)
- [Archive format](docs/reference/archive-format.md)
- [Credential policy](docs/reference/credentials.md)

## License

Copyright (c) 2026 Danny Perondi. All rights reserved.

This repository and its source code are proprietary. Unauthorized copying, modification, distribution, sublicensing, or commercial use is prohibited without prior written permission.

This software is provided "AS IS", without warranty or liability.
