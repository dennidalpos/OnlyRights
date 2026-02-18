# NTFS Audit

Suite Windows per analisi ACL NTFS/SMB composta da:
- **NtfsAudit.App** (WPF operativa),
- **NtfsAudit.Service** (esecuzione job in background),
- **NtfsAudit.Viewer** (apertura archivi `.ntaudit` in sola lettura).

## Architettura generale

### NtfsAudit.App
Responsabile di:
- configurazione root di scansione,
- esecuzione scansione locale o invio job al servizio,
- calcolo ACL NTFS/Share/Effective,
- filtri ACL e filtri albero,
- export Excel `.xlsx`,
- export/import archivio analisi `.ntaudit`.

File principali:
- `src/NtfsAudit.App/ViewModels/MainViewModel.cs`
- `src/NtfsAudit.App/Services/ScanService.cs`
- `src/NtfsAudit.App/Services/AnalysisArchive.cs`
- `src/NtfsAudit.App/Export/ExcelExporter.cs`

### NtfsAudit.Service
Responsabile di:
- polling queue job in `%ProgramData%\NtfsAudit\jobs`,
- esecuzione sequenziale delle root richieste,
- aggiornamento stato runtime in `%ProgramData%\NtfsAudit\service-status.json`.

### NtfsAudit.Viewer
Responsabile di:
- apertura archivio `.ntaudit` senza avviare nuove scansioni,
- render dei risultati tramite stessa pipeline di import usata nell’app principale.

---

## Processo di scansione (runtime)

Pipeline sintetica:
1. validazione opzioni e preparazione path temporanei,
2. enumerazione cartelle (con esclusione path DFSR/cache),
3. lettura ACL directory (e file, se attivato),
4. risoluzione identità e opzionale espansione gruppi,
5. emissione record export JSONL,
6. aggiornamento progress UI e risultati in memoria (`Details`, `TreeMap`).

Ottimizzazioni attive:
- enumerazione cartelle/file con singola `EnumerationOptions` riusata;
- coda export dati con capacità limitata (backpressure) per evitare crescita RAM indefinita;
- struttura albero con deduplica figli per ridurre overhead in scansioni profonde.

---

## Processo Import / Export

## Export Excel (`.xlsx`)
Pipeline:
1. lettura `data.jsonl` e `errors.jsonl`,
2. separazione utenti / gruppi / errori,
3. scrittura workbook OpenXML,
4. split automatico fogli oltre limite Excel.

Output:
- `Users_n`, `Groups_n`, `Errors`.

Caratteristiche:
- scrittura atomica output,
- gestione dataset estesi con split sheet,
- warning UI in caso di suddivisione.

## Export analisi (`.ntaudit`)
Pipeline:
1. validazione `TempDataPath`,
2. risoluzione root e `PathKind` (con fallback su opzioni scansione),
3. ricostruzione tree map da export se non presente in memoria,
4. creazione archivio ZIP temporaneo in `%TEMP%\NtfsAudit\exports`,
5. cleanup workspace export obsoleti,
6. replace atomico del file finale.

Contenuto archivio:
- `data.jsonl`
- `errors.jsonl`
- `tree.json`
- `folderflags.json`
- `meta.json`

Metadati salvati:
- root normalizzato,
- tipo path,
- timestamp UTC scansione,
- versione archivio,
- opzioni scansione.

## Import analisi (`.ntaudit`)
Pipeline:
1. cleanup cartelle import obsolete (`%TEMP%\NtfsAudit\imports`),
2. estrazione archivio in workspace dedicato (`<nome>_<timestamp>_<guid>`),
3. verifica presenza minima (`data.jsonl`, fallback `errors.jsonl` vuoto),
4. load metadati e fallback compatibili versioni precedenti,
5. ricostruzione `Details`, `TreeMap` e flag cartella,
6. normalizzazione timestamp importato in UTC,
7. applicazione opzioni nel ViewModel.

Regole di robustezza:
- normalizzazione path (slash misti inclusi),
- deduplica entry ACL,
- mantenimento flag service/admin account,
- cleanup cartella import solo su import fallito.

---

## Logica checkbox configurazione scansione

## 1) Profondità
- **Scan all depths** (`ScanAllDepths`)
  - `true`: ignora input numerico e usa profondità massima.
  - `false`: usa `MaxDepth` con clamp su range valido.

## 2) Identità
Checkbox padre: **Resolve identities** (`ResolveIdentities`).

Quando `ResolveIdentities = false`:
- `ExpandGroups = false`
- `UsePowerShell = false`
- `ExcludeServiceAccounts = false`
- `ExcludeAdminAccounts = false`

Quando `ResolveIdentities = true`:
- le opzioni figlie tornano disponibili.

## 3) Audit avanzato
Checkbox padre: **Enable advanced audit** (`EnableAdvancedAudit`).

