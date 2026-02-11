# Analisi progetto NtfsAudit (base per comparazione)

## 1) Executive summary
- **Tipo progetto**: soluzione desktop Windows (WPF) per audit ACL NTFS/SMB su path locali e di rete, con import/export analisi persistenti.
- **Obiettivo principale**: analizzare permessi cartelle/file, normalizzare i diritti, calcolare viste aggregate (anche Effective Access) e supportare reporting/riuso tramite file Excel e archivio `.ntaudit`.
- **Target tecnologico**: .NET multi-target (`net6.0-windows`, `net8.0-windows`) con due eseguibili principali + progetto test.

## 2) Funzionalità principali (catalogo)

### 2.1 Scansione e audit
- Scansione di root locali, UNC e DFS (con risoluzione path dedicata) con lettura ACL in pipeline concorrente.
- Supporto opzionale scansione file oltre alle sole cartelle.
- Inclusione/esclusione ACE ereditate.
- Lettura owner + SACL (quando privilegi disponibili).
- Possibilità di confronto baseline (diff added/removed e mismatch su nodi).

### 2.2 Identity resolution e classificazione account
- Risoluzione SID → nome principal (opzionale).
- Espansione gruppi annidati (opzionale).
- Supporto resolver AD multiplo (DirectoryServices / PowerShell) con fallback.
- Filtri/esclusioni per account di servizio e account admin basati su SID noti.

### 2.3 Effective access e layer permessi
- Layer distinti per NTFS, Share e Effective Access.
- Possibilità di leggere ACL Share SMB e combinarle con NTFS (intersezione maschere) per un effective più realistico.
- Normalizzazione diritti in summary leggibili in UI/export.

### 2.4 UX di analisi
- TreeView con caricamento lazy e filtri dedicati ai nodi “toccati” sui permessi:
  - permessi espliciti,
  - ereditarietà disabilitata,
  - diff vs parent,
  - deny espliciti,
  - mismatch baseline,
  - nodi con file / solo cartelle,
  - reset filtri.
- Tab risultati separati: gruppi, utenti, ACL completo, share, effective, info permessi nodo, errori.
- Filtri ACL combinabili (principal, SID, allow/deny, rights, risk level, owner, resource type, ecc.).

### 2.5 Persistenza, import ed export
- Export Excel `.xlsx` (streaming, split su più sheet oltre limite righe Excel).
- Export analisi `.ntaudit` (ZIP con `data.jsonl`, `errors.jsonl`, `tree.json`, `meta.json`, `folderflags.json`).
- Import `.ntaudit` in App e Viewer con modalità sola lettura nel viewer.
- Ripristino opzioni di scansione da import e gestione compatibilità legacy.

### 2.6 Operatività e manutenzione
- Script PowerShell per build/publish/clean con opzioni granulari (temp, cache, artifacts, dist, logs, imports).
- Logging dedicato e gestione errori non bloccante durante scansione (raccolta errori per report).

## 3) Struttura progetto

## 3.1 Soluzione
- `NtfsAudit.sln`
  - `src/NtfsAudit.App` → app WPF principale (scan + export + import).
  - `src/NtfsAudit.Viewer` → viewer WPF sola lettura per `.ntaudit`.
  - `tests/NtfsAudit.App.Tests` → test unitari su servizi core (es. permission calculator / baseline).

### 3.2 Organizzazione codice in `NtfsAudit.App`
- `Models/`: DTO dominio (ACE, scan options, scan result/progress, diff baseline, errori, path kind).
- `Services/`: motore scansione + servizi infrastrutturali (path resolver, identity resolver, group expansion, share permissions, baseline comparer, archive import/export).
- `ViewModels/`: orchestrazione UI (MainViewModel + folder node VM + command).
- `Export/`: writer record/exporter Excel.
- `Cache/`: cache locali (SID/name e membership gruppi).
- `Logging/`: logging applicativo.
- `MainWindow.xaml` + code-behind: layout UI, tab risultati, pannelli filtri/opzioni.

