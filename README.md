# OnlyRights

![OnlyRights NtfsAudit mark](docs/assets/onlyrights-mark.svg)

OnlyRights is the repository for the Windows `NtfsAudit` suite: a desktop-first toolset for inspecting NTFS and SMB share permissions, exporting audit results, and reopening saved analyses.

The legal project name is **OnlyRights**. The application components use the technical name **NtfsAudit**.

## Components

- `NtfsAudit.App`: WPF application for scans, filtering, export, import, and local operation.
- `NtfsAudit.Viewer`: read-only WPF viewer for `.ntaudit` archives.
- `NtfsAudit.Service`: optional Windows Service host for background scan jobs.

## Capabilities

- Scan Windows-visible filesystem roots: local paths, SMB/CIFS UNC paths, DFS namespaces, extended Windows paths, mapped drives, and SMB-exposed NAS paths.
- Collect NTFS ACLs and optional SMB share permissions.
- Resolve SIDs and principals, including optional PowerShell Active Directory fallback.
- Calculate effective permissions, risk metrics, group membership data, and ACL baseline comparisons.
- Export Excel reports and `.ntaudit` analysis archives.
- Reopen archives in the main app or read-only viewer.
- Protect scan credentials with path-based policy and DPAPI-backed storage.

## Requirements

- Windows.
- .NET SDK 8 compatible with [global.json](global.json).
- `Microsoft.NET.Sdk.WindowsDesktop` support in the selected .NET SDK.

## Quick Start

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1
```

Run the main application:

```powershell
dotnet run --project .\src\NtfsAudit.App\NtfsAudit.App.csproj -f net8.0-windows
```

Run the read-only viewer:

```powershell
dotnet run --project .\src\NtfsAudit.Viewer\NtfsAudit.Viewer.csproj -f net8.0-windows
```

## Documentation

- [Architecture](docs/architecture.md)
- [Operations, CI, testing, packaging, and release notes](docs/development/operations.md)
- [Archive format](docs/reference/archive-format.md)
- [Credential policy](docs/reference/credentials.md)
- [Open residual work](PROJECT_STATUS.json)

## License

Copyright (c) 2026 Danny Perondi. All rights reserved.

This repository and its source code are proprietary. Unauthorized copying, modification, distribution, sublicensing, or commercial use is prohibited without prior written permission.

This software is provided "AS IS", without warranty or liability.
