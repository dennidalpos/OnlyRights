# NTFS Audit

Suite Windows per analisi ACL NTFS/SMB composta da:
- **NtfsAudit.App** (WPF operativa),
- **NtfsAudit.Service** (host opzionale per job in background),
- **NtfsAudit.Viewer** (apertura archivi `.ntaudit` in sola lettura).

## Stack

- .NET 6/8 per applicazioni WPF Windows
- .NET 8 per Windows Service e test
- OpenXML per export Excel
- Microsoft.Data.Sqlite per payload/query layer read-only su archivi grandi
- Newtonsoft.Json per serializzazione archivio/stato

## Struttura del repository

- `src/NtfsAudit.App`: applicazione principale (scan, filtri, export/import, UI).
- `src/NtfsAudit.Service`: host Windows opzionale per esecuzione job asincroni/background con la stessa pipeline dell'app.
- `src/NtfsAudit.Viewer`: client in sola lettura per archivi analisi.
- `tests/NtfsAudit.App.Tests`: test unitari su pipeline, path, filtri e robustezza import/export.
- `scripts/build.ps1`: restore/build/test/publish con opzioni cleaning integrate.
- `scripts/clean.ps1`: pulizia artefatti build e residui operativi (cache/temp/job/report).
- `.github/workflows/ci.yml`: pipeline minima Windows per restore/build/test.
- `global.json`: pin dell'SDK .NET 8 usato dal repository.

Output generati riconoscibili dalla root:
- `artifacts/bin/<ProjectName>/...`: output di build.
- `artifacts/obj/<ProjectName>/...`: intermedi MSBuild/restore.
- `artifacts/test-results/...`: risultati `dotnet test`.
- `dist/<Configuration>/...`: publish distribuiti generati dagli script.

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
   - `analysis.sqlite`
   - `meta.json`
5. validazione archivio temporaneo,
6. replace atomico output finale.

Dettagli aggiornati:
- estensione `.ntaudit` applicata automaticamente se omessa,
- workspace export in `%TEMP%\NtfsAudit\exports` con retention automatica,
- payload SQLite incluso per consultazione read-only scalabile di dataset grandi,
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
7. uso del payload SQLite in modalità lazy per archivi grandi oppure fallback ai JSON legacy per archivi piccoli,
8. ricostruzione `Details`, `TreeMap`, flag cartella e baseline,
9. normalizzazione timestamp e opzioni nel ViewModel.

Regole di robustezza:
- normalizzazione path con slash misti,
- deduplica ACL duplicate,
- fallback sicuri per archivi legacy,
- soglia lazy SQLite applicata solo ai dataset grandi, preservando il comportamento storico sugli archivi piccoli,
- cleanup automatico workspace solo su import fallito.

### Viewer read-only

- `NtfsAudit.Viewer` può essere avviato anche con path `.ntaudit` come argomento da riga di comando o share di rete.
- Gli archivi grandi vengono consultati in sola lettura tramite `analysis.sqlite` senza materializzare tutti gli ACE in memoria all’avvio.

---

## Modalità locale vs servizio

- **Locale**: scansione nel processo UI con progress in tempo reale.
- **Servizio Windows**: host opzionale per enqueue job in `%ProgramData%\NtfsAudit\jobs`, esecuzione in background e monitor stato via `service-status.json`.
- Il servizio non aggiunge funzionalità di analisi rispetto all'app: serve soprattutto per scansioni lunghe, non interattive o quando si vuole disaccoppiare l'esecuzione dalla sessione UI.
- **Credenziali scansione**: supporto a credenziali globali applicative e override dedicato per singola root, con priorità `override root -> globali -> utente corrente`.

Nella UI:
- checkbox **Esegui tramite servizio Windows** (default non selezionato),
- badge stato servizio unificato,
- azioni install/disinstalla servizio dalla toolbar,
- sezione **Credenziali scansione** per salvare in locale credenziali protette e override per la root selezionata.

Persistenza credenziali:
- storage locale protetto via DPAPI nel profilo utente,
- payload dei job service protetto per `LocalMachine`,
- nessuna credenziale esportata nei metadati `.ntaudit`.

---

## Pulizia residui

Pulsante **Pulisci cache**:
1. elimina residui operativi runtime (`temp` scan, cache locale, cache servizio, jobs, service-status),
2. preserva i workspace `%TEMP%\\NtfsAudit\\imports` e `%TEMP%\\NtfsAudit\\exports` gestiti con retention dedicata,
3. ricrea la cartella temp applicativa,
4. apre la cartella temp per verifica rapida.

