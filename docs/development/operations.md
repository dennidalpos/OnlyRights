# Operations

This repository is Windows-first. All versioned operational scripts are PowerShell scripts under `scripts/`. The repository does not define Bash, WSL, npm, lint, format, external deploy, signing, or release-publishing workflows.

## Prerequisites

Required:

- Windows.
- .NET SDK 8 compatible with `global.json`.
- `Microsoft.NET.Sdk.WindowsDesktop` support in the selected .NET SDK.

Optional capabilities checked by `scripts/doctor.ps1`:

- `sc.exe` for Windows Service management.
- `msiexec.exe` for MSI smoke tests.
- WiX `candle.exe` and `light.exe` under `tools/wix314-binaries`.

## Setup

Recommended first-run setup:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1
```

Setup runs bootstrap, doctor, and a Release build. Use `-SkipBuild` to run only bootstrap and doctor:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1 -SkipBuild
```

Use `-RequireOptionalTools` when service and MSI tooling must be present:

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

`scripts/build.ps1` is the canonical build entrypoint used by CI. It delegates to `scripts/compile.ps1`, which handles restore/build orchestration and project/framework filtering.

Test:

```powershell
powershell -File .\scripts\test.ps1 -Configuration Release
```

Package:

```powershell
powershell -File .\scripts\pack.ps1 -Configuration Release
```

Publish local package output:

```powershell
powershell -File .\scripts\publish.ps1 -Configuration Release
```

Run the main app:

```powershell
dotnet run --project .\src\NtfsAudit.App\NtfsAudit.App.csproj -f net8.0-windows
```

Run the read-only viewer:

```powershell
dotnet run --project .\src\NtfsAudit.Viewer\NtfsAudit.Viewer.csproj -f net8.0-windows
```

## CI

The GitHub Actions workflow is `.github/workflows/ci.yml`. It runs on `windows-latest` for pushes to `main`, `master`, `codex/**`, and for pull requests.

CI runs:

1. clean;
2. bootstrap;
3. doctor;
4. Release build;
5. Release tests;
6. default package;
7. `win-x64` and `win-x86` packages;
8. local publish;
9. `net6.0-windows` App/Viewer build, package, and publish;
10. default and x86 MSI builds;
11. service install/uninstall smoke;
12. MSI install, uninstall, and upgrade smoke;
13. final cleanup.

For exact commands, inspect `.github/workflows/ci.yml`; it is the source of truth for CI sequencing.

## Packaging

`scripts/pack.ps1` creates publish-style package directories under `artifacts/packages`. App, Viewer, and Service outputs include `NtfsAudit.Core` through project references.

Default framework-dependent package:

```powershell
powershell -File .\scripts\pack.ps1 -Configuration Release
```

RID-specific packages:

```powershell
powershell -File .\scripts\pack.ps1 -Configuration Release -Runtime win-x64
powershell -File .\scripts\pack.ps1 -Configuration Release -Runtime win-x86
```

`win-x64` and `win-x86` outputs are staged under `artifacts/packages/<Configuration>/<Runtime>/<Framework>`. Matching build outputs use separated `x64` or `x86` platform targets under `artifacts/build`.

`net6.0-windows` package commands must use `-SkipService` because `NtfsAudit.Service` targets `net8.0-windows` only.

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

The MSI build uses local WiX binaries from `tools/wix314-binaries`. Runtime-specific MSIs are staged under `artifacts/packages/<Configuration>/<Runtime>/<Framework>/installer` and include an architecture suffix, such as `OnlyRights-NtfsAudit-1.0.0-x64.msi` or `OnlyRights-NtfsAudit-1.0.0-x86.msi`.

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

## Smoke Tests

MSI install:

```powershell
powershell -File .\scripts\packaging\msi-install-test.ps1 -Configuration Release -InstallRoot artifacts\publish\msi-smoke\basic -SkipBuild
```

MSI uninstall:

```powershell
powershell -File .\scripts\packaging\msi-uninstall-test.ps1 -Configuration Release -InstallRoot artifacts\publish\msi-smoke\basic
```

MSI upgrade:

```powershell
powershell -File .\scripts\packaging\msi-upgrade-test.ps1 -Configuration Release -InstallRoot artifacts\publish\msi-smoke\upgrade
```

Manual app smoke path:

1. Run `scripts/setup.ps1`.
2. Start `NtfsAudit.App`.
3. Add a small test folder.
4. Set an output folder for `.ntaudit`.
5. Keep service mode disabled unless testing service execution.
6. Run the scan.
7. Confirm the `.ntaudit` output exists.
8. Export Excel if that path is under test.

Locale smoke path:

1. Start `NtfsAudit.App`.
2. Confirm the first-run UI is English when no `ui-preferences.json` locale is present.
3. Switch the selector to `Italiano`.
4. Confirm toolbar, sidebar, tree, status, and dialog text update without restart.
5. Restart the app and confirm the selected locale is persisted.

## Cleanup

Remove build, package, publish outputs, and legacy build folders:

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

## Release Boundaries

Build, tests, packaging, local publish, service smoke, MSI smoke, deploy, signing, and release are separate phases.

The repository currently provides build, test, package, local publish, service smoke, and MSI smoke scripts. It does not provide an evidenced external deploy, signing, or release-publishing command. A build or package result must not be treated as a signed release.

## Maintenance

- Keep behavior, tests, scripts, CI, documentation, and `PROJECT_STATUS.json` aligned in the same change.
- Keep `PROJECT_STATUS.json` limited to real open residual work.
- Check `.gitignore` when adding generated outputs, logs, caches, temporary files, or machine-specific files.
- Monitor source files at or above 800 lines; evaluate files at or above 1500 lines for split/refactor work.
