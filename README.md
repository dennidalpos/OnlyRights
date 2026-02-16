# NTFS Audit

Suite Windows per analisi ACL NTFS/SMB con:
- applicazione WPF operativa,
- servizio Windows per job in background,
- viewer in sola lettura per archivi `.ntaudit`.

## Componenti

### NtfsAudit.App (WPF)
Funzionalità principali:
- configurazione multi-root;
- scansione diretta oppure invio job al servizio;
- audit ACL NTFS/Share/Effective;
- filtri ACL avanzati e filtri albero;
- export Excel `.xlsx`;
- export/import archivio analisi `.ntaudit`.

File chiave:
- `src/NtfsAudit.App/ViewModels/MainViewModel.cs`
- `src/NtfsAudit.App/Services/ScanService.cs`
- `src/NtfsAudit.App/Services/AnalysisArchive.cs`
- `src/NtfsAudit.App/Export/ExcelExporter.cs`

### NtfsAudit.Service (Windows Service)
- polling cartella job `%ProgramData%\NtfsAudit\jobs`;
- esecuzione sequenziale delle root richieste;
- pubblicazione stato runtime in `%ProgramData%\NtfsAudit\service-status.json`.

### NtfsAudit.Viewer
- apertura archivio `.ntaudit` in modalità sola lettura;
- riuso della stessa pipeline import dell’app principale.

## Processo Import / Export

## Export Excel (`.xlsx`)
Pipeline:
1. lettura `data.jsonl` e `errors.jsonl`;
2. separazione record utenti/gruppi/errori;
3. creazione workbook OpenXML;
4. split automatico fogli quando si supera il limite Excel per sheet.

Output:
- fogli `Users_n`, `Groups_n`, `Errors`;
- scrittura atomica del file finale;
- warning UI quando il dataset è stato splittato.

## Export analisi (`.ntaudit`)
Pipeline aggiornata:
1. validazione file dati scansione (`TempDataPath`);
2. risoluzione root/path kind e fallback su opzioni scansione;
3. build tree map (se non già disponibile in memoria);
4. creazione archivio zip in workspace temporaneo `%TEMP%\NtfsAudit\exports`;
5. replace atomico sul file output finale.

Contenuto archivio:
- `data.jsonl`
- `errors.jsonl`
- `tree.json`
- `folderflags.json`
- `meta.json`

Metadati salvati:
- root path normalizzato,
- path kind,
- timestamp scansione UTC,
- versione archivio,
- opzioni scansione.

## Import analisi (`.ntaudit`)
Pipeline aggiornata:
1. estrazione in workspace `%TEMP%\NtfsAudit\imports\<nome_archivio>_<timestamp>_<guid>`;
2. verifica presenza dati minimi (`data.jsonl`, fallback su `errors.jsonl` vuoto);
3. caricamento metadati con fallback compatibile;
4. ricostruzione dettagli ACL (`Details`), albero (`TreeMap`) e flag cartella;
5. normalizzazione timestamp importato (UTC) con fallback su `LastWriteTimeUtc` archivio;
6. applicazione opzioni importate al ViewModel.

Regole di robustezza:
- path normalizzati (anche con slash misti);
- deduplica entry ACL;
- conservazione flag service/admin account;
- cleanup automatico cartella import solo in caso di import fallito.

## Logica checkbox di configurazione scansione

### Profondità
- `ScanAllDepths = true` -> `MaxDepth = int.MaxValue` in esecuzione.
- `ScanAllDepths = false` -> input profondità abilitato e clamp su range valido.

### Identità
- `ResolveIdentities` governa:
  - `ExpandGroups`
  - `UsePowerShell`
  - `ExcludeServiceAccounts`
  - `ExcludeAdminAccounts`
- Se disattivata, le opzioni dipendenti vengono forzate a `false` per coerenza.

### Audit avanzato
- `EnableAdvancedAudit` governa:
  - `ComputeEffectiveAccess`
  - `IncludeSharePermissions`
  - `IncludeFiles`
  - `ReadOwnerAndSacl`
  - `CompareBaseline`
