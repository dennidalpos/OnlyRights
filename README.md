# NTFS Audit (WPF, .NET 8)

NTFS Audit è un’applicazione WPF per l’analisi dei permessi NTFS su percorsi locali e UNC, con supporto per la risoluzione di gruppi/utenti e l’esportazione dei risultati in Excel.

## Requisiti
- Windows Server 2012/2012 R2/2016+ o Windows 10/11.
- .NET 8 SDK (target `net8.0-windows`).
- Account con permessi amministrativi sulle cartelle da analizzare.
- (Opzionale) RSAT/modulo ActiveDirectory per la risoluzione AD via PowerShell.

## Avvio rapido
1. Compila o pubblica l’app con gli script PowerShell.
2. Avvia `NtfsAudit.App.exe`.
3. Inserisci un percorso locale (`C:\...`) o UNC (`\\server\share\...`).
4. Imposta `MaxDepth` oppure abilita **Analizza tutte le sottocartelle** per una scansione completa.
5. Se necessario, regola le opzioni di performance/completezza (permessi ereditati, risoluzione identità, espansione gruppi).
6. Premi **Start**.
7. Al termine, usa **Export** (o **Export al termine**).

## Funzionalità
- **Albero cartelle** con caricamento lazy.
- **Dettagli ACL** per cartella selezionata (gruppi, utenti, dettaglio completo, errori).
- **Barra di avanzamento deterministica** che avanza in base alle cartelle processate/in coda.
- **Colorazione per diritto** attiva di default.
- **Colonna Depth** per capire la profondità della cartella rispetto alla root.
- **Stato disabilitato** per utenti/computer (se risolto via AD), con riga barrata in UI.
- **Opzioni di scansione** per bilanciare velocità e completezza.

## Risoluzione AD e gruppi annidati
- Se disponibile, l’app usa PowerShell per risolvere utenti/gruppi e membri annidati.
- In assenza del modulo AD, usa il fallback DirectoryServices.
- L’identificazione “disabilitato” viene letta da AD (utenti e computer) quando la risoluzione è attiva.

## Opzioni di scansione
- **Analizza tutte le sottocartelle**: ignora `MaxDepth` e analizza l’intero albero.
- **Includi permessi ereditati**: se disattivato, include solo ACE non ereditate (più rapido, meno completo).
- **Risolvi identità (più lento)**: se disattivato, evita risoluzione SID→nome e riduce l’accesso ad AD.
- **Espandi gruppi annidati**: disponibile solo quando la risoluzione identità è attiva.
- **Usa PowerShell per AD**: abilita la risoluzione AD via PowerShell (se RSAT presente).

## Export Excel
L’export genera un file Excel con tre fogli:
- `FolderPermissions` (tutte le ACE, inclusi membri dei gruppi espansi)
- `SummaryByPrincipal` (conteggio cartelle e diritto massimo)
- `Errors` (path, tipo, messaggio)

Nel foglio `FolderPermissions` sono presenti anche:
- **Disabilitato** (true/false)
- **IncludeInherited** (true/false)
- **ResolveIdentities** (true/false)

Formato nome file:
```
<NomeCartellaScansionata>_<dd-MM-yyyy-HH-mm>.xlsx
```

## Logging e file temporanei
- Il log applicativo è usato solo per diagnosticare crash non gestiti.
- I file temporanei della scansione vengono rimossi alla chiusura dell’app.

## Script
### build.ps1
```
.\scripts\build.ps1 -Configuration Release
```
Parametri principali:
- `-Configuration Release|Debug`
- `-SkipRestore`
- `-SkipBuild`
- `-SkipPublish`
- `-OutputPath <cartella>`
- `-Runtime <rid>`
- `-SelfContained`
- `-PublishSingleFile`
- `-PublishReadyToRun`

Output di default: `dist\<Configuration>`.

### clean.ps1
```
.\scripts\clean.ps1
```
Parametri principali:
- `-KeepDist`
- `-KeepTemp`
- `-KeepCache`

## Troubleshooting
- **Cartelle inaccessibili**: l’app registra l’errore e continua.
- **AD non disponibile**: disattiva “Usa PowerShell per AD” o installa RSAT.
- **Prestazioni**: riduci `MaxDepth`, disattiva “Risolvi identità” o “Espandi gruppi annidati”.
