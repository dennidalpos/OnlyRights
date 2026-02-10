# NTFS Audit

NTFS Audit è una soluzione desktop per Windows (WPF) dedicata all’analisi dei permessi NTFS su percorsi locali e UNC. Include due eseguibili:

- **NtfsAudit.App** per la scansione e l’export dei risultati.
- **NtfsAudit.Viewer** per aprire archivi `.ntaudit` in modalità sola lettura.

L’applicazione legge le ACL delle cartelle, normalizza i diritti, può risolvere i SID in nomi leggibili e, se richiesto, espandere i gruppi annidati. I dati possono essere esportati in Excel o archivi `.ntaudit` riutilizzabili.

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
   - Esclude le cartelle di sync/cache DFS (es. `DfsrPrivate`, `System Volume Information\\DFSR`).
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
6. Al termine, usa **Export Excel** o **Export Analisi**.

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
- Le opzioni legate all’identità (PowerShell/Esclusioni/Espansione) sono disponibili solo quando la risoluzione identità è attiva.
- Quando “Risolvi identità” viene disattivato, le opzioni dipendenti (espansione gruppi, PowerShell, esclusioni) vengono automaticamente disabilitate.
- **Colora per diritto**: evidenzia visivamente le ACL.
- **Abilita audit avanzato**: abilita funzioni di audit estese.
- L’audit avanzato è **attivo di default**; le opzioni figlie (Effective Access, Owner/SACL, baseline) sono attivate di default, mentre **“Scansiona file” resta disattivo** per limitare i tempi di scansione.
- **Calcola Effective Access**: calcolo base con merge Allow/Deny (non considera ordine ACE o membership avanzata).
- **Leggi permessi Share (SMB)**: interroga le ACL del share SMB e le combina con le ACL NTFS per ottenere l’Effective Access reale (intersezione).
- **Scansiona file**: include i file oltre alle cartelle.
- **Leggi Owner e SACL**: arricchisce le ACE con owner e policy di audit.
- **Confronta con baseline/policy attese**: calcola differenze rispetto alla baseline del percorso root.
- Le opzioni avanzate sono attive solo quando è selezionato “Abilita audit avanzato”.
- Disattivando l’audit avanzato, le opzioni figlie (Effective Access, file, owner/SACL, baseline) vengono azzerate per mantenere coerenza con la scansione.
- La lettura SACL richiede il privilegio `SeSecurityPrivilege`: se non disponibile o negata, la colonna SACL/Audit riporta un messaggio esplicativo.

### Filtri risultati

### Filtri TreeView (nuovo)

La TreeView supporta filtri rapidi per isolare nodi con variazioni sui permessi:
- permessi espliciti,
- ereditarietà disabilitata,
- differenze vs parent,
- deny espliciti,
- mismatch baseline,
- rischio High/Medium/Low.

I filtri preservano la gerarchia mostrando automaticamente gli antenati necessari alla navigazione.


I filtri nella vista risultati funzionano in combinazione:

- **Filtro ACL**: ricerca per nome, SID, allow/deny, diritti, effective access, percorso/nome cartella, SACL/audit, source, resource type, target path, owner, risk level e membri espansi.
- **Everyone / Authenticated Users**: mostra solo le ACE con i rispettivi SID o nome equivalente.
- **Utenti di servizio**: mostra solo le ACE marcate come account di servizio.
- **Admin**: mostra solo le ACE marcate come account amministrativi.
- **Solo Deny**: mostra solo ACE di tipo deny.
- **Ereditarietà disabilitata**: filtra le ACE con ereditarietà disabilitata.
- Se **Allow** e **Deny** sono entrambi disattivati, il risultato è vuoto.
- Se **Ereditati** ed **Espliciti** sono entrambi disattivati, il risultato è vuoto.
- Se tutte le categorie principali (Everyone, Authenticated Users, Service, Admin, Altri) sono disattivate, il risultato è vuoto.

## Export

### Processo comune di export

Gli export partono sempre dai risultati correnti della scansione/import:

1. Seleziona la cartella root e applica i filtri desiderati.
2. Avvia l’export dalla toolbar.
3. Scegli nome e percorso tramite dialog di salvataggio (il default usa la root e l’ultima cartella usata).

Il file generato riflette:

- per **Excel**: i dati grezzi della scansione (gli eventuali filtri UI non vengono applicati all’export),
- per **.ntaudit**: opzioni di scansione e metadati (oltre ai dati ACL completi).

