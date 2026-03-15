# Project Specification

## Goal
Fornire una suite Windows per analizzare permessi NTFS e share SMB, esportare i risultati, archiviarli in formato `.ntaudit` e riaprirli in modalità sola lettura.

## Scope
- Scansione ACL NTFS su percorsi locali, UNC, DFS e NFS.
- Raccolta opzionale di permessi share SMB.
- Risoluzione identità e gruppi per principal SID/AD.
- Calcolo permessi effettivi, metriche di rischio e baseline ACL.
- Export dei risultati in `.xlsx` e `.ntaudit`.
- Import e visualizzazione di archivi `.ntaudit`.
- Payload SQLite read-only per consultazione scalabile di dataset grandi.
- Esecuzione locale oppure tramite Windows Service.
- Quarantena dei job service corrotti/non validi con aggiornamento dello stato runtime.
- Script PowerShell per build, publish e pulizia artefatti/dati operativi.

## Non Scope
- Supporto multipiattaforma non Windows.
- Gestione o modifica diretta delle ACL sul filesystem remoto o locale.
- Persistenza centralizzata server-side o database applicativo.
- Interfacce web o API HTTP pubbliche.

## Architecture
- `src/NtfsAudit.App`: applicazione WPF principale con UI, view model, pipeline di scan, servizi di risoluzione identità, export/import archivio, payload SQLite e calcolo permessi.
- `src/NtfsAudit.Service`: worker service Windows che esegue job di scansione in background usando la logica condivisa dell'app.
- `src/NtfsAudit.Viewer`: client WPF in sola lettura per aprire archivi `.ntaudit`, anche da argomento/percorsi di rete, con caricamento lazy da SQLite per archivi grandi.
- `tests/NtfsAudit.App.Tests`: test unitari sui componenti core di path resolution, permission calculation, archive import/export e filtri.
- `src/NtfsAudit.App/ViewModels/MainViewModel*.cs`: partial class separate per stato, comandi, esecuzione scan e runtime/UI lifecycle.
- `scripts/build.ps1` e `scripts/clean.ps1`: automazione locale per restore/build/test/publish e pulizia.

## Constraints
- Repository orientato a Windows e target `net6.0-windows` / `net8.0-windows`.
- Il service supporta solo `net8.0-windows`.
- L'SDK locale/CI deve essere pinato tramite `global.json` su toolchain .NET 8 supportata.
- I workspace `%TEMP%\\NtfsAudit\\imports` e `%TEMP%\\NtfsAudit\\exports` non devono essere cancellati distruttivamente in shutdown/cleanup generici; sono gestiti con retention dedicata.
- Gli archivi `.ntaudit` devono includere `analysis.sqlite` per consentire query/read-only efficienti sui dataset grandi.
- La coerenza tra codice, documentazione e `PROJECT_STATUS.json` va mantenuta ad ogni modifica.
- File >=800 linee devono essere monitorati; file >=1500 linee vanno valutati per refactor/split.
