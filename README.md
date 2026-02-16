# NTFS Audit

Suite Windows per analisi ACL NTFS/SMB con UI interattiva, coda job via servizio Windows e viewer in sola lettura.

## Componenti

- **NtfsAudit.App (WPF)**
  - configurazione multi-root,
  - scansione locale o invio job al servizio,
  - modalità servizio abilitata di default per mantenere l'analisi attiva anche dopo logout,
  - filtri ACL e filtri albero,
  - export `.xlsx` e archivio `.ntaudit`,
  - import archivio `.ntaudit` con ripristino stato analisi.
- **NtfsAudit.Service (Windows Service)**
  - polling job queue in `%ProgramData%\NtfsAudit\jobs`,
  - esecuzione sequenziale delle root,
  - pubblicazione stato runtime in `%ProgramData%\NtfsAudit\service-status.json`.
- **NtfsAudit.Viewer**
  - apertura archivi `.ntaudit` in modalità sola lettura.

## Architettura repository

- `src/NtfsAudit.App`
  - `ViewModels/MainViewModel.cs`: orchestrazione scansione, import/export, logica checkbox, logica filtri ACL/albero.
  - `Services/AnalysisArchive.cs`: serializzazione/deserializzazione archivio analisi.
  - `Export/ExcelExporter.cs`: generazione workbook OpenXML a fogli multipli.
- `src/NtfsAudit.Service`
  - `ScanWorker.cs`: loop servizio, lettura job e aggiornamento stato runtime.
- `tests/NtfsAudit.App.Tests`
  - test unitari su servizi core.
- `scripts/build.ps1`
  - restore/build/test/publish + clean opzionale integrato.
- `scripts/clean.ps1`
  - pulizia selettiva build output, cache, temp, export, job, stato servizio.

## Processo import/export analisi

### Export Excel (`.xlsx`)

- L’export legge il file JSONL della scansione e produce:
  - fogli separati `Users_n` e `Groups_n`,
  - foglio `Errors`.
- Se il numero righe supera il limite Excel per foglio, crea fogli incrementali (`Users_2`, `Groups_2`, ...).
- Le colonne includono path, principal, layer (NTFS/Share/Effective), rights, ownership, audit/risk e SID.
- La scrittura è atomica: il file viene costruito su file temporaneo e poi sostituito in output.

### Export analisi (`.ntaudit`)

- L’archivio contiene:
  - `data.jsonl` (dati ACL),
  - `errors.jsonl` (errori scansione),
  - `tree.json` (struttura albero),
  - `folderflags.json` (flag per nodo: esplicito, inheritance disabled, diff baseline),
  - `meta.json` (root, tipo path, timestamp, opzioni scansione).
- Le informazioni vengono salvate con path normalizzati per mantenere coerenza fra ambienti.

### Import analisi (`.ntaudit`)

- L’archivio viene estratto in cartella temporanea `...\NtfsAudit\imports\...`.
- Se metadati opzionali mancano, l’import effettua fallback su dati disponibili (`data.jsonl`).
- Il timestamp importato viene normalizzato (UTC); in assenza di timestamp valido usa il `LastWriteTimeUtc` del file archivio.
- Le entry ACL importate preservano i flag `IsServiceAccount` / `IsAdminAccount` esportati e applicano fallback su classificazione SID.
- Dopo import:
  - vengono applicate opzioni scansione,
  - vengono ricalcolati albero/filtri,
  - vengono ripopolati errori e pannelli dettaglio.

## Logica checkbox di configurazione scansione

### Profondità

- **Analizza tutte le sottocartelle** (`ScanAllDepths`)
  - se attivo, `MaxDepth = int.MaxValue`.
  - se disattivo, è abilitato l’input profondità (`IsMaxDepthEnabled = true`) e il valore viene clampato a limiti validi.
- Le cartelle di sistema DFSR/DFRS vengono escluse automaticamente dalla scansione (es. `System Volume Information\DFSR`, `DfsrPrivate`, `ConflictAndDeleted`, `Staging`, `PreExisting`).

### Identità

- **Risolvi identità** (`ResolveIdentities`) abilita/disabilita:
  - `ExpandGroups`,
  - `UsePowerShell`,
  - `ExcludeServiceAccounts`,
  - `ExcludeAdminAccounts`.
