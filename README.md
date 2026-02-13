# NTFS Audit

NTFS Audit è una suite Windows per analisi ACL NTFS/SMB con modalità interattiva e batch via servizio.

Componenti:
- **NtfsAudit.App** (WPF): scansione, filtri, export/import, invio job al servizio.
- **NtfsAudit.Service** (Windows Service): esecuzione job in background da `%ProgramData%\NtfsAudit\jobs`.
- **NtfsAudit.Viewer**: apertura in sola lettura degli archivi `.ntaudit`.

---

## 1) Architettura repository

- `src/NtfsAudit.App`
  - ViewModel/UI, orchestrazione scansione, import/export.
  - Servizi di risoluzione path (locale/UNC/DFS/NFS) e risoluzione identità.
- `src/NtfsAudit.Service`
  - Worker che legge i job JSON e processa più root in sequenza.
- `src/NtfsAudit.Viewer`
  - Viewer dedicato per consultazione analisi archiviate.
- `tests/NtfsAudit.App.Tests`
  - Test unitari servizi (permessi, baseline, resolver).
- `scripts/build.ps1`
  - Pipeline restore/build/test/publish con clean opzionale.
- `scripts/clean.ps1`
  - Pulizia selettiva artefatti locali, cache, import/export e job servizio.

---

## 2) Flussi di scansione

### Modalità App (interattiva)
1. Selezione di una o più root.
2. Scansione immediata.
3. Rendering albero + griglie ACL + errori.
4. Export opzionale in `.xlsx` e/o `.ntaudit`.

### Modalità servizio Windows
1. App serializza un job JSON in `%ProgramData%\NtfsAudit\jobs`.
2. Service legge i file `job_*.json` in ordine.
3. Ogni root del job viene elaborata indipendentemente.
4. Se configurato `OutputDirectory`, viene prodotto un archivio `.ntaudit` per root.

---

## 3) Installazione/disinstallazione servizio

Dalla UI App:
- **Installazione**
  - Ricerca `NtfsAudit.Service.exe` / `.dll` in percorsi noti.
  - `sc create ...` con fallback robusto:
    - se il servizio esiste già (`1073`), esegue `sc config` per aggiornare `binPath` e startup.
  - Applica descrizione (`sc description`) e tenta avvio (`sc start`).
- **Disinstallazione**
  - `sc stop` e `sc delete` con gestione idempotente:
    - ignora stati attesi (es. servizio già fermo/inesistente).

L’esecuzione di `sc` gestisce fallback con elevazione (`runas`) in caso di access denied.

---

## 4) Import / Export

## 4.1 Export Excel (`.xlsx`)
- Origine: `data.jsonl` + `errors.jsonl` della scansione corrente.
- Pre-check obbligatorio: file dati scansione presente e non mancante.
- Post-check obbligatorio: file output creato e non vuoto.
- Split automatico in più sheet se superato il limite righe Excel.

## 4.2 Export analisi (`.ntaudit`)
- Formato: archivio zip con entry:
  - `data.jsonl`
  - `errors.jsonl`
  - `tree.json`
  - `folderflags.json`
  - `meta.json`
- Export atomico:
  - scrittura su file temporaneo;
  - sostituzione finale output solo a export completato.
- `meta.json` include root, path kind, timestamp e opzioni scansione.

## 4.3 Import analisi (`.ntaudit`)
- Estrazione in temp folder dedicata:
  - `%TEMP%\NtfsAudit\imports\<archive>_yyyy_MM_dd_HH_mm_<guid>`
- Gestione robusta archivi legacy/parziali:
  - se manca una entry opzionale, usa fallback ricostruzione (es. tree da export records).
- Validazioni post-import:
  - presenza file dati,
  - dataset non vuoto,
  - struttura `TreeMap` / `Details` coerente.

---

## 5) Logica checkbox e filtri

## 5.1 Filtri ACL (griglie)
Vincoli “almeno una opzione attiva”:
- `ShowAllow` / `ShowDeny`
- `ShowInherited` / `ShowExplicit`

Categorie principal (almeno una attiva):
- `ShowEveryone`
- `ShowAuthenticatedUsers`
- `ShowServiceAccounts`
- `ShowAdminAccounts`
- `ShowOtherPrincipals`

Se l’utente spegne tutte le categorie principal, viene riattivata automaticamente `ShowOtherPrincipals`.

Ricerca testuale (`AclFilter`) applicata su:
- nome/SID principal,
- layer, tipo allow/deny,
- rights summary,
- path/cartella,
- owner/share/server/source/risk,
- membri gruppo (se presenti).

## 5.2 Filtri albero
Filtri supportati:
- `TreeFilterExplicitOnly`
- `TreeFilterInheritanceDisabledOnly`
- `TreeFilterDiffOnly`
- `TreeFilterExplicitDenyOnly`
- `TreeFilterBaselineMismatchOnly`
- `TreeFilterFilesOnly`
- `TreeFilterFoldersOnly`

Regole:
- `FilesOnly` e `FoldersOnly` non possono essere entrambi OFF.
- I filtri categoria sono in OR tra loro (nodo incluso se soddisfa almeno un criterio attivo).
- I filtri tipo risorsa supportano valori localizzati (`Folder`/`Cartella`/`Directory`, `File`).

---

## 6) Tipi percorso e visualizzazione root

Rilevazione tipo percorso:
- `Local`
- `UNC/SMB`
- `DFS`
- `NFS`

In UI:
- badge tipo percorso;
- colore differenziato per tipo;
- badge `DFS multi-server` se namespace risolve più target.

---

## 7) Script build/clean

## `scripts/build.ps1`
Pipeline:
- restore
- build
- test
- publish App/Viewer/Service

Opzioni principali:
- `-Configuration Release|Debug`
- `-SkipRestore`, `-SkipBuild`, `-SkipTests`, `-SkipPublish`
- `-Framework`, `-Runtime`, `-SelfContained`
- `-PublishSingleFile`, `-PublishReadyToRun`
- `-RunClean` + flag clean forwardati a `clean.ps1`

Pulizie disponibili anche in build:
- `-CleanAllTemp`
- `-CleanImports`
- `-CleanCache`
- `-CleanLogs`
- `-CleanExports`
- `-CleanServiceJobs`

Nota: `-CleanAllTemp` abilita automaticamente anche pulizia log/export/job servizio.

## `scripts/clean.ps1`
Pulizia selettiva:
- bin/obj, `.vs`, `dist`, `artifacts`
- `%TEMP%\NtfsAudit` (import/export/log)
- `%LOCALAPPDATA%\NtfsAudit\Cache`
- `%LOCALAPPDATA%\NtfsAudit\Logs`
- file export `.xlsx`/`.ntaudit`
- `%ProgramData%\NtfsAudit\jobs\job_*.json`

Modalità specifiche:
- `-ImportsOnly`
- `-CacheOnly`
- `-CleanAllTemp`
- combinazioni con `-Keep*` per preservare aree specifiche.

---

## 8) Comandi rapidi

```powershell
# build + test
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release

# build + publish + clean esteso
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release -RunClean -CleanAllTemp
```

---

## 9) Note operative

- Repository orientato a Windows (.NET desktop + ACL NTFS native).
- In ambienti non Windows alcune funzionalità non sono eseguibili (WPF, service, ACL native).
- I resolver path includono fallback difensivi per ambienti non Windows quando API di rete Windows non sono disponibili.
