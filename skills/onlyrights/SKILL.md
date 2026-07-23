---
name: onlyrights
description: General development entrypoint for OnlyRights C#/.NET 8 WinUI/WPF NtfsAudit application suite. Points to specialized sub-skills for core logic, WPF UI, and DevOps operations.
---

# OnlyRights Development Umbrella Skill

This umbrella skill serves as the primary developer guide and runtime instructions for agents working on the [OnlyRights](file:///d:/GITHUB/OnlyRights) repository. 

Development is divided into three specialized sub-skills which must be loaded based on the component being modified:

## Specialized Development Skills

1. **[onlyrights-core](file:///d:/GITHUB/OnlyRights/skills/onlyrights-core/SKILL.md)**: 
   Use this skill when working on scanning, ACL calculations, SID mapping, SQLite analysis archive format, and DPAPI credential security.
   
2. **[onlyrights-app](file:///d:/GITHUB/OnlyRights/skills/onlyrights-app/SKILL.md)**: 
   Use this skill when working on WPF UI elements, MVVM ViewModels, resources dictionaries, localization (English/Italian), and Viewer bootstrapping.
   
3. **[onlyrights-ops](file:///d:/GITHUB/OnlyRights/skills/onlyrights-ops/SKILL.md)**: 
   Use this skill when executing PowerShell scripts, restoring dependencies, building solution binaries, packaging NSIS 64-bit installers, or installing the background Windows Service.

## Documentation References

- **Architecture Details**: Deep-dive on architecture, layers, components, and data directories in [architecture.md](file:///d:/GITHUB/OnlyRights/docs/architecture.md).
- **Operations & Commands**: Comprehensive script definitions, parameters, testing boundaries, and service commands in [operations.md](file:///d:/GITHUB/OnlyRights/docs/development/operations.md).
- **Archive Format Specification**: Structure, versions, and SQLite lazy-loading rules for `.ntaudit` in [archive-format.md](file:///d:/GITHUB/OnlyRights/docs/reference/archive-format.md).
- **Credential Policy Reference**: Impersonation, DPAPI scopes, and isolation guarantees in [credentials.md](file:///d:/GITHUB/OnlyRights/docs/reference/credentials.md).
