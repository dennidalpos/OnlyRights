# Credential Policy

Credential behavior is path-based and implemented by `ScanCredentialPathPolicy`.

## Effective Source

- Local paths (`C:\data`, extended local paths): always use the current Windows identity. Configured global credentials are ignored.
- UNC paths (`\\server\share`) and DFS namespaces: preserve the configured source when present. If no credential is configured, scanning uses the current Windows identity.
- WSL administrative shares (`\\wsl$\...`): treated as Windows-exposed UNC paths. ACL completeness depends on the Windows provider.
- Service jobs: credential payloads are protected for `LocalMachine` before they are handed to the service.
- No configured credential: impersonation is skipped and the process identity is used.

## Storage

Interactive app credentials can be read from legacy user-profile storage when migration is possible.

The common global credential store is `%ProgramData%\NtfsAudit\scan-credentials.json` and uses the `LocalMachine` DPAPI scope so the service can resolve background work queued from the app.

Service job credentials are protected independently for `LocalMachine`. The service does not read WPF UI state and does not reference `NtfsAudit.App`.

## Isolation

Credentials are not written to `.ntaudit` metadata, JSONL export records, or error records.

Tests cover archive metadata stripping, `ErrorEntry` serialization, `ExportRecord` serialization, and path-policy behavior for local, UNC, and DFS roots.
