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

- v7: Only supported format version. The importer strictly requires version 7. All older archive versions (v1-v6) are not supported.

The current exporter writes version 7. Importing older versions or legacy RootPathKind values such as "Nfs" or "4" is not supported.

## SQLite Loading

Archives with 5000 or more data rows use `analysis.sqlite` lazily for folder detail loading. Smaller imports can read `data.jsonl` directly while still carrying the SQLite payload in modern archives.

## Credential Stripping

Archive metadata uses `ScanOptions.CreateArchiveSafeCopy()`. Credential objects, credential source, passwords, protected password payloads, and DPAPI scope values are stripped before `meta.json` is written.

`ExportRecord` and `ErrorEntry` serialization do not carry credential configuration fields. Credential behavior is documented in [credential policy](credentials.md).
