# Architecture

OnlyRights contains the Windows `NtfsAudit` application suite. The repository is Windows-first and does not define a Linux, Bash, WSL, web, or HTTP API workflow.

## Product Boundary

Implemented:

- ACL scanning for local Windows paths, SMB/CIFS UNC paths, DFS namespaces, extended Windows paths, mapped drives, and SMB-exposed NAS paths.
- Optional SMB share permission collection.
- SID and principal identity resolution.
- Effective permission calculation, risk metrics, group membership export data, and ACL baseline comparison.
- Excel export (`.xlsx`).
- Analysis archive export/import (`.ntaudit`).
- Read-only archive viewing.
- Optional Windows Service execution for background scan jobs.
- Service-backed persisted scan schedules created from the app Settings surface.
- Protected scan credentials with path-based execution policy.

Not implemented:

- Cross-platform runtime support.
- Direct filesystem ACL mutation.
- Central server-side persistence.
- Public HTTP APIs or a web UI.
- Native NFS paths such as `nfs://server/export`, native Linux/POSIX paths, POSIX ACL scanning, HTTP/WebDAV paths, or cloud object storage without a Windows filesystem mount.

## Projects

- `src/NtfsAudit.Core`: shared scan, archive, credential, cache, export, logging, identity, permission, and path-resolution logic.
- `src/NtfsAudit.App`: WPF application containing the main UI, view models, localization, dialogs, and the shared WPF surface currently reused by the viewer package.
- `src/NtfsAudit.Service`: Windows Worker Service host for background scan jobs using `NtfsAudit.Core`.
- `src/NtfsAudit.Viewer`: WPF read-only viewer for `.ntaudit` archives that currently boots `NtfsAudit.App.MainWindow` and `NtfsAudit.App.ViewModels.MainViewModel` through a direct project reference.
- `tests/NtfsAudit.App.Tests`: xUnit tests for core path, permission, archive, export, service-job, privacy, and robustness behavior.

`NtfsAudit.Service` references `NtfsAudit.Core`; it does not reference the WPF application project.

## Frameworks

- `NtfsAudit.App`: `net6.0-windows`; `net8.0-windows`.
- `NtfsAudit.Core`: `net6.0-windows`; `net8.0-windows`.
- `NtfsAudit.Viewer`: `net6.0-windows`; `net8.0-windows`.
- `NtfsAudit.Service`: `net8.0-windows`.
- `NtfsAudit.App.Tests`: `net8.0-windows`.

`scripts/compile.ps1 -Framework <target>` builds only projects that declare the requested target framework.

## Runtime Flow

The main scan flow validates input roots, resolves path kind, enumerates filesystem entries, reads NTFS ACLs, optionally reads SMB share permissions, resolves identities when enabled, calculates effective permissions and risk data, then writes scan data for export and archive creation.

Path/provider failures are degraded into normalized error records when the scan can continue. Native NFS and POSIX ACLs are not scanned. WSL administrative shares such as `\\wsl$\...` are treated as Windows-exposed UNC paths; ACL completeness depends on what the Windows provider exposes.

## Localization

The WPF UI uses resource dictionaries in `NtfsAudit.App`. English (`en`) is the first-run default and fallback locale; Italian (`it`) is also supported.

Language selection is stored in `ui-preferences.json`. Missing or unsupported locale values fall back to English without breaking older preference files.

Persisted scan/archive/export tokens remain stable for compatibility. Strings that are part of `.ntaudit` payloads, Excel output shape, ACL/risk filtering, service jobs, or domain data should only be localized through display projection unless a separate compatibility change covers storage and migration.

## Service Mode

The service runs long or non-interactive scans outside the main UI process. It does not add separate analysis features.

Runtime service paths:

- `%ProgramData%\NtfsAudit\jobs`: service job files.
- `%ProgramData%\NtfsAudit\schedules`: persisted schedule definitions.
- `%ProgramData%\NtfsAudit\schedule-status.json`: persisted scheduler runtime state.
- `%ProgramData%\NtfsAudit\service-status.json`: service runtime status.

Service install, start, stop, uninstall, and smoke-test commands are documented in [operations](development/operations.md).

## Outputs

Generated repository outputs are centralized under `artifacts/`:

- `artifacts/build`: build outputs and MSBuild intermediates.
- `artifacts/test-results`: test output.
- `artifacts/packages`: package staging created by `scripts/pack.ps1`.
- `artifacts/publish`: local publish output copied from package staging.
- `artifacts/logs`: MSI smoke-test logs.

Runtime data outside the repository uses Windows user or machine data locations. Archive temporary workspaces use `%TEMP%\NtfsAudit\imports` and `%TEMP%\NtfsAudit\exports`.

Repository-alignment residuals are tracked in [PROJECT_STATUS.json](../PROJECT_STATUS.json) when they remain open, and described operationally in [operations](development/operations.md).

## Contract References

- Archive layout and compatibility: [archive format](reference/archive-format.md).
- Credential source, DPAPI scope, and privacy guarantees: [credential policy](reference/credentials.md).
- Build, test, CI, packaging, release boundaries, cleanup, and maintenance: [operations](development/operations.md).
