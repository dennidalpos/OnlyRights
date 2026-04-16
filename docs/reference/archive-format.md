# Archive Format

`.ntaudit` files are ZIP archives produced by `AnalysisArchive`.

## Current Entries

Modern archives contain:

- `data.jsonl`: newline-delimited `ExportRecord` rows.
- `errors.jsonl`: newline-delimited scan error rows. The entry exists even when empty.
- `tree.json`: folder tree map used by the app and viewer.
- `folderflags.json`: per-folder display and risk flags.
- `analysis.sqlite`: SQLite payload used for lazy detail loading on large archives.
- `meta.json`: archive metadata, root path, root path kind, timestamp, version, counts, and archive-safe scan options.

`analysis.sqlite` is mandatory for archives exported by the current code. Import remains tolerant for older archives where compatibility rules allow JSON-only loading.

## Versions

- v1-v2: legacy imports tolerate malformed rows that cannot be parsed.
- v3-v4: structured metadata and tree data are expected when present.
- v5-v6: data and error record counts are validated when metadata provides them.
- v7: current format. `analysis.sqlite`, `folderflags.json`, strict metadata, and archive-safe scan options are written.

The current exporter writes version 7. Import accepts legacy `RootPathKind` values of `Nfs` or numeric `4` and maps them to UNC because native NFS scanning is not supported.

## SQLite Loading

Archives with 5000 or more data rows use `analysis.sqlite` lazily for folder detail loading. Smaller imports can read `data.jsonl` directly while still carrying the SQLite payload in modern archives.

## Credential Stripping

Archive metadata uses `ScanOptions.CreateArchiveSafeCopy()`. Credential objects, credential source, passwords, protected password payloads, and DPAPI scope values are stripped before `meta.json` is written.

`ExportRecord` and `ErrorEntry` serialization do not carry credential configuration fields. Credential behavior is documented in [credential policy](credentials.md).