Script CLI equivalenti:
- `scripts/clean.ps1 -CleanOperationalData` preserva `%TEMP%\\NtfsAudit\\imports` e `%TEMP%\\NtfsAudit\\exports`
- `scripts/clean.ps1 -CleanImportExportData` pulisce report/export e workspace analisi
- `scripts/clean.ps1 -CleanAnalysisWorkspace` cancella solo i workspace analisi `imports/exports`

---

## Script build / clean

## `scripts/build.ps1`

Flusso standard:
1. restore (`dotnet restore`),
2. build (`dotnet build`),
3. test (`dotnet test`),
4. publish App/Viewer/Service (`dotnet publish`, framework-dependent di default).

Output:
- build/intermedi centralizzati in `artifacts/bin` e `artifacts/obj`,
- risultati test in `artifacts/test-results`,
- publish in `dist/<Configuration>/[Runtime]/[Framework]`.

Opzioni principali:
- `-Configuration`, `-Framework`, `-Runtime`, `-OutputPath`
- `-SkipRestore`, `-SkipBuild`, `-SkipTests`, `-SkipPublish`
- `-SkipViewerPublish`, `-SkipServicePublish`, `-SkipPublishClean`
- `-SelfContained`, `-PublishSingleFile`, `-PublishReadyToRun`
- `-RunClean` + opzioni cleaning (`-CleanOperationalData`, `-CleanImportExportData`, ...)

Con `-RunClean`, i cleanup generici o operativi preservano i workspace `%TEMP%\\NtfsAudit\\imports` e `%TEMP%\\NtfsAudit\\exports`; per rimuoverli servono i flag espliciti `-CleanAnalysisWorkspace`, `-CleanAnalysisImports`, `-CleanAnalysisExports` o `-CleanImportExportData`.

Il comando stampa un riepilogo finale build (configurazione, dist, test/publish).

## `scripts/clean.ps1`

Pulizia modulare:
- artefatti compilazione centralizzati (`artifacts/bin`, `artifacts/obj`, `artifacts/test-results`) e residui legacy `src/**/bin|obj`,
- cartella `.vs` e publish `dist`,
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

I preset generici `-CleanAllTemp` e `-CleanOperationalData` preservano `%TEMP%\\NtfsAudit\\imports` e `%TEMP%\\NtfsAudit\\exports`; la cancellazione dei workspace analisi resta un'azione esplicita.

---

## Avvio rapido

### Build completa
```powershell
powershell -File .\scripts\build.ps1 -Configuration Release
```

### Build senza test
```powershell
powershell -File .\scripts\build.ps1 -SkipTests
```

### Clean operativo completo
```powershell
powershell -File .\scripts\clean.ps1 -CleanOperationalData
```

### Test con build già eseguita
```powershell
dotnet test .\NtfsAudit.sln -c Release --no-build --nologo
```

### Publish self-contained
```powershell
powershell -File .\scripts\build.ps1 -Configuration Release -Runtime win-x64 -SelfContained -PublishSingleFile
```

## Setup

Prerequisiti:
- Windows con .NET SDK 8 installato (pin in `global.json`, attualmente `8.0.100` con roll-forward `latestFeature`)
- workload desktop .NET/WPF disponibile

Ripristino dipendenze:
```powershell
dotnet restore .\NtfsAudit.sln
```

## Run

Esecuzione applicazione principale:
```powershell
dotnet run --project .\src\NtfsAudit.App\NtfsAudit.App.csproj -f net8.0-windows
```

Esecuzione test:
```powershell
dotnet test .\NtfsAudit.sln -c Release --no-build --nologo
```

## Documentation

- `PROJECT_SPEC.md`
- `PROJECT_STATUS.json`
- `.github/workflows/ci.yml`

## Copyright

Copyright (c) 2026 Danny Perondi. All rights reserved.

OnlyRights is proprietary, confidential, and closed-source. You may view this
repository only for reference, evaluation, or internal review.

You may not copy, modify, reuse, distribute, publish, sublicense, sell, or
otherwise use any part of this project, including source code, scripts,
documentation, or assets, without prior written permission from Danny
Perondi.

This project is provided "AS IS", without warranty or liability.
