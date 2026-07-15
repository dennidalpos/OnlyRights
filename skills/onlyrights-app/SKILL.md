---
name: onlyrights-app
description: Use this skill when developing or modifying the NtfsAudit.App WPF desktop application, NtfsAudit.Viewer read-only client, XAML layouts, localization dictionaries, and MVVM ViewModels.
---

# OnlyRights WPF App (NtfsAudit.App & NtfsAudit.Viewer) Development Skill

This skill contains UI design guidelines and rules for developing and maintaining the WPF Desktop components.

## Documentation References

- **Architecture Details**: Layout and interactions of the WPF Application, Viewer, and Resource Dictionaries in [architecture.md](file:///d:/GITHUB/OnlyRights/docs/architecture.md).

## Critical Guidelines

### 1. WPF Architecture & MVVM Pattern
- **Main WPF Client (`NtfsAudit.App`)**: Houses MVVM ViewModels, Commands, Views, and Localization resources.
- **Read-Only Viewer (`NtfsAudit.Viewer`)**: boots `NtfsAudit.App.MainWindow` and `NtfsAudit.App.ViewModels.MainViewModel` through a direct project reference.
- **Service Isolation**: The Worker Service host (`NtfsAudit.Service`) must NOT reference the WPF application project (`NtfsAudit.App`).
- **Anonymization Option**: The view model supports `AnonymizeIdentities` binding which is saved in `ui-preferences.json` and passed in scan execution options.

### 2. Localization (`LocalizationManager`)
- Supported locales: English (`en`, default/fallback) and Italian (`it`).
- UI locale preferences are stored in `ui-preferences.json`.
- UI strings must be updated dynamically without requiring application restarts.
- Localization files are stored as XAML Resource Dictionaries under `src/NtfsAudit.App/Resources`.
- **Hardening Keys**: `Scan.AnonymizeIdentities` must be defined in both English and Italian dictionaries to label the GDPR checkbox.

### 3. Resource Dictionaries & Shared Views
- Shared styles and UI resources must be loaded dynamically by merging `SharedResources.xaml`.
- Merged dictionary check: Use `App.EnsureSharedResourcesLoaded(resources)` when initializing resources in secondary entrypoints to avoid duplicating resource definitions.
- Dynamic theme or style changes must adhere to the defined WPF layout rules.

### 4. Layout Alignment & Typography Guidelines
- **Path Inputs Alignment**: Forms containing paths must layout the path `TextBox` left-aligned (occupying column 0 with `Width="*"`) and group any control buttons (such as `Browse` or `Add`) on the right (using `Width="Auto"`). This ensures consistent left-justification of text fields.
- **Global Typography**: Centralized window properties like `FontFamily` should be set globally via the default `Style TargetType="Window"` in `SharedResources.xaml` rather than duplicated in local views.

### 5. UI Controls & Badges Styling
- **Active Tab Highlight**: Selected TabItems in TabControl styles use a 3px top accent color border highlight to visually distinguish the active tab.
- **Folder Tree Badges**: Folder nodes display shortened status badges (e.g. NTFS explicit permissions, explicit denies, files presence, protected inheritance) which are mapped dynamically to centralized localization strings.
- **Rights Legend Alignment**: The rights legend must match the exact badges and abbreviations used in the results grid to maintain visual consistency.
