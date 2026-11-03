# Security Policy

## Supported versions

Only the latest commit on the `main` branch is supported.

## Reporting a vulnerability

Please do not open a public issue for security problems.

Use GitHub's private vulnerability reporting: open the **Security** tab of this repository and choose **Report a vulnerability**. Include the affected component (`NtfsAudit.App`, `NtfsAudit.Viewer`, `NtfsAudit.Service` or the archive format), reproduction steps and the impact you observed. Remove real paths, account names and audit data from any evidence you attach.

You can expect an acknowledgement within a few days. Confirmed issues are fixed on `main` and disclosed after a fix is available.

## Scope notes

- Stored scan credentials are protected with Windows DPAPI; see the credential policy in `docs/reference/credentials.md`.
- `.ntaudit` archives and Excel exports contain permission data of the scanned paths; handle them as sensitive.
- Dependencies are monitored with Dependabot and secret scanning with push protection.
