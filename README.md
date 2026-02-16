# NTFS Audit

Suite Windows per analisi ACL NTFS/SMB con UI interattiva, coda job via servizio Windows e viewer in sola lettura.

## Componenti

- **NtfsAudit.App (WPF)**
  - configurazione multi-root,
  - scansione locale o invio job al servizio,
  - tray icon con stato runtime,
  - export `.xlsx` e archivio `.ntaudit`,
  - pulizia dati scansione, cache e residui operativi.
- **NtfsAudit.Service (Windows Service)**
  - polling job queue in `%ProgramData%\NtfsAudit\jobs`,
  - esecuzione sequenziale delle root,
  - pubblicazione stato runtime in `%ProgramData%\NtfsAudit\service-status.json`.
- **NtfsAudit.Viewer**
  - apertura archivi `.ntaudit` in modalità sola lettura.

---

## Architettura repository

- `src/NtfsAudit.App`
  - `ViewModels/MainViewModel.cs`: orchestrazione scansione, gestione job servizio, comandi UI/tray, cleanup.
  - `MainWindow.xaml` + `MainWindow.xaml.cs`: shell UI, tray icon/menu, eventi finestra.
  - `Services/*`: scanning ACL, resolver identità, import/export archivi.
  - `Cache/*`: gestione cache locale SID e membership.
- `src/NtfsAudit.Service`
  - `ScanWorker.cs`: loop servizio, lettura job e aggiornamento stato runtime.
- `src/NtfsAudit.Viewer`
  - bootstrap viewer read-only.
- `tests/NtfsAudit.App.Tests`
  - unit test su servizi core.
- `scripts/build.ps1`
  - restore/build/test/publish + clean opzionale integrato.
- `scripts/clean.ps1`
  - pulizia selettiva build output, cache, temp, export, job, stato servizio.

---

## Logica servizio e tray icon

### Stato servizio

- L’app legge periodicamente `%ProgramData%\NtfsAudit\service-status.json`.
- In UI mostra badge runtime (`ServiceRuntimeStatusText`) con:
  - root corrente e progress `root i/n`,
  - **code scansioni** (`PendingJobs`) sempre esplicitate,
  - ultimo messaggio operativo del servizio.

### Tray icon

- La tray icon è visibile quando:
  - finestra minimizzata, oppure
  - servizio in esecuzione.
- Context menu tray:
  - **Apri**,
  - **Ferma scansione / job**,
  - **Pulisci cache/residui**,
  - **Esci**.
- Chiusura finestra:
  - se il servizio è in esecuzione, la finestra non termina il processo ma resta in tray.

---

## Esecuzione a istanza singola

`NtfsAudit.App` usa un mutex nominato globale (`Global\NtfsAudit.App.SingleInstance`) per impedire avvii multipli della UI.

Comportamento:
- se esiste già un’istanza attiva, il nuovo avvio mostra un messaggio informativo e termina.

---

## Flussi scansione

### Modalità locale (app)

1. Seleziona una o più root.
2. Avvia scansione.
3. Analizza risultati su tree + griglie ACL + errori.
4. Esporta in `.xlsx` o `.ntaudit`.

### Modalità servizio

1. L’app serializza un job JSON in `%ProgramData%\NtfsAudit\jobs`.
2. Il servizio processa i file `job_*.json` in ordine lessicografico.
3. Ogni root viene eseguita indipendentemente.
4. Se `OutputDirectory` è valorizzato, il servizio crea un archivio `.ntaudit` per root.

### Stop scansione per aggiornare il job

- Pulsante **Stop + pulizia** e voce tray **Ferma scansione / job**:
  - annullano scansione locale in corso,
  - fermano il servizio (se running),
  - rimuovono i job pendenti (`job_*.json`),
  - eseguono automaticamente la pulizia cache/residui (temp, cache locale, stato servizio),
  - consentono di aggiornare subito la lista cartelle e rilanciare.

---

## Pulizia dati scansione, cache e residui

### Da interfaccia

- **Cancella scansione corrente**
  - elimina file temporanei `scan_*.jsonl` / `errors_*.jsonl` della sessione corrente,
  - resetta i risultati caricati in UI.
- **Pulisci cache/residui**
  - pulisce `%TEMP%\NtfsAudit`,
  - pulisce `%LOCALAPPDATA%\NtfsAudit\Cache`,
  - rimuove `%ProgramData%\NtfsAudit\jobs` e `service-status.json`.

### Da script

- `scripts/clean.ps1` e `scripts/build.ps1 -RunClean` supportano anche `-CleanScanData` per pulizia completa dati scansione runtime.

---

## Script build e clean

## `scripts/build.ps1`

Pipeline standard:
- restore
- build
- test
- publish (App / Viewer / Service)

Flag principali:
- `-Configuration Release|Debug`
- `-SkipRestore`, `-SkipBuild`, `-SkipTests`, `-SkipPublish`
- `-Framework`, `-Runtime`, `-SelfContained`
- `-PublishSingleFile`, `-PublishReadyToRun`
- `-RunClean` (forward a `clean.ps1`)

Pulizie forwardabili:
- `-CleanAllTemp`
- `-CleanImports`
- `-CleanCache`
- `-CleanLogs`
- `-CleanExports`
- `-CleanServiceJobs`
- `-CleanScanData`

## `scripts/clean.ps1`

Pulizia selettiva:
- output build (`bin/obj`, `.vs`, `dist`, `artifacts`),
- `%TEMP%\NtfsAudit`,
- `%LOCALAPPDATA%\NtfsAudit\Cache`,
- `%LOCALAPPDATA%\NtfsAudit\Logs`,
- export `.xlsx` e `.ntaudit`,
- queue job servizio,
- stato runtime servizio,
- dati scansione runtime (`-CleanScanData`).

---

## Comandi rapidi

```powershell
# build + test
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release

# clean esteso + build
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release -RunClean -CleanAllTemp -CleanScanData

# solo pulizia dati runtime scansione
powershell -ExecutionPolicy Bypass -File .\scripts\clean.ps1 -CleanScanData -CleanServiceJobs -CleanCache
```

---

## Note operative

- Progetto orientato a Windows (WPF, ACL NTFS native, service control manager).
- In ambienti Linux/macOS parte delle funzionalità non è eseguibile.
- I resolver path includono fallback difensivi quando API Windows non sono disponibili.