- Se disattivata, le opzioni figlie vengono forzate a `false`.

## Logica filtri ACL

Filtri booleani:
- Allow / Deny (almeno uno sempre attivo);
- Inherited / Explicit (almeno uno sempre attivo);
- Protected / Disabled;
- categorie principal:
  - Everyone,
  - Authenticated Users,
  - Service Accounts,
  - Admin Accounts,
  - Other Principals.

Regole di coerenza:
- se tutte le categorie principal sono disattivate, viene riattivata automaticamente `Other Principals`;
- classificazione principal via SID noto + fallback nome + fallback flag entry.

Filtro testuale (`AclFilter`): ricerca case-insensitive su principal, SID, rights, path, owner, share, risk/audit/source/path kind e membri gruppo.

Persistenza preferenze:
- i filtri ACL e tree filter vengono salvati in cache UI (`ui-preferences.json`);
- durante load/reset massivo della UI la persistenza è sospesa per evitare scritture ripetute e stati intermedi incoerenti.

## Logica filtri albero

Filtri disponibili:
- solo ACL esplicite,
- solo inheritance disabled,
- solo differenze,
- solo deny espliciti,
- solo mismatch baseline,
- solo file,
- solo cartelle.

Regole:
- file e cartelle non possono essere entrambi disattivati;
- viene preservata la selezione corrente quando possibile dopo il reload filtrato;
- i filtri sono persistiti immediatamente nelle preferenze UI.

## Esclusione cartelle DFSR/cache

La scansione salta cartelle tecniche DFSR (es. `System Volume Information\DFSR`, `DfsrPrivate`, `ConflictAndDeleted`, `Staging`, `PreExisting`) con parsing robusto anche su path con separatori misti (`\` e `/`).

## Script build / clean

## `scripts/build.ps1`
Pipeline:
1. restore
2. build
3. test
4. publish (App / Viewer / Service)

Flag principali:
- `-Configuration`
- `-SkipRestore`, `-SkipBuild`, `-SkipTests`, `-SkipPublish`
- `-Framework`, `-Runtime`, `-SelfContained`
- `-PublishSingleFile`, `-PublishReadyToRun`
- `-RunClean`

Pulizia integrata supportata:
- `-CleanAllTemp`
- `-CleanImports`
- `-CleanCache`
- `-CleanLogs`
- `-CleanExports`
- `-CleanServiceJobs`
- `-CleanScanData`
- `-CleanAnalysisImports`
- `-CleanAnalysisExports`
- `-CleanAnalysisWorkspace` (shortcut: abilita sia import che export workspace analisi)

## `scripts/clean.ps1`
Pulizia selettiva di:
- output build (`bin/obj`, `.vs`, `dist`, `artifacts`),
- `%TEMP%\NtfsAudit`,
- `%LOCALAPPDATA%\NtfsAudit\Cache`,
- `%LOCALAPPDATA%\NtfsAudit\Logs`,
- file export (`.xlsx`, `.ntaudit`),
- queue job servizio,
- stato runtime servizio,
- workspace analisi import/export.

Nuovo shortcut:
- `-CleanAnalysisWorkspace` => pulisce `%TEMP%\NtfsAudit\imports` e `%TEMP%\NtfsAudit\exports`.

## Comandi rapidi

```powershell
# build completa
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release

# clean esteso + build
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release -RunClean -CleanAllTemp -CleanScanData -CleanAnalysisWorkspace

# pulizia solo workspace analisi
powershell -ExecutionPolicy Bypass -File .\scripts\clean.ps1 -CleanAnalysisWorkspace
```

## Note operative

- Progetto orientato a Windows (WPF, ACL NTFS native, service control manager).
- In ambienti Linux/macOS parte delle funzionalità non è eseguibile.
- In ambiente senza SDK .NET non è possibile eseguire `dotnet build/test/publish`.
