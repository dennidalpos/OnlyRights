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
- Esecuzione locale oppure tramite Windows Service opzionale come host per scansioni background, non interattive o lunghe, senza funzionalita applicative aggiuntive.
- Credenziali scansione a due livelli con credenziali globali applicative, override per singola root e fallback finale all'utente corrente.
- Quarantena dei job service corrotti/non validi con aggiornamento dello stato runtime.
- Script PowerShell canonici per bootstrap, doctor, compile, build, test, pack, publish e clean.

## Non Scope
- Supporto multipiattaforma non Windows.
- Gestione o modifica diretta delle ACL sul filesystem remoto o locale.
- Persistenza centralizzata server-side o database applicativo.
- Interfacce web o API HTTP pubbliche.

## Architecture
- `src/NtfsAudit.App`: applicazione WPF principale con UI, view model, pipeline di scan, servizi di risoluzione identità, export/import archivio, payload SQLite e calcolo permessi.
- `src/NtfsAudit.Service`: worker service Windows opzionale che ospita job di scansione in background usando la stessa logica condivisa dell'app, senza introdurre funzionalita applicative dedicate.
- `src/NtfsAudit.Viewer`: client WPF in sola lettura per aprire archivi `.ntaudit`, anche da argomento/percorsi di rete, con caricamento lazy da SQLite per archivi grandi.
- `tests/NtfsAudit.App.Tests`: test unitari sui componenti core di path resolution, permission calculation, archive import/export e filtri.
- `src/NtfsAudit.App/ViewModels/MainViewModel*.cs`: partial class separate per stato, comandi, esecuzione scan e runtime/UI lifecycle.
- `src/NtfsAudit.App/Services/ScanCredential*.cs`: persistenza locale protetta DPAPI, sanitizzazione export e payload macchina per i job del servizio.
- `scripts/*.ps1`: entrypoint canonici del repository; `scripts/helpers/common.ps1` centralizza il contesto condiviso, `scripts/windows/*.ps1` governa il ciclo di vita del servizio Windows e `scripts/packaging/*.ps1` governa il packaging MSI con tool locali WiX.

## Constraints
- Repository orientato a Windows e target `net6.0-windows` / `net8.0-windows`.
- Il service supporta solo `net8.0-windows`.
- L'SDK locale/CI deve essere pinato tramite `global.json` su toolchain .NET 8 supportata.
- Gli output persistenti devono essere centralizzati dalla root sotto `artifacts/` (`artifacts/build`, `artifacts/test-results`, `artifacts/packages`, `artifacts/publish`).
- I workspace `%TEMP%\\NtfsAudit\\imports` e `%TEMP%\\NtfsAudit\\exports` non devono essere cancellati distruttivamente in shutdown/cleanup generici; sono gestiti con retention dedicata.
- Le credenziali scansione persistite localmente devono essere protette tramite DPAPI; i job del servizio devono ricevere solo payload credenziali protetti per `LocalMachine`, mai password in chiaro.
- Gli archivi `.ntaudit` devono includere `analysis.sqlite` per consentire query/read-only efficienti sui dataset grandi.
- La coerenza tra codice, documentazione e `PROJECT_STATUS.json` va mantenuta ad ogni modifica.
- File >=800 linee devono essere monitorati; file >=1500 linee vanno valutati per refactor/split.
