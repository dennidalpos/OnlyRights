# NTFS Audit

NTFS Audit è una suite Windows (WPF + Worker Service) per analizzare ACL NTFS/SMB su percorsi locali, UNC e DFS, esportare risultati in Excel e archivi `.ntaudit`, e (opzionalmente) far girare scansioni tramite servizio Windows anche dopo logout utente.

## Componenti della soluzione

- **NtfsAudit.App** (`src/NtfsAudit.App`): applicazione principale con scansione, filtri, export/import e gestione servizio.
- **NtfsAudit.Viewer** (`src/NtfsAudit.Viewer`): viewer leggero per aprire analisi `.ntaudit` in sola consultazione.
- **NtfsAudit.Service** (`src/NtfsAudit.Service`): worker eseguibile come Windows Service per processare job di scansione in background.
- **NtfsAudit.App.Tests** (`tests/NtfsAudit.App.Tests`): test unitari servizi core.

---

## Funzionalità principali

### 1) Scansione ACL

- scansione cartelle locali/UNC/DFS;
- profondità limitata (`MaxDepth`) o completa (`ScanAllDepths`);
- inclusione ACL ereditate (`IncludeInherited`);
- scansione file opzionale (`IncludeFiles`);
- lettura owner/SACL opzionale (`ReadOwnerAndSacl`) con privilegi adeguati.

### 2) Identità e Active Directory

- risoluzione SID -> nome con cache locale;
- risoluzione AD con provider Directory Services / PowerShell;
- espansione gruppi annidati (`ExpandGroups`);
- classificazione account di servizio/admin per filtri rapidi.

### 3) Audit avanzato

Con `EnableAdvancedAudit` attivo:

- acquisizione permessi share SMB (`IncludeSharePermissions`);
- calcolo effective access (`ComputeEffectiveAccess`);
- confronto baseline (`CompareBaseline`).

### 4) Export / Import

- export Excel (`.xlsx`);
- export analisi completa (`.ntaudit`);
- import `.ntaudit` in App e Viewer.

### 5) Multi-root + DFS target per voce

Nella UI puoi:

- aggiungere più cartelle in elenco scansione;
- selezionare una cartella in elenco e scegliere il **target DFS specifico** per quella voce;
- usare un path unico di output `.ntaudit`, con un archivio separato per ogni root elaborata.

### 6) Modalità servizio Windows

- pulsanti UI per installare/disinstallare servizio;
- coda job in `%ProgramData%\NtfsAudit\jobs`;
- il service processa i job e salva i `.ntaudit` nell’output configurato;
- scansioni continuano senza sessione utente attiva (scenario logout/RDP disconnected).

---

## Requisiti

- Windows 10/11 o Windows Server con WPF;
- .NET SDK 8.x (consigliato) per build/publish;
- privilegi adeguati ai percorsi target;
- per installare servizio: shell elevata (Run as Administrator).

---

## Build, test e publish

## Comandi dotnet essenziali

```powershell
dotnet restore NtfsAudit.sln
dotnet build NtfsAudit.sln -c Release
dotnet test NtfsAudit.sln -c Release
```

## Script build (`scripts/build.ps1`)

Esecuzione tipica:

```powershell
./scripts/build.ps1 -Configuration Release
```

Cosa fa (default):

1. restore soluzione;
2. build soluzione;
3. test soluzione;
4. publish App;
5. publish Viewer;
6. publish Service.

Output publish default: `dist/<Configuration>/...`

### Opzioni principali build

- `-Framework net8.0-windows`
- `-Runtime win-x64`
- `-SelfContained`
- `-PublishSingleFile`
- `-PublishReadyToRun`
- `-SkipRestore`, `-SkipBuild`, `-SkipTests`, `-SkipPublish`
- `-SkipViewerPublish`
- `-SkipServicePublish`
- `-RunClean` (invoca `clean.ps1` prima della build)

Esempio publish completo self-contained:

```powershell
./scripts/build.ps1 -Configuration Release -Framework net8.0-windows -Runtime win-x64 -SelfContained -PublishSingleFile -PublishReadyToRun
```

---

## Clean e housekeeping

## Script clean (`scripts/clean.ps1`)

Pulizia completa standard:

```powershell
./scripts/clean.ps1
```

Pulisce:

- `.vs`
- `bin/obj` di:
  - `NtfsAudit.App`
  - `NtfsAudit.Viewer`
  - `NtfsAudit.Service`
  - `NtfsAudit.App.Tests`
- `dist` / `artifacts` (in base ai flag)
- cache locale / temp import / log / export temporanei (opzionali via flag)

Flag utili:

- `-CleanAllTemp`
- `-CleanImports`
- `-CleanCache`
- `-CleanLogs`
- `-CleanExports`
- `-KeepDist`
- `-KeepArtifacts`

---

## Deploy servizio Windows

### 1) Build/publish

Pubblica anche il service con `scripts/build.ps1` (default, salvo `-SkipServicePublish`).

### 2) Installazione da UI

L’app cerca automaticamente:

- `NtfsAudit.Service.exe`
- `NtfsAudit.Service.dll`

in cartella applicazione e path build/publish comuni del repository.

Se trova solo `.dll`, usa comando `dotnet "...\NtfsAudit.Service.dll"` come `binPath` del servizio.

### 3) Esecuzione job

Con “Esegui tramite servizio Windows” attivo, la UI serializza job in `%ProgramData%\NtfsAudit\jobs`.
Il service legge i job, esegue la scansione e produce un `.ntaudit` per ciascuna root.

---

## Struttura repository

- `src/NtfsAudit.App/`
  - `ViewModels/MainViewModel.cs`: orchestrazione UI, comandi scansione, gestione servizio, filtri.
  - `Services/`: scan engine, resolver AD, archive import/export, baseline diff.
  - `Models/`: DTO ACL, opzioni scansione, job service.
- `src/NtfsAudit.Viewer/`: viewer `.ntaudit`.
- `src/NtfsAudit.Service/`: worker service per scansioni background.
- `tests/NtfsAudit.App.Tests/`: test unitari.
- `scripts/`: automazione build/clean.