Quando `EnableAdvancedAudit = false`:
- `ComputeEffectiveAccess = false`
- `IncludeSharePermissions = false`
- `IncludeFiles = false`
- `ReadOwnerAndSacl = false`
- `CompareBaseline = false`

Quando `EnableAdvancedAudit = true`:
- le opzioni figlie tornano configurabili.

## 4) Coerenze di stato UI
- Le opzioni importate da archivio vengono riallineate con la stessa logica padre/figlie.
- Durante reset/load massivo viene sospesa la persistenza preferenze per evitare stati intermedi incoerenti.

---

## Logica filtri ACL (tabella risultati)

Filtri principali:
- **Allow / Deny** (almeno uno deve rimanere attivo),
- **Inherited / Explicit** (almeno uno deve rimanere attivo),
- **Inheritance disabled**,
- **Protected**,
- categorie principal:
  - Everyone,
  - Authenticated Users,
  - Service Accounts,
  - Admin Accounts,
  - Other Principals.

Regole:
- se tutte le categorie principal vengono disattivate, viene riattivato automaticamente `Other Principals`;
- filtro testuale (`AclFilter`) case-insensitive su principal, SID, diritti, path, owner, share, rischio, source, `PathKind`, membri gruppo;
- filtri persistiti in `ui-preferences.json`.

---

## Logica filtri albero

Filtri disponibili:
- solo ACL esplicite,
- solo inheritance disabled,
- solo differenze baseline,
- solo deny espliciti,
- solo mismatch baseline,
- solo file,
- solo cartelle.

Regole:
- file/cartelle non possono essere entrambi disattivati;
- preservazione selezione corrente dove possibile dopo reload;
- persistenza immediata dei flag filtro.

---

## Doppio click su utenti / gruppi (dettaglio principal)

Comportamento:
- doppio click gruppo -> lookup membri gruppo,
- doppio click utente -> lookup gruppi utente.

Robustezza:
- eccezioni lookup AD intercettate con messaggio contestuale,
- fallback sicuro su array vuoti in caso di resolver `null`,
- prevenzione errore generico dispatcher durante apertura dettaglio principal.

---

## Script build / clean

## `scripts/build.ps1`
Pipeline standard:
1. `dotnet restore`
2. `dotnet build`
3. `dotnet test`
4. `dotnet publish` (App, Viewer, Service)

Flag principali:
- `-Configuration`
- `-SkipRestore`, `-SkipBuild`, `-SkipTests`, `-SkipPublish`
- `-SkipViewerPublish`, `-SkipServicePublish`, `-SkipPublishClean`
- `-Framework`, `-Runtime`, `-SelfContained`
- `-PublishSingleFile`, `-PublishReadyToRun`
- `-OutputPath`
- `-RunClean`

Pulizia pre-build integrata:
- `-CleanAllTemp`
- `-CleanImports`
- `-CleanCache`
- `-CleanLogs`
- `-CleanExports`
- `-CleanServiceJobs`
- `-CleanScanData`
- `-CleanAnalysisImports`
- `-CleanAnalysisExports`
- `-CleanAnalysisWorkspace` (shortcut import+export workspace analisi)
- `-CleanImportExportData` (shortcut import temporanei + export file + workspace analisi)
- `-CleanOperationalData` (shortcut esteso: import/export + scan data + service jobs + logs + workspace analisi)

## `scripts/clean.ps1`
Pulizia selettiva di:
- output build (`bin/obj`, `.vs`, `dist`, `artifacts`),
- `%TEMP%\NtfsAudit`,
- `%LOCALAPPDATA%\NtfsAudit\Cache`,
- `%LOCALAPPDATA%\NtfsAudit\Logs`,
- file `.xlsx` / `.ntaudit`,
- queue job servizio,
- stato runtime servizio,
- workspace analisi import/export.

Shortcut:
- `-CleanAnalysisWorkspace` => pulisce `%TEMP%\NtfsAudit\imports` + `%TEMP%\NtfsAudit\exports`
- `-CleanImportExportData` => pulisce import temporanei + file export + workspace analisi
- `-CleanOperationalData` => pulizia estesa operativa (import/export, scan data, job servizio, log, workspace analisi)

---

## Comandi rapidi

```powershell
# build completa
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release

# clean esteso + build
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release -RunClean -CleanAllTemp -CleanScanData -CleanAnalysisWorkspace

# pulizia workspace analisi
powershell -ExecutionPolicy Bypass -File .\scripts\clean.ps1 -CleanAnalysisWorkspace

# pulizia completa import/export
powershell -ExecutionPolicy Bypass -File .\scripts\clean.ps1 -CleanImportExportData

# pulizia operativa estesa (log + queue + scan data + import/export)
powershell -ExecutionPolicy Bypass -File .\scripts\clean.ps1 -CleanOperationalData
```

---

## Note operative

- Progetto orientato Windows (WPF, ACL NTFS native, service manager).
- In Linux/macOS alcune funzionalità non sono eseguibili.
- Senza SDK .NET non è possibile eseguire `dotnet restore/build/test/publish`.
