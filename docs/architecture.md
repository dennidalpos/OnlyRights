# Architecture

OnlyRights contains the Windows `NtfsAudit` application suite. The repository is Windows-first and does not define a Linux, Bash, WSL, web, or HTTP API workflow.

## Scope

The implemented product surface is:

- ACL scanning for local, UNC, DFS, and NFS-classified paths.
- Optional SMB share permission collection.
- SID and principal identity resolution.
- Effective permission calculation, risk metrics, and ACL baseline support.
- Excel export (`.xlsx`).
- Analysis archive export/import (`.ntaudit`).
- Read-only SQLite payloads for large archive navigation.
- Optional Windows Service execution for background scan jobs.
- Protected local scan credentials with global credentials for UNC/DFS scans and fallback to the current Windows user for local paths.

The repository does not implement:

- Cross-platform support.
- Direct filesystem ACL mutation.
- Central server-side persistence.
- Public HTTP APIs or a web UI.

## Projects

- `src/NtfsAudit.App`: WPF application containing the main UI, view models, scan pipeline, permission logic, archive import/export, Excel export, credential storage, and SQLite payload handling.
- `src/NtfsAudit.Service`: Windows Worker Service host for background scan jobs using shared application logic.
- `src/NtfsAudit.Viewer`: WPF read-only viewer for `.ntaudit` archives.
- `tests/NtfsAudit.App.Tests`: unit tests for core path, permission, archive, export, service-job, and robustness behavior.

## Framework Contract

- `NtfsAudit.App`: `net6.0-windows` and `net8.0-windows`.
- `NtfsAudit.Viewer`: `net6.0-windows` and `net8.0-windows`.
- `NtfsAudit.Service`: `net8.0-windows`.
- `NtfsAudit.App.Tests`: `net8.0-windows`.

`scripts/compile.ps1 -Framework <target>` builds only projects that declare the requested target framework.

## Runtime Flow

The main scan flow validates input roots, resolves path kind, enumerates filesystem entries, reads NTFS ACLs, optionally reads SMB share permissions, resolves identities when enabled, calculates effective permissions and risk data, then writes progressive scan data to JSONL outputs.

Path/provider failures are degraded into normalized error records when the scan can continue. NFS-classified paths are labeled as such, but the product only analyzes metadata exposed through Windows providers.

## Localization

The WPF UI uses a small resource-dictionary localization layer in `NtfsAudit.App`. English (`en`) is the first-run default and fallback locale; Italian (`it`) is the second supported locale. XAML chrome uses dynamic resources, while ViewModel and dialog text use the shared localization helper.

Language selection is stored as an optional field in `ui-preferences.json`. Missing or unsupported locale values fall back to English without breaking older preference files.

Persisted scan/archive/export tokens remain stable for compatibility. Strings that are part of `.ntaudit` payloads, Excel output shape, ACL/risk filtering, service jobs, or domain data should only be localized through display projection unless a separate compatibility change explicitly covers storage and migration.

## Archive Flow

`.ntaudit` export creates a ZIP archive with the required entries:

- `data.jsonl`
- `errors.jsonl`
- `tree.json`
- `folderflags.json`
- `analysis.sqlite`
- `meta.json`

Import validates the archive structure, metadata compatibility, and record counts when available. Large archives use the SQLite payload lazily; smaller or legacy archives can fall back to JSON data.

Temporary archive workspaces live under `%TEMP%\NtfsAudit\imports` and `%TEMP%\NtfsAudit\exports` and are managed separately from generic runtime cleanup.

## Service Mode

The service is optional. It exists to run long or non-interactive scans outside the main UI process. It does not add separate analysis features.

Service job files are stored under `%ProgramData%\NtfsAudit\jobs`. Service runtime status is written to `%ProgramData%\NtfsAudit\service-status.json`.

Credential material is protected locally:

- global scan credentials are stored under `%ProgramData%\NtfsAudit\scan-credentials.json` and protected for `LocalMachine`;
- legacy user-profile scan credentials are migrated to the common global credential store when they can be read;
- service job credential payloads are protected for `LocalMachine`;
- credentials are not exported into `.ntaudit` metadata.

## Outputs

Generated outputs are centralized under `artifacts/`:

- `artifacts/build`: build outputs and MSBuild intermediates.
- `artifacts/test-results`: test output.
- `artifacts/packages`: package/publish staging created by `scripts/pack.ps1`.
- `artifacts/packages/<Configuration>/<Framework>/installer`: MSI output and WiX staging.
- `artifacts/publish`: local publish output copied from packages.
- `artifacts/logs`: MSI smoke-test logs.

`artifacts/` is ignored by Git.

## Maintenance Constraints

- Keep behavior, tests, scripts, CI, documentation, and `PROJECT_STATUS.json` aligned in the same change.
- Keep `PROJECT_STATUS.json` limited to real open residual work.
- Monitor source files at or above 800 lines; evaluate files at or above 1500 lines for split/refactor work.
