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
- `docs/setup-iniziale.md`: guida rapida per setup iniziale e configurazione utente.
- `scripts/setup.ps1`: wrapper rapido per il primo setup locale (`bootstrap` + `doctor` + `build`).
- `scripts/bootstrap.ps1`, `doctor.ps1`, `compile.ps1`, `build.ps1`, `test.ps1`, `pack.ps1`, `publish.ps1`: entrypoint canonici del repository.
- `scripts/clean.ps1`: pulizia artefatti build e residui operativi (cache/temp/job/report), con preset `-ResetToInitialState` per tornare a uno stato sorgente-only.
- `scripts/reset-repo-state.ps1`: wrapper dedicato per riportare il repository allo stato iniziale pulito senza toccare le modifiche git tracciate.
- `scripts/windows/*.ps1`: gestione servizio Windows via `sc.exe` con fallback opzionale a `tools/nssm`.
- `scripts/packaging/*.ps1`: build/test install/test upgrade/test uninstall MSI tramite `tools/wix314-binaries` e `msiexec`.
- `.github/workflows/ci.yml`: pipeline Windows che usa gli stessi entrypoint locali.
- `global.json`: pin dell'SDK .NET 8 usato dal repository.

Contratto framework supportato:
- `NtfsAudit.App` e `NtfsAudit.Viewer` restano supportati su `net6.0-windows` e `net8.0-windows`.
- `NtfsAudit.Service`, test automatici, smoke test servizio e packaging MSI restano allineati a `net8.0-windows`.
- La CI valida `net8.0-windows` end-to-end e aggiunge copertura `build`/`pack`/`publish` per `net6.0-windows` su App e Viewer.

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

Diagnostica runtime:
- i fallimenti nella lettura dei permessi share SMB vengono registrati in `errors.jsonl` con messaggi distinti per accesso negato, host/share non raggiungibile o provider non compatibile, invece di essere ignorati silenziosamente.
- i path con permessi insufficienti, provider/filesystem non supportati o verifiche preliminari non deterministiche non bloccano piu l'avvio della scansione: il problema viene degradato automaticamente e registrato in `errors.jsonl` con un messaggio normalizzato.
- i path classificati come `NFS` vengono rilevati ed etichettati in UI, ma il prodotto non dichiara parità completa con i permessi POSIX/NFS: l'analisi resta limitata ai metadati e provider che Windows espone realmente.

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
- **Credenziali scansione**: supporto a credenziali globali applicative e override dedicato per singola root, con priorità `override root -> globali -> utente corrente`; sui path locali o non UNC l'esecuzione resta comunque sull'utente corrente.

Nella UI:
- checkbox **Esegui tramite servizio Windows** (default non selezionato),
- badge stato servizio unificato con testo runtime/progress,
- azioni install/disinstalla servizio dalla toolbar,
- sezione **Credenziali scansione** per salvare in locale credenziali protette e override per la root selezionata,
- selettore target DFS sia per il path in input sia per la root DFS già presente in elenco.

Persistenza credenziali:
- storage locale protetto via DPAPI nel profilo utente,
- payload dei job service protetto per `LocalMachine`,
- nessuna credenziale esportata nei metadati `.ntaudit`.

Persistenza configurazione scansione:
- le preferenze UI salvano root corrente, target DFS selezionato, cartelle in elenco, output `.ntaudit`, modalità servizio e principali flag di scansione.
- le credenziali restano persistite separatamente nello storage protetto locale e non vengono duplicate nel file `ui-preferences.json`.

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
- `scripts/clean.ps1 -ResetToInitialState` o `scripts/reset-repo-state.ps1` rimuovono artefatti, cache, temp, export e workspace analisi per tornare a uno stato iniziale pulito

---

## Script canonici

- `scripts/setup.ps1`: percorso consigliato per il primo avvio locale; richiama `bootstrap`, `doctor` e `build` con un solo comando.
- `scripts/bootstrap.ps1`: restore non interattivo della solution.
- `scripts/doctor.ps1`: verifica i prerequisiti minimi per setup/build locale e segnala separatamente le capability opzionali per servizio Windows e packaging MSI. Con `-RequireOptionalTools` rende bloccanti anche i controlli opzionali.
- `scripts/compile.ps1`: compila la solution senza packaging.
- `scripts/build.ps1`: wrapper canonico su `compile`; in questo repository `compile` e `build` coincidono tecnicamente.
- `scripts/test.ps1`: esegue i test automatici e salva i risultati in `artifacts/test-results`.
- `scripts/pack.ps1`: produce publish App/Viewer/Service sotto `artifacts/packages`.
- `scripts/publish.ps1`: copia artefatti già prodotti da `pack` sotto `artifacts/publish`.

## Script Windows

- `scripts/windows/service-install.ps1`: installa `NtfsAuditWorker` dai path canonici `src/.../bin`, `artifacts/build`, `artifacts/packages`, `artifacts/publish` o da un path esplicito.
- `scripts/windows/service-start.ps1` e `service-stop.ps1`: avvio/stop non interattivi del servizio.
- `scripts/windows/service-uninstall.ps1`: rimuove il servizio e pulisce i residui operativi.
- `scripts/windows/services-cleanup.ps1`: cleanup difensivo post-test; `nssm-cleanup.ps1` forza il ramo fallback NSSM.

## Script MSI

- `scripts/packaging/msi-build.ps1`: genera l'MSI sotto `artifacts/packages/.../installer`.
- `scripts/packaging/msi-install-test.ps1`: installazione silenziosa verificabile con log in `artifacts/logs`.
- `scripts/packaging/msi-upgrade-test.ps1`: prova upgrade compatibile tra due versioni MSI.
- `scripts/packaging/msi-uninstall-test.ps1`: uninstall silenzioso e cleanup finale del servizio.
- `.github/workflows/ci.yml`: oltre a build/test/pack/publish e build MSI, esegue smoke test non interattivi di install/uninstall servizio e install/uninstall/upgrade MSI.

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
- `-ResetToInitialState`

I preset generici `-CleanAllTemp` e `-CleanOperationalData` preservano `%TEMP%\\NtfsAudit\\imports` e `%TEMP%\\NtfsAudit\\exports`; la cancellazione dei workspace analisi resta un'azione esplicita.
`-ResetToInitialState` esegue invece una pulizia completa sorgente-only: artefatti, cache, temp, export, job servizio e workspace analisi, senza alterare file versionati o modifiche git locali.

---

## Avvio rapido

### Setup iniziale consigliato
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1
```

Guida utente dettagliata:
- `docs/setup-iniziale.md`

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

### Reset stato iniziale pulito
```powershell
powershell -File .\scripts\reset-repo-state.ps1
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

Procedura più semplice per il primo avvio:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1
```

Per la configurazione iniziale guidata dell'app:
- `docs/setup-iniziale.md`

## Run

Esecuzione applicazione principale:
```powershell
dotnet run --project .\src\NtfsAudit.App\NtfsAudit.App.csproj -f net8.0-windows
```

Esecuzione viewer read-only:
```powershell
dotnet run --project .\src\NtfsAudit.Viewer\NtfsAudit.Viewer.csproj -f net8.0-windows
```

Esecuzione test:
```powershell
powershell -File .\scripts\test.ps1 -Configuration Release
```

## Documentation

- `docs/setup-iniziale.md`
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
