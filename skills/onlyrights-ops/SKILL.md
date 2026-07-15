---
name: onlyrights-ops
description: Use this skill when building, testing, packaging, or installing the OnlyRights components, managing Windows Services, executing WiX MSI installer scripts, or running local diagnostics.
---

# OnlyRights Operations (DevOps & Scripts) Development Skill

This skill contains scripting, build, packaging, and maintenance rules for the OnlyRights suite.

## Documentation References

- **Operations & Commands**: Comprehensive script definitions, parameters, testing boundaries, and service commands in [operations.md](file:///d:/GITHUB/OnlyRights/docs/development/operations.md).

## Critical Guidelines

### 1. PowerShell Script Layout
- Always run the official PowerShell scripts under `scripts/` instead of raw `dotnet` or `msbuild` commands.
- Entrypoint scripts must be located at the root of `scripts/` (e.g. `build.ps1`, `start-app.ps1`).
- Supporting and technical scripts are grouped in folders:
  - `build/`: Technical scripts for package layout staging and installer compilation.
  - `maintenance/`: System verification, service management, cleanups, and testing.
  - `run/`: Secondary runners.
  - `internal/`: Shared helper functions (no direct entrypoints).

### 2. Main Workflows
- **Dependency Restore**: `pwsh -ExecutionPolicy Bypass -File .\scripts\install-dependencies.ps1`
- **Build Solution**: `pwsh -File .\scripts\build.ps1` (compiles Release by default; supports `-Runtime` and `-Installer`).
- **Run Application**: `pwsh -File .\scripts\start-app.ps1`
- **Run Tests**: `pwsh -File .\scripts\maintenance\run-tests.ps1 -Configuration Release`

### 3. Packaging & WiX Installers
- MSIs are generated using WiX 3.14 (located under `tools\wix314-binaries`).
- MSI compilation commands are under `scripts\build\build-installer.ps1`, which compiles a pair of installers in tandem: one for the main scanner application and service, and one for the read-only audit viewer.
- Operators use:
  - `pwsh -File .\scripts\build.ps1 -Runtime win-x64 -Installer`
  - `pwsh -File .\scripts\build.ps1 -Runtime win-x86 -Installer`
- Generated installers must run with `perMachine` scope and require elevation.
- **Staging Policy**: Staging the package layout (`stage-package-layout.ps1`) is executed on every build-installer run (unless explicitly skipped using `-SkipPack`) to ensure the MSI is compiled with current binaries rather than old artifacts.
- **Upgrades Policy**: The WiX configuration enables `AllowSameVersionUpgrades="yes"` in `<MajorUpgrade>` to ensure that successive development builds of the same version clean up the previous installation and do not create duplicate entries in Windows Installed Applications.
- **Application Elevation**: The WPF desktop application and the Viewer are configured to require administrative execution level, enforcing UAC elevation on startup to ensure deep scanning capabilities.

### 4. Windows Service Management
- Managed via `manage-service.ps1` under `scripts\maintenance\`:
  - Install: `manage-service.ps1 -Action Install -Configuration Release`
  - Start: `manage-service.ps1 -Action Start`
  - Stop: `manage-service.ps1 -Action Stop`
  - Uninstall: `manage-service.ps1 -Action Uninstall`
  - Cleanup: `manage-service.ps1 -Action Cleanup`
- Service status files and jobs are staged under `%ProgramData%\NtfsAudit\`.
