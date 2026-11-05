# Scripts

[Italian reference](README.it.md)

The root scripts directory exposes operator entrypoints. Technical helpers are grouped under build/, maintenance/, run/ and internal/.

## Main entrypoints

| Script | Purpose |
|---|---|
| install-dependencies.ps1 | Restore solution packages |
| build.ps1 | Build Release; accepts Configuration, Runtime win-x64 and Installer |
| package.ps1 | Stage App/Viewer/Service under artifacts/packages and compile NSIS installers |
| start-app.ps1 | Start the main WPF application |
| prepare-network-share-app.ps1 | Stage the shared application folder under artifacts/publish |
| clean-repo.ps1 | Explicit deletion of selected generated/runtime/import-export state |

Use the .NET 8 SDK selected by global.json and Windows desktop build support. A newer major SDK alone does not satisfy that pinned SDK resolution. Installer compilation also needs makensis.exe; installation and service operations can require elevation.

## Development flow

~~~powershell
pwsh -NoProfile -File scripts/install-dependencies.ps1
pwsh -NoProfile -File scripts/maintenance/check-prerequisites.ps1
pwsh -NoProfile -File scripts/build.ps1
pwsh -NoProfile -File scripts/maintenance/run-tests.ps1 -Configuration Release
~~~

Run start-app.ps1 only for an intended interactive smoke check. Build and package defaults write repository-local artifacts and project bin/obj. Managed verification must use an isolated source copy or separately verified output redirection inside the central runtime; changing TEMP alone does not redirect all outputs.

For an intended x64 installer build:

~~~powershell
pwsh -NoProfile -File scripts/build.ps1 -Runtime win-x64 -Installer
~~~

The generated packages are NSIS EXE installers, not MSI. The source supports the main app and read-only viewer distributions. Inspect the current package script for names/version and output layout rather than assuming a previously generated installer is current or signed.

## Technical directories

- build/: package layout and NSIS compilation.
- maintenance/: prerequisites, automated tests and explicit installer/service smoke operations.
- run/: secondary-component launchers.
- internal/: shared helpers, not independent operator workflows.

manage-service.ps1 and test-installer.ps1 require an explicit Action. Their install, start, stop, uninstall, cleanup and upgrade paths change machine state; they are not part of a read-only audit. Use a disposable host for installer/service qualification.

clean-repo.ps1 has OperationalData, ImportExportData and ResetState modes. Review the concrete deletion scope, references and recovery before invoking it. No cleanup is necessary merely to update a guide.

See [operations](../docs/development/operations.md), [architecture](../docs/architecture.md), [archive format](../docs/reference/archive-format.md), [credentials](../docs/reference/credentials.md).
