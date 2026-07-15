---
name: onlyrights-core
description: Use this skill when working on the NtfsAudit.Core scanning, ACL analysis, SID resolution, Excel exporter, credential DPAPI security, and SQLite analysis archive format.
---

# OnlyRights Core (NtfsAudit.Core) Development Skill

This skill contains technical guidelines and rules for developing and maintaining the Core component of the NtfsAudit suite.

## Documentation References

- **Architecture Details**: Deep-dive on architecture, layers, and components in [architecture.md](file:///d:/GITHUB/OnlyRights/docs/architecture.md).
- **Archive Format Specification**: Structure, versions, and SQLite lazy-loading rules for `.ntaudit` in [archive-format.md](file:///d:/GITHUB/OnlyRights/docs/reference/archive-format.md).
- **Credential Policy Reference**: DPAPI scopes, impersonation, and isolation guarantees in [credentials.md](file:///d:/GITHUB/OnlyRights/docs/reference/credentials.md).

## Critical Guidelines

### 1. Filesystem ACL Scanning & Permissions
- NTFS scanning must handle NTFS security descriptor DACLs, resolving individual Access Control Entries (ACEs).
- UNC / DFS paths must support optional SMB share permission collection when requested. Non-Windows systems or NAS devices (e.g. Synology Samba shares) do not support WMI; thus, the share permission loader must fall back dynamically to Win32 `NetShareGetInfo` to read and parse the share's security descriptor without raising RPC errors.
- Supported path types: local paths, SMB/CIFS UNC paths, DFS namespaces, extended paths, and mapped drives.
- WSL administrative shares (`\\wsl$\...`) are scanned via standard Windows UNC providers.
- **GDPR Anonymization**: When `ScanOptions.AnonymizeIdentities` is `true`, all custom/user-specific SIDs and Names (e.g., S-1-5-21-...) are mapped to consistent pseudonyms (e.g., `User_1`, `Group_2` / `S-1-5-21-0-0-1-1`, `S-1-5-21-0-0-2-2`) during scanning, and user profile segments in paths (under `\Users\` and `\profiles\`) are masked. Well-known system SIDs are preserved for security audit clarity.

### 2. Impersonation & Credential Policy
- **Local Path Impersonation**: Local paths (e.g. `C:\data`) must always use the process identity. Configured credentials must be ignored.
- **UNC & DFS Path Impersonation**: Preserve the configured credentials if present. If no credential is configured, fallback to process identity.
- **Service Job Scope**: Job credentials must be DPAPI-protected using `LocalMachine` scope so the background service (running under a service account) can decrypt and execute the scan.
- **Credential Isolation**: Security credentials, passwords, and tokens must never be written to:
  - `.ntaudit` archive metadata (`meta.json`).
  - Newline-delimited JSONL export records (`data.jsonl`).
  - Scan error entries (`errors.jsonl`).
  - Use `ScanOptions.CreateArchiveSafeCopy()` to strip credentials before writing metadata.
- **Access Control Hardening**: Sensitive folders and files (such as `%ProgramData%\NtfsAudit\scan-credentials.json`, schedules, and runtime snapshots) must have strict ACLs applied via `SecurityHardeningHelper` to permit access ONLY to SYSTEM, Administrators, and the active Windows identity.

### 3. Archive Format (`.ntaudit`)
- Archives are ZIP files. Version 7 is strictly enforced and required.
- Legacy import support for versions 1 to 6 has been removed.
- **Mandatory Entries**:
  - `data.jsonl`: Newline-delimited `ExportRecord` rows.
  - `errors.jsonl`: Newline-delimited scan errors. Must exist (even if empty).
  - `tree.json`: Folder tree maps for the WPF viewer/app.
  - `folderflags.json`: Per-folder display and risk flags.
  - `analysis.sqlite`: Mandatory SQLite payload.
  - `meta.json`: Metadata, timestamp, counts, and archive-safe scan options.
- **Lazy Loading**: Archives with 5000+ data rows must load folder details lazily from `analysis.sqlite` instead of loading all jsonl lines.
- **Archive Hardening**: Exported `.ntaudit` zip archives must have strict ACLs applied via `SecurityHardeningHelper` upon creation to restrict access solely to SYSTEM, Administrators, and the owner.

### 4. Greenfield Development Policy
- The project is developed as a fresh greenfield application.
- Do NOT implement legacy compatibility layers, backward-compatibility shims, transitional logic, historical cleanups, or support migrations from previous/older database versions, older archive versions (v1-v6), or older credential store layouts.
- Only the standard v7 archive format and standard `%ProgramData%` credential store must be supported.
