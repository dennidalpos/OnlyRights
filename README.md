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

SDK selection follows global.json; a newer major SDK alone does not satisfy the .NET 8 requirement. Build and fixture results do not qualify real SMB/domain, installer or service environments.

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

Build, test, packaging, NSIS installer, service, and CI-facing commands are documented in [scripts/README.md](scripts/README.md) and in the technical operations documentation.

## Current Status

- Build, test and packaging scripts are in [scripts/README.md](scripts/README.md). The last recorded run (July 23, 2026) built the solution and NSIS installer and passed 167 tests with 0 failures; it applies to that revision only.
- The repository contains a CI workflow (`.github/workflows/ci.yml`), but GitHub Actions are disabled for this repository: build and test locally with the scripts above.

## Technical Documentation

- [Architecture](docs/architecture.md)
- [Operations and CI-facing commands](docs/development/operations.md)
- [Archive format](docs/reference/archive-format.md)
- [Credential policy](docs/reference/credentials.md)

## License

Copyright (c) 2026 Danny Perondi. All rights reserved.

This repository and its source code are proprietary. Unauthorized copying, modification, distribution, sublicensing, or commercial use is prohibited without prior written permission.

This software is provided "AS IS", without warranty or liability.
