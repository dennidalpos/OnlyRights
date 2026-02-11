# NTFS Audit

NTFS Audit è una suite WPF per analizzare permessi NTFS/SMB su percorsi locali, UNC e DFS. La soluzione è composta da:

- **NtfsAudit.App**: scansione, analisi interattiva, export Excel e archivio `.ntaudit`.
- **NtfsAudit.Viewer**: sola consultazione/import di analisi salvate.
- **NtfsAudit.App.Tests**: test unitari sui servizi di calcolo ACL.

---

## 1) Cosa fa l’applicazione

L’applicazione acquisisce ACL NTFS, opzionalmente integra layer Share SMB ed Effective Access, risolve SID/identità AD e produce:

- vista ad albero del percorso analizzato;
- tabelle ACL per gruppi/utenti/tutte le entry;
- pannello errori;
- riepilogo rischio (deny, everyone, authenticated users, baseline mismatch).

È pensata per attività operative di audit e hardening su file server Windows.

---

## 2) Funzioni principali

## Scansione

- Selezione cartella root locale/UNC.
- Profondità configurabile (`MaxDepth`) o scansione completa (`ScanAllDepths`).
- Inclusione ACL ereditate (`IncludeInherited`).
- Opzione scansione file (`IncludeFiles`) oltre alle cartelle.
- Lettura Owner e SACL (`ReadOwnerAndSacl`) se permessi/contesto lo consentono.

## Risoluzione identità

- Risoluzione SID con cache locale.
- Risoluzione AD con provider composito (PowerShell + Directory Services).
- Espansione gruppi annidati (`ExpandGroups`).
- Classificazione account di servizio/amministrativi.

## Audit avanzato

Con `EnableAdvancedAudit` attivo:

- acquisizione ACL Share (`IncludeSharePermissions`);
- calcolo Effective Access (`ComputeEffectiveAccess`);
- confronto baseline (`CompareBaseline`).

## Export e import

- **Export Excel (`.xlsx`)** con dataset ACL per analisi esterna.
- **Export archivio (`.ntaudit`)** con dati scansione completi.
- **Import archivio (`.ntaudit`)** in App o Viewer, con validazione struttura e recovery dei metadati principali.

---

## 3) Logica filtri e checkbox

## Filtri ACL (griglie)

Le checkbox in area filtri ACL applicano restrizioni immediate su tutte le griglie ACL:

- `Allow` / `Deny`: tipo ACE;
- `Ereditato` / `Esplicito`: provenienza della regola;
- `Protetto`: nodi con ereditarietà disabilitata;
- `Disabilitato`: ACE disattivate;
- `Everyone`, `Authenticated Users`, `Utenti di servizio`, `Admin`, `Altri`: segmentazione principale.

Il filtro testuale (`AclFilter`) ricerca su campi principali (principal, SID, rights, path, share, risk, source e membership).

## Filtri Tree

I filtri Tree sono **inclusivi**:

- `NTFS espliciti`: mostra solo nodi con permessi espliciti;
- `Protetto`: mostra solo nodi con inheritance disabilitata;
- `Diff vs padre`: mostra solo nodi con differenze ACL rispetto al padre;
- `Deny espliciti`: mostra solo nodi con deny espliciti;
- `Mismatch baseline`: mostra solo nodi con mismatch baseline;
- `Con file` / `Solo cartelle`: controllo visibilità per nodi con contenuto file o cartella.

`Reset filtri tree` riporta la configurazione predefinita neutra.

---

## 4) Requisiti

- Windows 10/11 o Server con supporto WPF.
- .NET SDK 8.x consigliato.
- Accesso ai percorsi target e, per audit avanzato completo, esecuzione elevata.

---

## 5) Build, test, publish

## Comandi dotnet

```powershell
dotnet restore NtfsAudit.sln
dotnet build NtfsAudit.sln -c Release
dotnet test NtfsAudit.sln -c Release
```

## Script build (`scripts/build.ps1`)

Flusso base:

```powershell
./scripts/build.ps1 -Configuration Release
```

Supporta restore/build/test/publish app + viewer e gestione clean integrata.

Opzioni più usate:

- `-Framework net8.0-windows`
- `-Runtime win-x64`
- `-SelfContained`
- `-PublishSingleFile`
- `-PublishReadyToRun`
- `-SkipRestore`, `-SkipBuild`, `-SkipTests`, `-SkipPublish`
- `-RunClean` con switch pulizia (`-CleanTemp`, `-CleanImports`, `-CleanCache`, `-CleanLogs`, `-CleanExports`, `-CleanDist`, `-CleanArtifacts`)

## Script clean (`scripts/clean.ps1`)

Pulizia completa predefinita:

```powershell
./scripts/clean.ps1
```

Rimuove binari/obj, `.vs`, dist/artifacts (in base ai flag), cache locale, temp import/export e log.

---

## 6) Flusso operativo consigliato

1. Selezionare root da analizzare.
2. Configurare opzioni scansione (profondità, identity resolution, audit avanzato).
3. Avviare scansione.
4. Applicare filtri tree/ACL per isolare criticità.
5. Esportare Excel per audit tabellare.
6. Salvare archivio `.ntaudit` per review successiva e condivisione con Viewer.

---

## 7) Struttura repository

- `src/NtfsAudit.App/`
  - `ViewModels/MainViewModel.cs`: orchestrazione UI, comandi, filtri, import/export.
  - `Services/`: scan, risoluzione identità, diff ACL, archivio analisi.
  - `Export/`: serializzazione record ACL + export Excel.
- `src/NtfsAudit.Viewer/`: shell viewer e bootstrap.
- `tests/NtfsAudit.App.Tests/`: test unitari dei servizi core.
- `scripts/`: automazione build/clean.
