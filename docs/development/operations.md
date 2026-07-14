# Operations

This repository is Windows-first. All versioned operational scripts are PowerShell scripts under `scripts/`. The root `scripts\` directory now exposes only the main operator entrypoints; technical scripts are grouped under `scripts\build`, `scripts\maintenance`, `scripts\run`, and `scripts\internal`.

## Prerequisites

Required:

- Windows.
- .NET SDK 8 compatible with `global.json`.
- `Microsoft.NET.Sdk.WindowsDesktop` support in the selected .NET SDK.

Optional capabilities checked by `scripts\maintenance\check-prerequisites.ps1`:

- `sc.exe` for Windows Service management.
- `msiexec.exe` for MSI smoke tests.
- WiX `candle.exe` and `light.exe` under `tools/wix314-binaries`.

## Recommended Operator Flow

From the repository root:

```powershell
pwsh -ExecutionPolicy Bypass -File .\scripts\install-dependencies.ps1
pwsh -File .\scripts\maintenance\check-prerequisites.ps1
pwsh -File .\scripts\build.ps1
pwsh -File .\scripts\start-app.ps1
```

For architecture-specific build and installer flows:

```powershell
pwsh -File .\scripts\build.ps1 -Runtime win-x64 -Installer
pwsh -File .\scripts\build.ps1 -Runtime win-x86 -Installer
```

To prepare the shared `app` folder:

```powershell
pwsh -File .\scripts\prepare-network-share-app.ps1
```
## Main Script Layout

- `scripts\install-dependencies.ps1`: restore dependencies.
- `scripts\build.ps1`: compiled build. Supports parameters `-Runtime`, `-Configuration`, and `-Installer`.
- `scripts\start-app.ps1`: start the main WPF app.
- `scripts\prepare-network-share-app.ps1`: stage the shared app folder under `artifacts\publish`.
- `scripts\clean-repo.ps1`: clean build/package/publish/temp repository outputs. Supports parameters `-OperationalData`, `-ImportExportData`, and `-ResetState`.

## Build and Package Technical Scripts

Canonical Release build remains:

```powershell
pwsh -File .\scripts\build.ps1
```

Technical package staging commands are:

```powershell
pwsh -File .\scripts\build\stage-package-layout.ps1 -Configuration Release
pwsh -File .\scripts\build\stage-package-layout.ps1 -Configuration Release -Runtime win-x64
pwsh -File .\scripts\build\stage-package-layout.ps1 -Configuration Release -Runtime win-x86
```

Current package behavior:

- Package staging is written under `artifacts\packages`.
- Builds without `-Runtime` prune non-Windows `runtimes\` subdirectories.
- The Viewer package removes duplicate `NtfsAudit.App` entrypoint files and keeps only the viewer executable entrypoint.

Self-contained package staging remains available as a technical command:

```powershell
pwsh -File .\scripts\build\stage-package-layout.ps1 -Configuration Release -Runtime win-x64 -SelfContained -PublishSingleFile
pwsh -File .\scripts\build\stage-package-layout.ps1 -Configuration Release -Runtime win-x86 -SelfContained -PublishSingleFile
```

## Installer Build

The repository produces MSI installers through WiX. Generated MSIs are built with:

- installation scope `perMachine`;
- elevated install privileges;
- Start Menu shortcut;
- Desktop shortcut.

Technical MSI build commands:

```powershell
pwsh -File .\scripts\build\build-installer.ps1 -Configuration Release
pwsh -File .\scripts\build\build-installer.ps1 -Configuration Release -Runtime win-x64
pwsh -File .\scripts\build\build-installer.ps1 -Configuration Release -Runtime win-x86
```

The main operator entrypoints are:

```powershell
pwsh -File .\scripts\build.ps1 -Runtime win-x64 -Installer
pwsh -File .\scripts\build.ps1 -Runtime win-x86 -Installer
```

The MSI build uses local WiX binaries from `tools\wix314-binaries`. When `-Version` is omitted, `scripts\build\build-installer.ps1` resolves the version from `Directory.Build.props`. Runtime-specific MSIs are staged under `artifacts\packages\<Configuration>\<Runtime>\<Framework>\installer`.

## Service Scripts

Windows Service management is consolidated under a single maintenance script:

```powershell
pwsh -File .\scripts\maintenance\manage-service.ps1 -Action Install -Configuration Release
pwsh -File .\scripts\maintenance\manage-service.ps1 -Action Start
pwsh -File .\scripts\maintenance\manage-service.ps1 -Action Stop
pwsh -File .\scripts\maintenance\manage-service.ps1 -Action Uninstall
pwsh -File .\scripts\maintenance\manage-service.ps1 -Action Cleanup
```

These actions can require elevation during execution.

## Tests and Smoke Tests

Run automated tests:

```powershell
pwsh -File .\scripts\maintenance\run-tests.ps1 -Configuration Release
```

MSI smoke tests:

```powershell
pwsh -File .\scripts\maintenance\test-installer.ps1 -Action Install -Configuration Release -InstallRoot artifacts\publish\msi-smoke\basic -SkipBuild
pwsh -File .\scripts\maintenance\test-installer.ps1 -Action Uninstall -Configuration Release -InstallRoot artifacts\publish\msi-smoke\basic
pwsh -File .\scripts\maintenance\test-installer.ps1 -Action Upgrade -Configuration Release -InstallRoot artifacts\publish\msi-smoke\upgrade
```

Manual app smoke path:

1. Run `scripts\install-dependencies.ps1`.
2. Run `scripts\maintenance\check-prerequisites.ps1`.
3. Run `scripts\build.ps1`.
4. Start `NtfsAudit.App`.
5. Add a small test folder.
6. Set an output folder for `.ntaudit`.
7. Keep service mode disabled unless testing service execution.
8. Run the scan.
9. Confirm the `.ntaudit` output exists.
10. Export Excel if that path is under test.

Locale smoke path:

1. Start `NtfsAudit.App`.
2. Confirm the first-run UI is English when no `ui-preferences.json` locale is present.
3. Switch the selector to `Italiano`.
4. Confirm toolbar, root input, tree, status, and dialog text update without restart.
5. Restart the app and confirm the selected locale is persisted.

## Cleanup

Clean repository outputs:

```powershell
pwsh -File .\scripts\clean-repo.ps1
```

Clean runtime cache, logs, scan temp data, and service jobs while preserving analysis workspaces:

```powershell
pwsh -File .\scripts\clean-repo.ps1 -OperationalData
```

Clean analysis import/export data explicitly:

```powershell
pwsh -File .\scripts\clean-repo.ps1 -ImportExportData
```

Return to a source-only local state without reverting Git changes:

```powershell
pwsh -File .\scripts\clean-repo.ps1 -ResetState
```

## CI

The GitHub Actions workflow is `.github\workflows\ci.yml`. It runs on `windows-latest` and uses the reorganized paths under `scripts\`.

CI runs:

1. repository clean;
2. dependency restore;
3. prerequisite checks;
4. Release build;
5. Release tests;
6. default and runtime-specific package staging;
7. shared app folder preparation;
8. MSI build;
9. Windows Service install/uninstall smoke;
10. MSI install, uninstall, and upgrade smoke;
11. final cleanup.

For exact commands, inspect `.github\workflows\ci.yml`; it remains the source of truth for CI sequencing.

## Release Boundaries

Build, tests, package staging, shared app folder preparation, MSI generation, service smoke, MSI smoke, deploy, signing, and release are separate phases.

The repository currently provides build, test, package staging, shared app-folder preparation, service smoke, and MSI smoke scripts. It does not provide an evidenced external deploy, signing, or release-publishing command. A build or MSI output must not be treated as a signed release.

## Maintenance

- Keep behavior, tests, scripts, CI, documentation, and `PROJECT_STATUS.json` aligned in the same change.
- Keep `PROJECT_STATUS.json` limited to real open residual work.
- Check `.gitignore` when adding generated outputs, logs, caches, temporary files, or machine-specific files.
- Monitor source files at or above 800 lines; evaluate files at or above 1500 lines for split/refactor work.
