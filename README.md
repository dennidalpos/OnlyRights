# OnlyRights

Suite Windows `NtfsAudit` per analisi ACL NTFS/SMB composta da:
- **NtfsAudit.App** (WPF operativa),
- **NtfsAudit.Service** (host opzionale per job in background),
- **NtfsAudit.Viewer** (apertura archivi `.ntaudit` in sola lettura).

Nel repository il nome progetto legale è **OnlyRights**; la solution e i componenti applicativi mantengono il naming tecnico **NtfsAudit**.

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
- `scripts/bootstrap.ps1`, `doctor.ps1`, `compile.ps1`, `build.ps1`, `test.ps1`, `pack.ps1`, `publish.ps1`: entrypoint canonici del repository.
- `scripts/clean.ps1`: pulizia artefatti build e residui operativi (cache/temp/job/report).
- `scripts/windows/*.ps1`: gestione servizio Windows via `sc.exe` con fallback opzionale a `tools/nssm`.
- `scripts/packaging/*.ps1`: build/test install/test upgrade/test uninstall MSI tramite `tools/wix314-binaries` e `msiexec`.
- `.github/workflows/ci.yml`: pipeline Windows che usa gli stessi entrypoint locali.
- `global.json`: pin dell'SDK .NET 8 usato dal repository.

Output generati riconoscibili dalla root:
- `artifacts/build/<ProjectName>/...`: output di build.
- `artifacts/build/obj/<ProjectName>/...`: intermedi MSBuild/restore.
- `artifacts/test-results/...`: risultati `dotnet test`.
- `artifacts/packages/<Configuration>/...`: artefatti distribuibili prodotti da `pack`.
- `artifacts/packages/<Configuration>/<Framework>/installer/...`: MSI e staging WiX generati dagli script packaging.
- `artifacts/publish/<Configuration>/...`: staging locale prodotto da `publish`.

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

## Script canonici

- `scripts/bootstrap.ps1`: restore non interattivo della solution.
- `scripts/doctor.ps1`: verifica SDK .NET, `global.json` e toolchain locali Windows (`tools/wix314-binaries`, `tools/nssm`).
- `scripts/compile.ps1`: compila la solution senza packaging.
- `scripts/build.ps1`: wrapper canonico su `compile`; in questo repository `compile` e `build` coincidono tecnicamente.
- `scripts/test.ps1`: esegue i test automatici e salva i risultati in `artifacts/test-results`.
- `scripts/pack.ps1`: produce publish App/Viewer/Service sotto `artifacts/packages`.
- `scripts/publish.ps1`: copia artefatti già prodotti da `pack` sotto `artifacts/publish`.

## Script Windows

- `scripts/windows/service-install.ps1`: installa `NtfsAuditWorker` dai path canonici `artifacts/build|packages|publish` o da un path esplicito.
- `scripts/windows/service-start.ps1` e `service-stop.ps1`: avvio/stop non interattivi del servizio.
- `scripts/windows/service-uninstall.ps1`: rimuove il servizio e pulisce i residui operativi.
- `scripts/windows/services-cleanup.ps1`: cleanup difensivo post-test; `nssm-cleanup.ps1` forza il ramo fallback NSSM.

## Script MSI

- `scripts/packaging/msi-build.ps1`: genera l'MSI sotto `artifacts/packages/.../installer`.
- `scripts/packaging/msi-install-test.ps1`: installazione silenziosa verificabile con log in `artifacts/logs`.
- `scripts/packaging/msi-upgrade-test.ps1`: prova upgrade compatibile tra due versioni MSI.
- `scripts/packaging/msi-uninstall-test.ps1`: uninstall silenzioso e cleanup finale del servizio.

## `scripts/clean.ps1`

Pulizia modulare:
- artefatti compilazione centralizzati (`artifacts/build`, `artifacts/test-results`, `artifacts/packages`, `artifacts/publish`) e residui legacy `src/**/bin|obj`,
- cartella `.vs` e vecchi publish legacy `dist`,
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

### Bootstrap
```powershell
powershell -File .\scripts\bootstrap.ps1
```

### Doctor
```powershell
powershell -File .\scripts\doctor.ps1
```

### Build
```powershell
powershell -File .\scripts\build.ps1 -Configuration Release
```

### Test
```powershell
powershell -File .\scripts\test.ps1 -Configuration Release
```

### Pack
```powershell
powershell -File .\scripts\pack.ps1 -Configuration Release
```

### Publish locale
```powershell
powershell -File .\scripts\publish.ps1 -Configuration Release
```

### Installazione servizio Windows
```powershell
powershell -File .\scripts\windows\service-install.ps1 -Configuration Release
```

### Build MSI
```powershell
powershell -File .\scripts\packaging\msi-build.ps1 -Configuration Release -Version 1.0.0
```

### Test upgrade MSI
```powershell
powershell -File .\scripts\packaging\msi-upgrade-test.ps1 -Configuration Release -BaseVersion 1.0.0 -UpgradeVersion 1.0.1
```

### Clean operativo completo
```powershell
powershell -File .\scripts\clean.ps1 -CleanOperationalData
```

### Test con build già eseguita
```powershell
powershell -File .\scripts\test.ps1 -Configuration Release -SkipRestore -SkipBuild
```

### Pack self-contained
```powershell
powershell -File .\scripts\pack.ps1 -Configuration Release -Runtime win-x64 -SelfContained -PublishSingleFile
```

## Setup

Prerequisiti:
- Windows con .NET SDK 8 installato (pin in `global.json`, attualmente `8.0.100` con roll-forward `latestFeature`)
- workload desktop .NET/WPF disponibile

Ripristino dipendenze:
```powershell
powershell -File .\scripts\bootstrap.ps1
```

## Run

Esecuzione applicazione principale:
```powershell
dotnet run --project .\src\NtfsAudit.App\NtfsAudit.App.csproj -f net8.0-windows
```

Esecuzione test:
```powershell
powershell -File .\scripts\test.ps1 -Configuration Release
```

## Documentation

- `PROJECT_SPEC.md`
- `PROJECT_STATUS.json`
- `.github/workflows/ci.yml`

## Copyright

Project name: `OnlyRights`

Application suite: `NtfsAudit`

Copyright (c) 2026 Danny Perondi. All rights reserved.

This repository and its source code are proprietary. Unauthorized copying,
modification, distribution, sublicensing, or commercial use is prohibited
without prior written permission.

This software is provided "AS IS", without warranty or liability.
