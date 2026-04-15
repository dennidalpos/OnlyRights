# OnlyRights

![OnlyRights NtfsAudit mark](docs/assets/onlyrights-mark.svg)

OnlyRights is the repository for the Windows `NtfsAudit` suite: a desktop-first toolset for inspecting NTFS and SMB share permissions, exporting audit results, and reopening saved analyses.

The legal project name is **OnlyRights**. The application components use the technical name **NtfsAudit**.

## Overview

`NtfsAudit` helps review Windows filesystem permissions from a local desktop workflow. It can scan Windows-visible paths, collect permission data, export reports, and store reusable `.ntaudit` analysis archives for later review.

The suite contains:

- `NtfsAudit.App`: main WPF application for scans, filtering, export, import, and local operation.
- `NtfsAudit.Viewer`: read-only WPF viewer for `.ntaudit` archives.
- `NtfsAudit.Service`: optional Windows Service host for long-running or background scan jobs.

## Features

- NTFS ACL scanning for local, UNC, DFS, and NFS-classified paths.
- Optional SMB share permission collection.
- SID and principal resolution.
- Effective permission and risk metric calculation.
- Excel export (`.xlsx`).
- Analysis archive export/import (`.ntaudit`).
- Read-only SQLite payloads for large archive navigation.
- Optional Windows Service execution for background scans.
- Protected local scan credentials with global and per-root override support.

## Requirements

- Windows.
- .NET SDK 8 compatible with [global.json](global.json).
- `Microsoft.NET.Sdk.WindowsDesktop` support in the selected .NET SDK.

## Quick Start

From the repository root, run the verified setup path:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1
```

Start the main application:

```powershell
dotnet run --project .\src\NtfsAudit.App\NtfsAudit.App.csproj -f net8.0-windows
```

Open the read-only viewer:

```powershell
dotnet run --project .\src\NtfsAudit.Viewer\NtfsAudit.Viewer.csproj -f net8.0-windows
```

## Project Status

[PROJECT_STATUS.json](PROJECT_STATUS.json) currently contains no tracked open residual tasks.

The verified local workflow is Windows/PowerShell based. The repository includes scripts for setup, restore, prerequisite checks, build, tests, packaging, local publish, Windows Service smoke checks, and MSI smoke checks. It does not define a canonical lint, format, external deploy, signing, or release workflow.

Windows package artifacts can be produced as the default framework-dependent package, or explicitly for `win-x64` and `win-x86` when RID-specific native outputs or MSI architecture separation are required. See [Operations](docs/operations.md) for the canonical commands.

## Technical Documentation

- [Architecture](docs/architecture.md): project layout, runtime model, archive format, service mode, outputs, and maintenance constraints.
- [Operations](docs/operations.md): Windows setup, canonical commands, CI-equivalent checks, packaging, service scripts, MSI smoke tests, and cleanup.
- [CI workflow](.github/workflows/ci.yml): current GitHub Actions workflow.

## License

Copyright (c) 2026 Danny Perondi. All rights reserved.

This repository and its source code are proprietary. Unauthorized copying, modification, distribution, sublicensing, or commercial use is prohibited without prior written permission.

This software is provided "AS IS", without warranty or liability.
