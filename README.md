# NTFS Audit

NTFS Audit è una suite Windows composta da:
- **NtfsAudit.App**: applicazione WPF per scansione ACL e analisi.
- **NtfsAudit.Service**: servizio Windows per esecuzione job in background.
- **NtfsAudit.Viewer**: viewer in sola lettura per aprire archivi `.ntaudit`.

Supporta percorsi locali, UNC/SMB, DFS e NFS (con limiti noti su metadati disponibili da Windows).

## Architettura repository

- `src/NtfsAudit.App`: UI, orchestrazione scansione, export/import, filtri, persistenza preferenze.
- `src/NtfsAudit.Service`: worker che legge i job e lancia scansioni batch.
- `src/NtfsAudit.Viewer`: apertura analisi archiviate.
- `tests/NtfsAudit.App.Tests`: test unitari (servizi e calcolo permessi).
- `scripts/build.ps1`: pipeline locale build/test/publish + pulizia opzionale.
- `scripts/clean.ps1`: pulizia selettiva artefatti, cache, import/export, job servizio.

## Scansione

### Modalità

1. **Interattiva (App)**
   - selezione una o più root;
   - esecuzione immediata;
   - visualizzazione progress e risultati nella UI.

2. **Servizio Windows**
   - l’App serializza un job JSON in `%ProgramData%\\NtfsAudit\\jobs`;
   - il service processa tutte le root presenti nel job;
   - una root in errore non interrompe le successive;
   - per ogni root viene esportato un `.ntaudit` dedicato (se output configurato).

### Naming output `.ntaudit`

Per export automatici batch (App e Service):

`<nome_cartella_scansionata>_yyyy_MM_dd_HH_mm.ntaudit`

Esempi:
- `Finance_2026_02_13_09_45.ntaudit`
- `Engineering_2026_02_13_10_02.ntaudit`

## Import / Export

### Export Excel (`.xlsx`)

- disponibile da App su scansione caricata;
- include dati ACL e metadati utili per analisi;
- filename proposto con root sanitizzata + timestamp `yyyy_MM_dd_HH_mm`.

### Export analisi (`.ntaudit`)

- crea archivio zip con:
  - `data.jsonl`
  - `errors.jsonl`
  - `tree.json`
  - `folderflags.json`
  - `meta.json`
- il metadata include root, path kind, opzioni scansione e timestamp scansione.

### Import analisi (`.ntaudit`)

- valida struttura archivio;
- ricostruisce dettaglio ACL e mappa albero;
- applica opzioni importate (quando presenti);
- usa una cartella temp per import con naming leggibile:
  - `<nome_archivio>_yyyy_MM_dd_HH_mm_<guid>`.

## Logica checkbox e filtri

## Opzioni scansione

- `ResolveIdentities` abilita/disabilita blocco identity resolution.
  - quando disattivata, forzano OFF:
    - `ExpandGroups`
    - `UsePowerShell`
    - `ExcludeServiceAccounts`
    - `ExcludeAdminAccounts`
- `EnableAdvancedAudit` abilita/disabilita funzioni avanzate.
  - quando disattivata, forzano OFF:
    - `ComputeEffectiveAccess`
    - `IncludeSharePermissions`
    - `IncludeFiles`
    - `ReadOwnerAndSacl`
    - `CompareBaseline`

## Filtri ACL (grid)

- Coppie con vincolo “almeno una attiva”:
  - `ShowAllow` / `ShowDeny`
  - `ShowInherited` / `ShowExplicit`
- Categorie principal con vincolo “almeno una attiva”:
  - `ShowEveryone`
  - `ShowAuthenticatedUsers`
  - `ShowServiceAccounts`
  - `ShowAdminAccounts`
  - `ShowOtherPrincipals`
- Se l’utente tenta di spegnere tutte le categorie principal, viene riattivata automaticamente `ShowOtherPrincipals`.

## Filtri albero

- `TreeFilterFilesOnly` e `TreeFilterFoldersOnly` non possono essere entrambi OFF.
- Filtri cumulativi:
  - solo ACE esplicite,
  - solo inheritance disabilitata,
  - solo differenze baseline,
  - solo deny espliciti,
  - solo mismatch baseline,
  - files/folders.

## Evidenziazione tipo percorso

Nella lista root dell’App:
- badge `DFS`, `SMB Share`, `NFS`, `Locale`;
- colore di sfondo differenziato per tipo percorso;
- badge `DFS multi-server` quando la namespace DFS risolve più target server.

## Script build e clean

## `scripts/build.ps1`

Supporta:
- restore/build/test/publish;
- publish App + Viewer + Service;
- clean integrato (`-RunClean`) con forwarding opzioni.

Opzioni clean rilevanti:
- `-CleanAllTemp`
- `-CleanImports`
- `-CleanCache`
- `-CleanLogs`
- `-CleanExports`
- `-CleanServiceJobs` (nuovo): rimuove `job_*.json` da `%ProgramData%\\NtfsAudit\\jobs`.

## `scripts/clean.ps1`

Pulizia selettiva di:
- bin/obj e dist/artifacts;
- temp import/export;
- cache locale;
- log;
- export `.xlsx`/`.ntaudit`;
- job servizio (`-CleanServiceJobs`).

## Build rapida

```powershell
# build + test
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release

# build + publish + clean profondo
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release -RunClean -CleanAllTemp -CleanExports -CleanServiceJobs
```

## Note operative

- Il repository è orientato a Windows (.NET desktop + ACL NTFS).
- In ambienti non Windows alcune funzionalità non sono eseguibili (UI WPF, service install, ACL native).