- Se disattivata, i valori dipendenti vengono azzerati per evitare configurazioni incoerenti.

### Audit avanzato

- **Abilita audit avanzato** (`EnableAdvancedAudit`) governa:
  - `ComputeEffectiveAccess`,
  - `IncludeSharePermissions`,
  - `IncludeFiles`,
  - `ReadOwnerAndSacl`,
  - `CompareBaseline`.
- Se disattivata, le opzioni figlie vengono azzerate.

## Logica filtri ACL (grid utenti/gruppi/all/share/effective)

I filtri ACL combinano categorie booleane + filtro testuale.

### Filtri booleani

- Allow / Deny: almeno una categoria sempre attiva.
- Ereditato / Esplicito: almeno una categoria sempre attiva.
- Protected / Disabled: esclusione diretta delle entry con flag corrispondenti.
- Categorie principal:
  - Everyone,
  - Authenticated Users,
  - Service Accounts,
  - Admin Accounts,
  - Other Principals.
- Se tutte le categorie principal vengono disattivate, il sistema riattiva automaticamente `Other Principals`.

### Classificazione principal

- `Everyone` e `Authenticated Users` sono identificati da SID noti e alias testuali.
- `Service/Admin` usano:
  - flag dell’entry,
  - fallback su classificazione SID quando i flag non sono valorizzati.

### Filtro testuale (`AclFilter`)

Ricerca case-insensitive su:
- nome principal, SID,
- layer e Allow/Deny,
- rights, folder/target path, owner,
- share info, risk/audit/source/path kind,
- membri gruppo (`MemberNames`).

## Logica filtri albero

Filtri disponibili:
- solo nodi con ACL esplicite,
- solo inheritance disabled,
- solo differenze vs baseline,
- solo deny espliciti,
- solo mismatch baseline,
- solo file / solo cartelle.

Regole:
- file e cartelle non possono essere entrambi disattivati contemporaneamente;
- il reset filtri ripristina vista completa;
- il reload albero usa i dettagli correnti senza modificare dati sorgente.

## Script build / clean

## `scripts/build.ps1`

Pipeline:
- restore,
- build,
- test,
- publish (App / Viewer / Service).

Flag principali:
- `-Configuration Release|Debug`
- `-SkipRestore`, `-SkipBuild`, `-SkipTests`, `-SkipPublish`
- `-Framework`, `-Runtime`, `-SelfContained`
- `-PublishSingleFile`, `-PublishReadyToRun`
- `-RunClean` (delegato a `clean.ps1`)

Flag di pulizia supportati anche in build:
- `-CleanAllTemp`
- `-CleanImports`
- `-CleanCache`
- `-CleanLogs`
- `-CleanExports`
- `-CleanServiceJobs`
- `-CleanScanData`
- `-CleanAnalysisImports`
- `-CleanAnalysisExports`

## `scripts/clean.ps1`

Pulizia selettiva:
- output build (`bin/obj`, `.vs`, `dist`, `artifacts`),
- `%TEMP%\NtfsAudit`,
- `%LOCALAPPDATA%\NtfsAudit\Cache`,
- `%LOCALAPPDATA%\NtfsAudit\Logs`,
- export `.xlsx` e `.ntaudit`,
- queue job servizio,
- stato runtime servizio,
- workspace import analisi (`%TEMP%\NtfsAudit\imports`),
- workspace export analisi (`%TEMP%\NtfsAudit\exports`).

## Comandi rapidi

```powershell
# build + test
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release

# clean esteso + build
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release -RunClean -CleanAllTemp -CleanScanData -CleanAnalysisImports -CleanAnalysisExports

# pulizia dedicata import/export analisi
powershell -ExecutionPolicy Bypass -File .\scripts\clean.ps1 -CleanAnalysisImports -CleanAnalysisExports -CleanExports
```

## Note operative

- Progetto orientato a Windows (WPF, ACL NTFS native, service control manager).
- In ambienti Linux/macOS parte delle funzionalità non è eseguibile.
- I resolver path includono fallback difensivi quando API Windows non sono disponibili.
