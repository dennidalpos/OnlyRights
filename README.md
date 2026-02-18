# NTFS Audit

Suite Windows per analisi ACL NTFS/SMB composta da:
- **NtfsAudit.App** (WPF operativa),
- **NtfsAudit.Service** (esecuzione job in background),
- **NtfsAudit.Viewer** (apertura archivi `.ntaudit` in sola lettura).

## Struttura del repository

- `src/NtfsAudit.App`: applicazione principale (scan, filtri, export/import, UI).
- `src/NtfsAudit.Service`: servizio Windows per esecuzione job asincroni.
- `src/NtfsAudit.Viewer`: client in sola lettura per archivi analisi.
- `tests/NtfsAudit.App.Tests`: test unitari su pipeline, path, filtri e robustezza import/export.
- `scripts/build.ps1`: restore/build/test/publish con opzioni cleaning integrate.
- `scripts/clean.ps1`: pulizia artefatti build e residui operativi (cache/temp/job/report).

---

## Processo di scansione

Pipeline runtime (App):
1. validazione input (root, profondità, modalità locale/servizio, directory output),
2. risoluzione tipo path (Local/UNC/DFS/NFS),
3. enumerazione cartelle/file con esclusione path DFSR/cache,
4. lettura ACL NTFS e (se abilitato) ACL Share,
5. risoluzione identità (SID -> principal) e opzionale espansione gruppi,
6. calcolo permessi effettivi e metriche rischio,
7. scrittura progressiva su `data.jsonl` + `errors.jsonl`,
8. aggiornamento stato UI (progress, tree, filtri, indicatori).

Output in memoria:
- `Details`: mappa cartella -> dettaglio ACL,
- `TreeMap`: relazione padre/figli per navigazione tree,
- summary statistici e indicatori rischio.

Output persistente:
- report automatico `.ntaudit` nella cartella **Output report .ntaudit** (se impostata).

---

## Processo Export

### Export Excel (`.xlsx`)

Pipeline:
1. lettura `data.jsonl` e `errors.jsonl`,
2. parsing record ACL validi (skip metadati e record corrotti),
3. separazione utenti/gruppi,
4. creazione workbook OpenXML con fogli `Users_n`, `Groups_n`, `Errors`,
5. split automatico sheet oltre limite Excel,
6. scrittura atomica (`tmp` + move finale).

Dettagli robustezza:
- gestione righe invalide senza bloccare l’export,
- limiti colonna/row dimensionati per dataset grandi,
- creazione directory output automatica.

### Export analisi (`.ntaudit`)

Pipeline:
1. validazione file sorgente (`data.jsonl` obbligatorio),
2. risoluzione root e `PathKind` coerente con scan options,
3. ricostruzione `TreeMap` da export quando assente in memoria,
4. costruzione archivio ZIP con entry obbligatorie:
   - `data.jsonl`
   - `errors.jsonl` (vuoto se mancante)
   - `tree.json`
   - `folderflags.json`
   - `meta.json`
5. validazione archivio temporaneo,
6. replace atomico output finale.

Dettagli aggiornati:
- estensione `.ntaudit` applicata automaticamente se omessa,
- workspace export in `%TEMP%\NtfsAudit\exports` con retention automatica,
- metadati con conteggi record/errori per verifica integrità in import.

---

## Processo Import

### Import analisi (`.ntaudit`)

Pipeline:
1. risoluzione path archivio (supporto input senza estensione quando il file esiste con `.ntaudit`),
2. cleanup workspace import obsoleti in `%TEMP%\NtfsAudit\imports`,
3. estrazione in workspace isolato `<nome>_<timestamp>_<guid>`,
4. verifica entry minime (`data.jsonl`, `errors.jsonl` fallback vuoto),
5. lettura metadati e validazione compatibilità versione,
6. verifica consistenza conteggi record/errori (versioni recenti),
7. ricostruzione `Details`, `TreeMap`, flag cartella e baseline,
8. normalizzazione timestamp e opzioni nel ViewModel.

Regole di robustezza:
- normalizzazione path con slash misti,
- deduplica ACL duplicate,
- fallback sicuri per archivi legacy,
- cleanup automatico workspace solo su import fallito.

---

## Modalità locale vs servizio

- **Locale**: scansione nel processo UI con progress in tempo reale.
- **Servizio Windows**: enqueue job in `%ProgramData%\NtfsAudit\jobs`, esecuzione in background, monitor stato via `service-status.json`.

Nella UI:
- checkbox **Esegui tramite servizio Windows** (default non selezionato),
- badge stato servizio unificato,
- azioni install/disinstalla servizio dalla toolbar.

---

## Pulizia residui

Pulsante **Pulisci cache**:
1. elimina residui operativi (`temp`, cache locale, cache servizio, jobs, service-status),
2. ricrea la cartella temp applicativa e la cache servizio,
3. apre entrambe le cartelle (temp + cache servizio) per verifica rapida.

Script CLI equivalenti:
- `scripts/clean.ps1 -CleanOperationalData`
- `scripts/clean.ps1 -CleanImportExportData`
- `scripts/clean.ps1 -CleanAnalysisWorkspace`

---

## Script build / clean

## `scripts/build.ps1`

Flusso standard:
1. restore (`dotnet restore`),
2. build (`dotnet build`),
3. test (`dotnet test`),
4. publish App/Viewer/Service (`dotnet publish`).

Opzioni principali:
- `-Configuration`, `-Framework`, `-Runtime`, `-OutputPath`
- `-SkipRestore`, `-SkipBuild`, `-SkipTests`, `-SkipPublish`
- `-SkipViewerPublish`, `-SkipServicePublish`, `-SkipPublishClean`
- `-SelfContained`, `-PublishSingleFile`, `-PublishReadyToRun`
- `-RunClean` + opzioni cleaning (`-CleanOperationalData`, `-CleanImportExportData`, ...)

Il comando stampa un riepilogo finale build (configurazione, dist, test/publish).

## `scripts/clean.ps1`

Pulizia modulare:
- artefatti compilazione (`bin/obj/.vs/dist/artifacts`),
- temp applicativo `%TEMP%\NtfsAudit`,
- cache `%LOCALAPPDATA%\NtfsAudit\Cache`,
- log app/temp,
- export `.xlsx`/`.ntaudit`,
- job servizio `%ProgramData%\NtfsAudit\jobs`,
- workspace analisi `imports/exports`.

Preset utili:
- `-CleanAllTemp`
- `-CleanOperationalData`
- `-CleanImportExportData`
- `-CleanAnalysisWorkspace`

---

## Avvio rapido

### Build completa
```powershell
pwsh ./scripts/build.ps1 -Configuration Release
```

### Build senza test
```powershell
pwsh ./scripts/build.ps1 -SkipTests
```

### Clean operativo completo
```powershell
pwsh ./scripts/clean.ps1 -CleanOperationalData
```

### Publish self-contained
```powershell
pwsh ./scripts/build.ps1 -Configuration Release -Runtime win-x64 -SelfContained -PublishSingleFile
```