## 4) Pipeline tecnica (alto livello)
1. Normalizzazione/classificazione path root.
2. Enumerazione albero (queue concorrente + worker multipli).
3. Lettura ACL (cartelle, opzionalmente file).
4. Arricchimento identità (resolve SID, eventuale group expansion).
5. Calcoli derivati (rights summary, effective, baseline diff, rischio).
6. Streaming dei record su JSONL temp.
7. Binding risultati su TreeView/DataGrid + filtri.
8. Export in Excel o archivio `.ntaudit`.

## 5) Stack e dipendenze
- **Framework**: .NET 6/8 Windows (WPF).
- **Librerie principali**:
  - `DocumentFormat.OpenXml` (Excel export),
  - `Newtonsoft.Json` (serializzazione),
  - `System.Management`,
  - `System.DirectoryServices.AccountManagement` (versionata per TFM).
- **Testing**: xUnit + Microsoft.NET.Test.Sdk.

## 6) Indicatori utili per comparazione con altro progetto

### 6.1 Feature matrix (checklist rapida)
- Copertura path: Local / UNC / DFS / NFS (questo progetto: local+UNC+DFS con NFS best-effort da verificare caso per caso).
- Layer permessi distinti: NTFS / Share / Effective.
- Baseline policy diff: sì.
- Filtri tree avanzati: sì.
- Import/export analisi versionato: sì (`.ntaudit`).
- Viewer read-only separato: sì.
- AD integration (resolver multipli + fallback): sì.

### 6.2 Non-functional da confrontare
- Prestazioni scansione su dataset grandi (nodi/sec, memoria, latenza UI).
- Robustezza su ambienti con permessi restrittivi (error handling + continuation).
- Portabilità ambiente (Windows-only vs cross-platform).
- Qualità test automatizzati (copertura test oggi focalizzata soprattutto su logica permessi).
- Scalabilità export/reporting (streaming, limiti Excel, tempi serializzazione).

### 6.3 Domande guida per confronto tecnico
- L’altro progetto separa scanner e viewer o ha un unico runtime?
- Come gestisce effective access (solo NTFS o intersezione con share)?
- Ha persistenza “sessione audit” riapribile e versionata?
- Offre filtri tree orientati a security triage (deny, mismatch baseline, inheritance disabled)?
- Quanto è estensibile su nuovi provider path/protocollo?

## 7) Punti di forza e limiti osservabili

### Punti di forza
- Pipeline ricca per audit ACL enterprise (identity, baseline, effective, error tracking).
- UX orientata ad analisi forense/operativa con tab e filtri mirati.
- Buona separazione moduli (Models/Services/ViewModels/Export/Cache).
- Import/export analisi robusto, utile per condivisione e revisione offline.

### Limiti / aree da verificare in comparazione
- Soluzione fortemente **Windows-centric** (WPF + ACL API Windows).
- Test automatici presenti ma non estesi a tutto il workflow UI/import/export end-to-end.
- NFS e scenari ibridi richiedono verifica pratica in base all’infrastruttura target.

## 8) Schema dati e artefatti da confrontare
- **Input runtime**: `ScanOptions` (profondità, identity resolve, advanced audit, share/effective, baseline, ecc.).
- **Output primari**:
  - `ScanResult` in memoria (tree + dettagli + errori),
  - JSONL temporanei (`scan_*.jsonl`, `errors_*.jsonl`),
  - Excel `.xlsx`,
  - archivio `.ntaudit` (zip strutturato).

## 9) Suggerimento pratico per comparazione con altro progetto
Per un confronto oggettivo, eseguire benchmark su stesso dataset (stesso root path, stessi privilegi account, stesse opzioni scan) e misurare:
1. tempo totale scansione,
2. numero nodi/file processati,
3. errori raccolti,
4. accuratezza effective/baseline,
5. tempo export `.xlsx` e dimensione output,
6. tempo import archivio storico.

Questo set permette una comparazione tecnica più affidabile rispetto al solo confronto “feature list”.
