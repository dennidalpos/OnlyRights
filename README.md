# NTFS Audit

NTFS Audit è una soluzione desktop per Windows (WPF) dedicata all’analisi dei permessi NTFS su percorsi locali e UNC. Include due eseguibili:

- **NtfsAudit.App** per la scansione e l’export dei risultati.
- **NtfsAudit.Viewer** per aprire archivi `.ntaudit` in modalità sola lettura.

L’applicazione legge le ACL delle cartelle, normalizza i diritti, può risolvere i SID in nomi leggibili e, se richiesto, espandere i gruppi annidati. I dati possono essere esportati in Excel, HTML o archivi `.ntaudit` riutilizzabili.

## Indice

- [Panoramica](#panoramica)
- [Requisiti](#requisiti)
- [Avvio rapido](#avvio-rapido)
- [Architettura e progetti](#architettura-e-progetti)
- [Flusso di scansione](#flusso-di-scansione)
- [Opzioni di scansione](#opzioni-di-scansione)
- [Export](#export)
- [Formato archivi .ntaudit](#formato-archivi-ntaudit)
- [Viewer](#viewer)
- [Script di build](#script-di-build)
- [File temporanei](#file-temporanei)
- [Troubleshooting](#troubleshooting)

## Panoramica

Durante una scansione, NTFS Audit:

1. Normalizza il percorso (locale o UNC).
   - Per i namespace DFS seleziona prima il target con priorità più alta e, se non disponibile, usa i fallback.
2. Costruisce la struttura ad albero delle cartelle.
3. Legge le ACE (Allow/Deny) per ogni cartella.
4. Normalizza i diritti (Full, Modify, Read, ecc.).
5. (Opzionale) Risolve i SID in nomi e stato (utente/gruppo/disabled).
6. (Opzionale) Espande gruppi annidati per ricavare i membri effettivi.
7. Memorizza i risultati in file temporanei riutilizzabili.

L’interfaccia mostra:

- albero cartelle con caricamento lazy,
- griglie ACL separate per utenti/gruppi e vista completa,
- filtri per utente/SID/diritti/allow-deny,
- dashboard riassuntiva con indicatori rischio e baseline,
- contatori e progressi della scansione,
- pannello errori.

## Requisiti

- **Windows 10/11 o Windows Server 2016+** (target `net8.0-windows`).
- **Windows Server 2012 R2+** (target `net6.0-windows`).
- **.NET SDK 6+** per compilazione (multi-target `net6.0-windows;net8.0-windows`).
- Permessi di lettura ACL sulle cartelle analizzate.
- (Opzionale) Modulo **ActiveDirectory** / RSAT se si usa la risoluzione AD via PowerShell.

## Avvio rapido

1. Compila il progetto (vedi [Script di build](#script-di-build)).
2. Avvia `NtfsAudit.App.exe` per la scansione o `NtfsAudit.Viewer.exe` per consultare un archivio `.ntaudit`.
3. Seleziona il percorso root (`C:\...` o `\\server\share\...`).
4. Configura profondità e opzioni di scansione.
5. Avvia la scansione con **Start**.
6. Al termine, usa **Export Excel**, **Export HTML** o **Export Analisi**.

## Architettura e progetti

- **`src/NtfsAudit.App`**: applicazione principale WPF per scansione e export.
- **`src/NtfsAudit.Viewer`**: viewer WPF per import `.ntaudit` in sola lettura.
- **`scripts`**: script PowerShell per build/publish e pulizia.

Entrambi i progetti condividono lo stesso modello dati e i servizi di scanning/export.

## Flusso di scansione

La scansione è sequenziale per cartella e registra informazioni utili sia per la UI che per gli export.

- **AccessControl**: lettura ACL con `GetAccessRules`.
- **Normalizzazione diritti**: conversione `FileSystemRights` in stringhe leggibili.
- **Risoluzione identità**: SID → nome (con fallback AD o PowerShell).
- **Espansione gruppi**: opzionale, mediante resolver AD o DirectoryServices.
- **Persistenza temporanea**: JSONL per righe ACL e errori.

## Opzioni di scansione

Le opzioni principali influenzano prestazioni e dettaglio dei risultati:

- **Analizza tutte le sottocartelle**: ignora `MaxDepth` e scansiona l’intero albero.
- **Includi permessi ereditati**: include ACE ereditate nella griglia.
- **Risolvi identità (più lento)**: traduce i SID in nomi leggibili.
- **Espandi gruppi annidati**: risolve i membri effettivi dei gruppi.
- **Usa PowerShell per AD**: preferisce l’AD via PowerShell se disponibile.
- **Escludi utenti di servizio**: filtra account di servizio/built-in del sistema tramite SID noti (LocalSystem, LocalService, NetworkService, NT SERVICE).
- **Escludi utenti admin**: filtra account/gruppi privilegiati tramite SID noti (es. Domain Admins, Schema Admins, Builtin Administrators).
- **Colora per diritto**: evidenzia visivamente le ACL.
- **Abilita audit avanzato**: abilita funzioni di audit estese.
- **Calcola Effective Access**: calcolo base con merge Allow/Deny (non considera ordine ACE o membership avanzata).
- **Scansiona file**: include i file oltre alle cartelle.
- **Leggi Owner e SACL**: arricchisce le ACE con owner e policy di audit.
- **Confronta con baseline/policy attese**: calcola differenze rispetto alla baseline del percorso root.
- **Nota**: quando “Risolvi identità” è disattivato, le opzioni AD e filtro account vengono automaticamente disabilitate. Se “Abilita audit avanzato” è disattivato, le opzioni avanzate vengono azzerate.
- **Nota DFS**: le cartelle di cache DFS come `DfsrPrivate`/`DFSCache` non vengono scansionate.

## Export

### Export Excel (.xlsx)

Genera un file con quattro fogli:

- **Users**: ACE degli utenti.
- **Groups**: ACE dei gruppi.
- **Acl**: tutte le ACE.
- **Errors**: errori di accesso raccolti durante la scansione.

Colonne tipiche:

- `FolderPath`, `PrincipalName`, `PrincipalSid`, `PrincipalType`
- `AllowDeny`, `RightsSummary`, `RightsMask`, `EffectiveRightsSummary`, `EffectiveRightsMask`
- `InheritanceFlags`, `PropagationFlags`, `Source`, `Depth`
- `ResourceType`, `TargetPath`, `Owner`, `AuditSummary`, `RiskLevel`
- `IsDisabled`, `IsServiceAccount`, `IsAdminAccount`
- `HasExplicitPermissions`, `IsInheritanceDisabled`
- `IncludeInherited`, `ResolveIdentities`, `ExcludeServiceAccounts`, `ExcludeAdminAccounts`
- `EnableAdvancedAudit`, `ComputeEffectiveAccess`, `IncludeFiles`, `ReadOwnerAndSacl`, `CompareBaseline`

Formato nome file:

```
<NomeCartellaRoot>_<dd-MM-yyyy-HH-mm>.xlsx
```

Nota: l’export verifica la presenza del file dati della scansione e crea automaticamente la cartella di destinazione se non esiste.

### Export HTML (.html)

Produce un file HTML autoconclusivo che replica la vista corrente:

- albero cartelle e stato di espansione,
- cartella selezionata,
- tab con ACL utenti/gruppi/complete,
- tab errori con filtri applicati,
- filtri applicati,
- colori per diritto.

Formato nome file:

```
<NomeCartellaRoot>_<dd-MM-yyyy-HH-mm>.html
```

Nota: la cartella di destinazione viene creata automaticamente se non presente.

### Export Analisi (.ntaudit)

Archivia i dati della scansione in un singolo file `.ntaudit`:

- ACL in JSONL,
- errori in JSONL,
- mappa dell’albero,
- metadati (root e timestamp).

Formato nome file consigliato:

```
<NomeCartellaRoot>_<dd-MM-yyyy-HH-mm>.ntaudit
```

## Formato archivi .ntaudit

Un archivio `.ntaudit` è uno ZIP con le seguenti entry:

- `data.jsonl`: righe ACL.
- `errors.jsonl`: errori di accesso.
- `tree.json`: mappa albero cartelle.
- `meta.json`: metadati (root/timestamp).
- `folderflags.json`: flag aggiuntivi per folder.

Se `errors.jsonl` manca, l’import procede con un set vuoto. In fase di import, i flag `IsServiceAccount` e `IsAdminAccount` vengono ricalcolati da SID per garantire coerenza con le regole correnti.

## Viewer

Il viewer riutilizza la stessa interfaccia della scansione, ma disabilita le funzioni di analisi:

- consente solo **Import Analisi**,
- importa anche il pannello errori e i filtri associati,
- nasconde i controlli di scansione,
- rende la UI completamente in sola lettura.

## Script di build

### `scripts/build.ps1`

Compila e (opzionalmente) pubblica le app:

```
.\scripts\build.ps1 -Configuration Release
```

Parametri principali:

- `-Configuration Release|Debug`
- `-SkipRestore`
- `-SkipBuild`
- `-SkipPublish`
- `-SkipViewerPublish`
- `-SkipPublishClean`
- `-CleanTemp` (rimuove `%TEMP%\NtfsAudit` prima della build/publish)
- `-CleanCache` (pulisce `%LOCALAPPDATA%\NtfsAudit\Cache`)
- `-Framework <tfm>` (es. `net8.0-windows`)
- `-OutputPath <cartella>`
- `-Runtime <rid>` (es. `win-x64`)
- `-SelfContained`
- `-PublishSingleFile`
- `-PublishReadyToRun`

Output di default: `dist\<Configuration>` (con eventuale `<Runtime>` e/o `<Framework>`). Se `-OutputPath` è relativo, viene risolto rispetto alla root del repository. Il viewer viene pubblicato in `dist\<Configuration>\Viewer`.

### `scripts/clean.ps1`

Pulisce build, dist e cache:

```
.\scripts\clean.ps1
```

Parametri:

- `-Configuration <Release|Debug>`
- `-Framework <tfm>`
- `-Runtime <rid>`
- `-OutputPath <cartella>` (pulisce la cartella di output specificata, coerente con `build.ps1`)
- `-TempRoot <path>` (override di `%TEMP%` per i file temporanei)
- `-KeepDist`
- `-KeepArtifacts`
- `-KeepTemp`
- `-KeepImportTemp` (mantiene solo `NtfsAudit\\imports` anche con `-KeepTemp`)
- `-KeepCache`

## File temporanei

Durante la scansione vengono creati file in `%TEMP%\NtfsAudit`:

- `scan_*.jsonl`: righe ACL.
- `errors_*.jsonl`: errori di accesso.

Questi file sono riutilizzati per gli export e possono essere rimossi tramite `clean.ps1`.

## Troubleshooting

- **Accesso negato**: gli errori vengono registrati, la scansione continua.
- **AD non disponibile**: disattiva “Usa PowerShell per AD” o installa RSAT.
- **Prestazioni**: riduci la profondità o disattiva risoluzione identità/espansione gruppi.
- **Compatibilità 2012 R2**: usa `net6.0-windows` per server legacy.
