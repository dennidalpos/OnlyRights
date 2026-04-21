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
pwsh -ExecutionPolicy Bypass -File .\scripts\install-dependencies.ps1
pwsh -File .\scripts\maintenance\check-prerequisites.ps1
pwsh -File .\scripts\build.ps1
```

Run the main application:

```powershell
pwsh -File .\scripts\start-app.ps1
```

Run the read-only viewer:

```powershell
pwsh -File .\scripts\run\start-viewer.ps1
```

Build, test, packaging, MSI, service, and CI-facing commands are documented in [scripts/README.md](scripts/README.md) and in the technical operations documentation.

## Current Status

- The current workspace verification pass executed `pwsh -File .\scripts\clean-repo.ps1`, `pwsh -File .\scripts\install-dependencies.ps1`, `pwsh -File .\scripts\maintenance\check-prerequisites.ps1`, `pwsh -File .\scripts\build.ps1`, `pwsh -File .\scripts\build-x64.ps1`, `pwsh -File .\scripts\build-x86.ps1`, `pwsh -File .\scripts\prepare-network-share-app.ps1`, `pwsh -File .\scripts\generate-installer-x64.ps1`, and `pwsh -File .\scripts\generate-installer-x86.ps1` successfully on April 21, 2026.
- `pwsh -File .\scripts\maintenance\run-tests.ps1 -Configuration Release -SkipRestore` completed successfully in this workspace on April 21, 2026.
- `dotnet test .\tests\NtfsAudit.App.Tests\NtfsAudit.App.Tests.csproj -c Release --no-restore --filter MsiPackagingScriptTests` passed 4 script-layout tests in this workspace on April 21, 2026.
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
