# NTFS Audit

NTFS Audit è un’applicazione desktop WPF per analizzare autorizzazioni NTFS su alberi di cartelle Windows, con risoluzione identità (SID/utente/gruppo), supporto opzionale ai permessi SMB Share, calcolo dell’accesso effettivo e confronto ACL rispetto a baseline. Il progetto include anche un viewer dedicato all’apertura delle analisi archiviate.

## Obiettivo

Lo scopo del progetto è offrire una vista operativa e strutturata delle autorizzazioni, aiutando attività di:

- audit di sicurezza file server;
- verifica ereditarietà ACL ed eccezioni (deny/protected/explicit);
- individuazione di gruppi e account ad alto impatto (es. Everyone, Authenticated Users, account di servizio, account amministrativi);
- confronto differenziale ACL (cartella vs padre e baseline attesa);
- esportazione report e archiviazione analisi per revisione successiva.

## Componenti della soluzione

La soluzione contiene tre progetti principali:

- `src/NtfsAudit.App`: applicazione WPF principale (scanner + analisi + export/import).
- `src/NtfsAudit.Viewer`: viewer WPF per aprire analisi salvate.
- `tests/NtfsAudit.App.Tests`: test automatici dei servizi core.

## Funzionalità principali

### Scansione ACL NTFS

- Scansione da percorso radice locale o UNC.
- Profondità configurabile (`MaxDepth`) con opzione scansione completa (`ScanAllDepths`).
- Inclusione/esclusione ACE ereditate (`IncludeInherited`).
- Opzione di scansione file oltre alle cartelle (`IncludeFiles`).
- Lettura owner e SACL (se abilitata e con privilegi adeguati).

### Risoluzione identità

- Risoluzione SID → account tramite cache locale.
- Risoluzione AD tramite provider multipli (Directory Services e PowerShell, in base alle opzioni).
- Espansione gruppi per analisi membership (`ExpandGroups`).
- Filtri dedicati per escludere o evidenziare account di servizio e amministrativi.

### Audit avanzato

Quando attivo (`EnableAdvancedAudit`):

- acquisizione permessi Share SMB (`IncludeSharePermissions`);
- calcolo accesso effettivo (`ComputeEffectiveAccess`) combinando layer NTFS/Share;
- confronto con baseline ACL (`CompareBaseline`).

### Navigazione e analisi UI

- Albero cartelle con indicatori visuali (es. nodi protetti, deny espliciti, mismatch baseline, presenza file).
- Filtri tree rapidi con reset immediato.
- Tab separati per:
  - permessi gruppi;
  - permessi utenti;
  - dettaglio ACL completo;
  - permessi Share;
  - effective access;
  - riepilogo info permessi;
  - errori di scansione/import/export.
- Highlight dei diritti per priorità/rischio (read/write/modify/full/deny).

### Esportazione e archiviazione

- Export report Excel (`.xlsx`) per condivisione e audit formale.
- Export analisi in formato archivio (`.ntaudit`).
- Import analisi archiviate per revisione offline tramite app principale o viewer.

### Caching e performance

- Cache locale per risoluzione SID.
- Cache membership gruppi con TTL.
- Persistenza preferenze UI e percorsi usati di recente.

## Requisiti

- Windows (l’app è WPF e target `net6.0-windows` / `net8.0-windows`).
- .NET SDK 8 consigliato per build e test.
- Permessi adeguati all’analisi del filesystem target (per audit avanzato è consigliata esecuzione elevata).

## Build e test

### Comandi `dotnet`

```powershell
dotnet restore NtfsAudit.sln
dotnet build NtfsAudit.sln -c Release
dotnet test NtfsAudit.sln -c Release
```

### Script PowerShell inclusi

Build/publish completo:

```powershell
./scripts/build.ps1 -Configuration Release
```

Opzioni comuni:

- `-Framework net8.0-windows`
- `-Runtime win-x64`
- `-SelfContained`
- `-PublishSingleFile`
- `-PublishReadyToRun`
- `-SkipTests`
- `-RunClean`

Pulizia artefatti/cache/temp:

```powershell
./scripts/clean.ps1
```

Opzioni utili:

- `-CleanAllTemp`
- `-CleanImports`
- `-CleanCache`
- `-CleanLogs`
- `-CleanExports`

## Esecuzione

Dopo build/publish, avviare:

- `NtfsAudit.App` per scansioni complete, export/import e analisi interattiva;
- `NtfsAudit.Viewer` per consultazione analisi `.ntaudit`.

Flusso operativo tipico:

1. impostare percorso radice;
2. configurare opzioni di scansione (profondità, filtri, audit avanzato);
3. avviare scansione;
4. usare tree e tab per analisi dettagliata;
5. esportare in Excel o salvare archivio `.ntaudit`.

## Struttura tecnica (alto livello)

- **ViewModel/UI**: orchestrazione comandi, stato scansione, filtri e binding WPF.
- **Servizi**:
  - scansione ACL e costruzione albero;
  - risoluzione identità e classificazione SID;
  - espansione gruppi AD;
  - normalizzazione diritti e calcolo effective access;
  - confronto ACL/baseline;
  - gestione archivi analisi e export Excel.
- **Modelli**: opzioni scansione, dettaglio cartelle, ACE, differenze ACL, errori.
- **Cache**: SID, membership gruppi, preferenze locali.

## Note operative

- Su share remoti/DFS il comportamento dipende dalla raggiungibilità e dai permessi disponibili.
- Le opzioni avanzate possono aumentare i tempi di scansione su alberi molto grandi.
- Per risultati completi su owner/SACL/share/effective access è raccomandata esecuzione con privilegi elevati.
