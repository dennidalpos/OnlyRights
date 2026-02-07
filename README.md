# NTFS Audit

Applicazione WPF per analizzare permessi NTFS su percorsi locali e UNC, con risoluzione identità, espansione gruppi e export dei risultati in Excel o archivio analisi.

## Panoramica
NTFS Audit esegue una scansione delle ACL delle cartelle partendo da una root. Durante la scansione:
- costruisce l’albero delle cartelle,
- legge le ACE (Allow/Deny) e i diritti normalizzati,
- risolve i SID in nomi (opzionale),
- espande i gruppi annidati (opzionale),
- salva i risultati in file temporanei riutilizzabili per export o import.

## Requisiti
- Windows 10/11 o Windows Server 2016+ (build `net8.0-windows`).
- Windows Server 2012 R2+ (build `net6.0-windows`).
- .NET SDK 6+ (multi-target `net6.0-windows;net8.0-windows`).
- Permessi di lettura ACL sulle cartelle analizzate.
- (Opzionale) RSAT / modulo ActiveDirectory se si usa la risoluzione AD via PowerShell.

## Avvio rapido
1. Compila o pubblica l’app (vedi [Script](#script)).
2. Avvia `NtfsAudit.App.exe`.
3. Seleziona un percorso locale (`C:\...`) o UNC (`\\server\share\...`).
4. Imposta la profondità o abilita “Analizza tutte le sottocartelle”.
5. Configura le opzioni (permessi ereditati, risoluzione identità, espansione gruppi).
6. Premi **Start**.
7. Al termine usa **Export** o **Export Analisi**.

## Funzionalità principali
- **Albero cartelle** con caricamento lazy.
- **Dettaglio ACL** separato per gruppi, utenti e dettaglio completo.
- **Filtro errori** integrato.
- **Barra di avanzamento** con metriche (processate/in coda).
- **Colorazione diritti** (Full, Modify, Read, ecc.).
- **Filtri account** per escludere account di servizio o admin.

## Opzioni di scansione
- **Analizza tutte le sottocartelle**: ignora MaxDepth e scansiona l’intero albero.
- **Includi permessi ereditati**: include ACE ereditate.
- **Risolvi identità (più lento)**: traduce SID → nome con fallback AD.
- **Espandi gruppi annidati**: calcola membri reali dei gruppi.
- **Usa PowerShell per AD**: preferisce AD via PowerShell se disponibile.
- **Escludi utenti di servizio**: filtra account con naming tipico di servizio.
- **Escludi utenti admin**: filtra account con naming tipico admin.
- **Colora per diritto**: evidenzia i permessi nella griglia con colori.

## Export Excel
L’export genera un file `.xlsx` con tre fogli:
- **Users**: tutte le ACE degli utenti.
- **Groups**: tutte le ACE dei gruppi (con colonna dei membri).
- **Acl**: l’elenco completo, inclusi utenti, gruppi e record meta.

Colonne principali:
- `FolderPath`, `PrincipalName`, `PrincipalSid`, `PrincipalType`
- `AllowDeny`, `RightsSummary`, `IsInherited`
- `InheritanceFlags`, `PropagationFlags`, `Source`, `Depth`
- `IsDisabled`, `IsServiceAccount`, `IsAdminAccount`, `GroupMembers`
- `IncludeInherited`, `ResolveIdentities`, `ExcludeServiceAccounts`, `ExcludeAdminAccounts`

Formato nome file:
```
<NomeCartellaRoot>_<dd-MM-yyyy-HH-mm>.xlsx
```

## Export/Import Analisi (.ntaudit)
L’export analisi salva un archivio `.ntaudit` con:
- dati ACL in formato JSONL,
- errori in JSONL,
- struttura albero,
- metadati (root e timestamp).

L’import ricarica i dati senza rieseguire la scansione, ricostruendo albero, ACL e filtri errori. Se l’archivio manca il file errori, l’import continua con un set vuoto.

## Script
### build.ps1
Compila e (opzionalmente) pubblica:
```
.\scripts\build.ps1 -Configuration Release
```
Parametri:
- `-Configuration Release|Debug`
- `-SkipRestore`
- `-SkipBuild`
- `-SkipPublish`
- `-Framework <tfm>` (es. `net8.0-windows`, default `net8.0-windows` in publish)
- `-OutputPath <cartella>`
- `-Runtime <rid>` (es. `win-x64`)
- `-SelfContained`
- `-PublishSingleFile`
- `-PublishReadyToRun`

Output di default: `dist\<Configuration>`.

### clean.ps1
Rimuove build, dist e cache:
```
.\scripts\clean.ps1
```
Parametri:
- `-Configuration <Release|Debug>` (rimuove solo `dist\<Configuration>`)
- `-KeepDist`
- `-KeepArtifacts`
- `-KeepTemp`
- `-KeepCache`

## File temporanei
La scansione usa file temporanei in `%TEMP%\NtfsAudit` per:
- risultati ACL (`scan_*.jsonl`),
- errori (`errors_*.jsonl`).

Questi file vengono riutilizzati per l’export e possono essere rimossi con `clean.ps1 -KeepTemp:$false`.

## Troubleshooting
- **Accesso negato su cartelle**: gli errori vengono registrati e la scansione continua.
- **AD non disponibile**: disattiva “Usa PowerShell per AD” o installa RSAT.
- **Prestazioni**: riduci MaxDepth o disattiva risoluzione identità/espansione gruppi.

## Nota compatibilità Windows Server 2012 R2
Per garantire la compatibilità con domini basati su Windows Server 2012 R2, usa la build `net6.0-windows`.
Il progetto è multi-target: `net6.0-windows` per ambienti legacy e `net8.0-windows` per i sistemi più recenti.
In fase di build/publish puoi selezionare il framework con `-Framework net6.0-windows` o `-Framework net8.0-windows`.
