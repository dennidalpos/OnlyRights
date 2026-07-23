# OnlyRights Workspace Directives

These guidelines apply to all agents, assistants, and developers working on the **OnlyRights** codebase.

## Developer Skills Management

- **Read & Load Skills**: You must load and read the relevant developer skills (`onlyrights`, `onlyrights-core`, `onlyrights-app`, `onlyrights-ops`) before performing any development, build, test, packaging, or deployment operations.
- **Maintain Skill Updates**: When making code modifications, you **MUST** update the corresponding custom skill files at:
  - [skills/onlyrights-core/SKILL.md](file:///d:/GITHUB/OnlyRights/skills/onlyrights-core/SKILL.md) for core/scanning/archive/credentials changes.
  - [skills/onlyrights-app/SKILL.md](file:///d:/GITHUB/OnlyRights/skills/onlyrights-app/SKILL.md) for WPF/XAML/MVVM/Viewer changes.
  - [skills/onlyrights-ops/SKILL.md](file:///d:/GITHUB/OnlyRights/skills/onlyrights-ops/SKILL.md) for PowerShell scripts/NSIS installer/service commands changes.
  - [skills/onlyrights/SKILL.md](file:///d:/GITHUB/OnlyRights/skills/onlyrights/SKILL.md) for high-level structure alignment.
- **Synchronized alignment**: Keep all developer skills, official docs (`docs/`), tests (`tests/`), PowerShell scripts (`scripts/`), and [PROJECT_STATUS.json](file:///d:/GITHUB/OnlyRights/PROJECT_STATUS.json) in complete sync during any code change.

## Key Design Principles

- **Windows-First**: Only implement Windows-targeted, C#/WPF/Service, and PowerShell logic. No Linux, Bash, WSL, web, or HTTP API changes are permitted.
- **Credential Protection**: Strictly respect the DPAPI machine/user scope policy. Do NOT leak credential payloads in exports, metadata, or logs.
- **SQLite Archive Architecture**: Always guarantee version 7 compatibility for `.ntaudit` archive structures, ensuring the SQLite payload is written for large-scale lazy loading.
- **Greenfield Policy & No Legacy Migrations**: Treat the project as a fresh greenfield development. Do NOT write legacy compatibility layers, backward-compatibility shims, transitional logic, historical cleanups, or support migrations from previous/older database versions, older archive versions (v1-v6), or older credential store layouts. Only support standard version 7 archives and the standard `%ProgramData%` credential store.