### Export Excel (.xlsx)

Genera un file con tre fogli:

- **Users_#**: ACE degli utenti (spezzato in più fogli se necessario).
- **Groups_#**: ACE dei gruppi (spezzato in più fogli se necessario).
- **Errors**: errori di accesso raccolti durante la scansione.

Caratteristiche principali:

- Export in streaming (no caricamento completo in memoria).
- Solo utenti e gruppi: il foglio ACL completo non viene generato.
- La colonna `FolderName` contiene solo il nome cartella, senza il percorso completo.
- Se si supera il limite Excel (1.048.576 righe), i dati vengono suddivisi in più fogli e l’app mostra un warning.

Colonne tipiche:

- `FolderName`, `PrincipalName`, `PrincipalSid`, `PrincipalType`
- `PermissionLayer`, `AllowDeny`, `RightsSummary`, `EffectiveRightsSummary`
- `IsInherited`, `AppliesToThisFolder`, `AppliesToSubfolders`, `AppliesToFiles`
- `HasExplicitPermissions`, `IsInheritanceDisabled`
- `InheritanceFlags`, `PropagationFlags`, `Source`, `Depth`
- `ResourceType`, `TargetPath`, `Owner`, `ShareName`, `ShareServer`
- `AuditSummary`, `RiskLevel`, `Disabilitato`
- `IsServiceAccount`, `IsAdminAccount`

Formato nome file:

```
<NomeCartellaRoot>_<dd-MM-yyyy-HH-mm>.xlsx
```

Il dialog di export propone automaticamente questo nome e ricorda l’ultima cartella di salvataggio.

### Export Analisi (.ntaudit)

Archivia i dati della scansione in un singolo file `.ntaudit`:

- ACL in JSONL,
- errori in JSONL,
- mappa dell’albero,
- metadati (root e timestamp).
- opzioni di scansione (ripristinate all’import).

Durante l’export, se la mappa dell’albero non è disponibile o incompleta viene rigenerata dal dataset di ACL; il root viene dedotto dal percorso fornito o dalle opzioni di scansione salvate.

Formato nome file consigliato:

```
<NomeCartellaRoot>_<dd-MM-yyyy-HH-mm>.ntaudit
```

Il file esportato è riutilizzabile in **NtfsAudit.App** e **NtfsAudit.Viewer**.

## Import Analisi

L’import può essere eseguito da entrambi gli eseguibili:

- **NtfsAudit.App**: importa un archivio `.ntaudit`, ripristina opzioni di scansione, ricostruisce albero e risultati.
- **NtfsAudit.Viewer**: importa lo stesso archivio ma blocca le funzioni di scansione.

Flusso consigliato:

1. Apri **Import Analisi** dalla toolbar.
2. Seleziona il file `.ntaudit` (il dialog ricorda l’ultima cartella usata).
3. Conferma eventuali avvisi di compatibilità.
4. Verifica che la cartella root importata sia corretta e seleziona il nodo principale.

Nota: l’import ricalcola i flag di servizio/admin a partire dai SID correnti. Se il root non è presente nei metadati, viene ricostruito dalle opzioni di scansione o dai record presenti.

## Formato archivi .ntaudit

Un archivio `.ntaudit` è uno ZIP con le seguenti entry:

- `data.jsonl`: righe ACL.
- `errors.jsonl`: errori di accesso.
- `tree.json`: mappa albero cartelle.
- `meta.json`: metadati (root/timestamp/versione schema).
- `folderflags.json`: flag aggiuntivi per folder.

Se `errors.jsonl` manca, l’import procede con un set vuoto. In fase di import, i flag `IsServiceAccount` e `IsAdminAccount` vengono ricalcolati da SID per garantire coerenza con le regole correnti.
Le opzioni di scansione vengono lette dal record `SCAN_OPTIONS` presente in `data.jsonl`.

### Migrazione archivi legacy

Gli archivi creati con versioni precedenti non includono le informazioni sul layer Share/Effective e la versione di schema.
Per migrare:

