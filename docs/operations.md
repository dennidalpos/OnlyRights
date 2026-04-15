# Operations

All repository commands are Windows/PowerShell commands. The repository does not define Bash, WSL, npm, lint, or format workflows.

## Prerequisites

- Windows.
- .NET SDK 8 compatible with `global.json`.
- `Microsoft.NET.Sdk.WindowsDesktop` support in the selected .NET SDK.

Optional capabilities checked by `scripts/doctor.ps1`:

- `sc.exe` for Windows Service management.
- `msiexec.exe` for MSI install/uninstall smoke tests.
- WiX `candle.exe` and `light.exe` under `tools/wix314-binaries`.
- NSSM under `tools/nssm` for the optional fallback service path.

## Setup

Recommended first-run setup:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1
```

Setup runs:

1. `scripts/bootstrap.ps1`
2. `scripts/doctor.ps1`
3. `scripts/build.ps1 -Configuration Release -SkipRestore`

Prepare without building:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1 -SkipBuild
```

Make optional service/MSI checks blocking:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1 -RequireOptionalTools
```

## Canonical Commands

Restore:

```powershell
powershell -File .\scripts\bootstrap.ps1
```

Prerequisite check:

```powershell
powershell -File .\scripts\doctor.ps1
```

Build:

```powershell
powershell -File .\scripts\build.ps1 -Configuration Release
```

Test:

```powershell
powershell -File .\scripts\test.ps1 -Configuration Release
```

Pack:

```powershell
powershell -File .\scripts\pack.ps1 -Configuration Release
```

RID-specific package artifacts:

```powershell
powershell -File .\scripts\pack.ps1 -Configuration Release -Runtime win-x64
powershell -File .\scripts\pack.ps1 -Configuration Release -Runtime win-x86
```

Publish local package output:

```powershell
powershell -File .\scripts\publish.ps1 -Configuration Release
```

Run the main WPF app:

```powershell
dotnet run --project .\src\NtfsAudit.App\NtfsAudit.App.csproj -f net8.0-windows
```

Run the read-only viewer:

```powershell
dotnet run --project .\src\NtfsAudit.Viewer\NtfsAudit.Viewer.csproj -f net8.0-windows
```

## CI-Equivalent Local Verification

The CI workflow runs on `windows-latest` and uses the same repository scripts:

```powershell
powershell -File .\scripts\clean.ps1
powershell -File .\scripts\bootstrap.ps1
powershell -File .\scripts\doctor.ps1
powershell -File .\scripts\build.ps1 -Configuration Release -SkipRestore
powershell -File .\scripts\test.ps1 -Configuration Release -SkipRestore -SkipBuild
powershell -File .\scripts\pack.ps1 -Configuration Release -SkipRestore -SkipBuild
powershell -File .\scripts\publish.ps1 -Configuration Release
powershell -File .\scripts\build.ps1 -Configuration Release -Framework net6.0-windows -SkipRestore
powershell -File .\scripts\pack.ps1 -Configuration Release -Framework net6.0-windows -SkipRestore -SkipService
powershell -File .\scripts\publish.ps1 -Configuration Release -Framework net6.0-windows
powershell -File .\scripts\packaging\msi-build.ps1 -Configuration Release -SkipPack
powershell -File .\scripts\windows\service-install.ps1 -Configuration Release
powershell -File .\scripts\windows\service-uninstall.ps1
powershell -File .\scripts\packaging\msi-install-test.ps1 -Configuration Release -InstallRoot artifacts\publish\msi-smoke\basic -SkipBuild
powershell -File .\scripts\packaging\msi-uninstall-test.ps1 -Configuration Release -InstallRoot artifacts\publish\msi-smoke\basic
powershell -File .\scripts\packaging\msi-upgrade-test.ps1 -Configuration Release -InstallRoot artifacts\publish\msi-smoke\upgrade
powershell -File .\scripts\clean.ps1
```

Build, packaging, local publish, service smoke, MSI smoke, deploy, signing, and release are separate phases. Passing build or tests does not prove packaging, deploy, signing, or release readiness.

This repository has no evidenced external deploy command and no evidenced signing/release command.

## Packaging

`scripts/pack.ps1` creates publish-style package directories under `artifacts/packages`.

Default package target:

```powershell
powershell -File .\scripts\pack.ps1 -Configuration Release
```

The default package is framework-dependent and uses the normal `Any CPU` project configuration. Use explicit Windows runtime identifiers when a native/RID-specific artifact is required:

```powershell
powershell -File .\scripts\pack.ps1 -Configuration Release -Runtime win-x64
powershell -File .\scripts\pack.ps1 -Configuration Release -Runtime win-x86
```

Self-contained package:

```powershell
powershell -File .\scripts\pack.ps1 -Configuration Release -Runtime win-x64 -SelfContained -PublishSingleFile
powershell -File .\scripts\pack.ps1 -Configuration Release -Runtime win-x86 -SelfContained -PublishSingleFile
```

MSI build:

```powershell
powershell -File .\scripts\packaging\msi-build.ps1 -Configuration Release -Version 1.0.0
powershell -File .\scripts\packaging\msi-build.ps1 -Configuration Release -Runtime win-x64 -Version 1.0.0
powershell -File .\scripts\packaging\msi-build.ps1 -Configuration Release -Runtime win-x86 -Version 1.0.0
```

The MSI is generated with local WiX binaries from `tools/wix314-binaries`. The default MSI preserves the existing x64 output. Runtime-specific MSIs are staged under `artifacts/packages/<Configuration>/<Runtime>/<Framework>/installer` and are named with their architecture suffix, for example `OnlyRights-NtfsAudit-1.0.0-x64.msi` or `OnlyRights-NtfsAudit-1.0.0-x86.msi`.

MSI packages include the app icon in Add/Remove Programs and in setup-created Desktop and Start Menu shortcuts.

## Service Scripts

Install and start the service from the canonical package/build locations:

```powershell
powershell -File .\scripts\windows\service-install.ps1 -Configuration Release
```

Stop and uninstall the service:

```powershell
powershell -File .\scripts\windows\service-uninstall.ps1
```

Start/stop helpers:

```powershell
powershell -File .\scripts\windows\service-start.ps1
powershell -File .\scripts\windows\service-stop.ps1
```

## MSI Smoke Tests

Install smoke:

```powershell
powershell -File .\scripts\packaging\msi-install-test.ps1 -Configuration Release -InstallRoot artifacts\publish\msi-smoke\basic -SkipBuild
```

Uninstall smoke:

```powershell
powershell -File .\scripts\packaging\msi-uninstall-test.ps1 -Configuration Release -InstallRoot artifacts\publish\msi-smoke\basic
```

Upgrade smoke:

```powershell
powershell -File .\scripts\packaging\msi-upgrade-test.ps1 -Configuration Release -InstallRoot artifacts\publish\msi-smoke\upgrade
```

## Cleanup

Remove build/package/publish outputs and legacy build folders:

```powershell
powershell -File .\scripts\clean.ps1
```

Clean runtime cache, logs, scan temp data, and service jobs while preserving analysis import/export workspaces:

```powershell
powershell -File .\scripts\clean.ps1 -CleanOperationalData
```

Clean analysis import/export data explicitly:

```powershell
powershell -File .\scripts\clean.ps1 -CleanImportExportData
```

Return to a source-only local state without reverting Git changes:

```powershell
powershell -File .\scripts\reset-repo-state.ps1
```

## User Smoke Path

For a small manual smoke check:

1. Run `scripts/setup.ps1`.
2. Start `NtfsAudit.App`.
3. Add a small test folder.
4. Set an output folder for `.ntaudit`.
5. Keep service mode disabled unless testing service execution.
6. Run the scan.
7. Confirm the `.ntaudit` output exists.
8. Export Excel if that path is under test.

## Locale Smoke Path

The UI supports English and Italian through the language selector in the top toolbar. English is the default and fallback locale.

For a small locale smoke check:

1. Start `NtfsAudit.App`.
2. Confirm the first-run UI is English when no `ui-preferences.json` locale is present.
3. Switch the selector to `Italiano`.
4. Confirm toolbar, sidebar, tree, status, and dialog text update without restart.
5. Restart the app and confirm the selected locale is persisted.