1. Apri l’archivio `.ntaudit` in **NtfsAudit.Viewer**.
2. Esporta nuovamente l’analisi con **Export Analisi**: il file rigenera `meta.json` con versione e serializza i layer Share/Effective.
3. Per ottenere dati Share/Effective aggiornati, riesegui una scansione con **Leggi permessi Share (SMB)** attivo.

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
- `-CleanAllTemp` (pulisce temp, import e cache in un’unica operazione)
- `-CleanTemp` (rimuove `%TEMP%\NtfsAudit` prima della build/publish)
- `-CleanImports` (pulisce `%TEMP%\NtfsAudit\\imports`)
- `-CleanCache` (pulisce `%LOCALAPPDATA%\NtfsAudit\Cache`)
- `-CleanDist` (rimuove la cartella `dist` di output prima di build/publish)
- `-CleanArtifacts` (rimuove la cartella `artifacts`)
- `-TempRoot <path>` (override di `%TEMP%`/`%TMP%` per le pulizie)
- `-Framework <tfm>` (es. `net8.0-windows`)
- `-OutputPath <cartella>` (supporta percorsi relativi alla root del repo)
- `-Runtime <rid>` (es. `win-x64`)
- `-SelfContained`
- `-PublishSingleFile`
- `-PublishReadyToRun`

Output di default: `dist\<Configuration>` (con eventuale `<Runtime>` e/o `<Framework>`). Il viewer viene pubblicato in `dist\<Configuration>\Viewer`.

### `scripts/clean.ps1`

Pulisce build, dist e cache:

```
.\scripts\clean.ps1
```

Parametri:

- `-Configuration <Release|Debug>`
- `-Framework <tfm>`
- `-Runtime <rid>`
- `-TempRoot <path>` (override di `%TEMP%`/`%TMP%` per i file temporanei)
- `-DistRoot <path>` (pulisce una cartella dist custom)
- `-KeepDist`
- `-KeepArtifacts`
- `-KeepTemp`
- `-KeepImportTemp` (mantiene `NtfsAudit\\imports`; se `-KeepTemp` non è impostato, vengono rimossi gli altri sotto-percorsi)
- `-KeepCache`
- `-ImportsOnly` (pulisce solo `%TEMP%\\NtfsAudit\\imports`)
- `-CacheOnly` (pulisce solo `%LOCALAPPDATA%\\NtfsAudit\\Cache`)
- `-CleanImports` (alias per `-ImportsOnly`)
- `-CleanCache` (alias per `-CacheOnly`)
- `-CleanAllTemp` (pulisce temp, import e cache)

## File temporanei

Durante la scansione vengono creati file in `%TEMP%\NtfsAudit`:

- `scan_*.jsonl`: righe ACL.
- `errors_*.jsonl`: errori di accesso.

Questi file sono riutilizzati per gli export e possono essere rimossi tramite `clean.ps1`.
Gli import di analisi usano `%TEMP%\NtfsAudit\\imports` e possono essere eliminati separatamente.

## Troubleshooting

- **Accesso negato**: gli errori vengono registrati, la scansione continua.
- **SACL/Audit vuote**: verifica che “Leggi Owner e SACL” sia attivo e che l’app sia eseguita come amministratore (richiede la privilege `SeSecurityPrivilege`).
- **TreeView vuota o senza marker**:
  - Se la mappa albero non è disponibile, la UI ricostruisce l’albero dai dettagli ACL o dai record di export; una scansione con molti errori o percorsi non accessibili può produrre pochi nodi.
  - Se il percorso root è una share/DFS profonda, l’import prova a limitare l’albero alle sottocartelle del root per evitare nodi “sopra” il root.
  - Verifica che il percorso root sia corretto e raggiungibile (UNC/DFS) e, se possibile, avvia l’app come amministratore per leggere Owner/SACL e ottenere più dettagli.
  - In ambienti con ACL particolarmente restrittive, abilita “Analizza tutte le sottocartelle” per massimizzare la copertura e riduci l’uso di filtri che escludono molte ACE.
- **Unità di rete non visibili nel dialog**: inserisci manualmente il percorso UNC (es. `\\server\share`) nel campo percorso o verifica la mappatura da Esplora file.
- **AD non disponibile**: disattiva “Usa PowerShell per AD” o installa RSAT.
- **Prestazioni**: riduci la profondità o disattiva risoluzione identità/espansione gruppi.
- **SACL sempre vuote**: verifica che “Leggi Owner e SACL” sia attivo e che l’app sia eseguita come amministratore. In caso di privilegi mancanti o accesso negato, la UI mostra “SACL non disponibile (privilegi/accesso negato)”; se non esistono regole di audit, viene indicato “Nessuna voce SACL”.
- **Compatibilità 2012 R2**: usa `net6.0-windows` per server legacy.
