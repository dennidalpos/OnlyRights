# Prompt per Codex — Audit permessi completo (local, rete, DFS, NFS) + performance + UX treeview

Sei Codex e devi implementare una **feature release end-to-end** nel progetto WPF `NtfsAudit.App`.

## Obiettivo
Colmare i gap dell’audit permessi per supportare in modo robusto:
1. Percorsi **locali**.
2. Percorsi di **rete SMB/UNC**.
3. Namespace **DFS** con fallback target.
4. Percorsi **NFS** (mount locali o share remote accessibili da Windows).

Inoltre:
- ottimizzare performance di scansione e caricamento UI,
- aggiungere filtri rapidi sulla **TreeView** per evidenziare cartelle/file “toccati” a livello permessi,
- aggiungere un nuovo tab con info generali dei permessi per il nodo selezionato.

## Contesto attuale (da preservare e estendere)
- Esiste già una scansione ACL multi-thread con `ScanService` e code concorrenti, con export JSONL/Excel.
- Esiste risoluzione DFS e normalizzazione percorso (`PathResolver`).
- UI già mostra badge sui nodi tree (espliciti/protetti/baseline/share) ma **mancano filtri tree strutturati**.
- Tab risultati presenti: Gruppi, Utenti, ACL, Share, Effective Access, Errori.
- Non esiste un tab dedicato “Info Generali Permessi” del nodo selezionato.
- NFS non è gestito esplicitamente con una pipeline dedicata/diagnostica specifica.

## Requisiti funzionali

### A) Audit multiprotocollo/località
1. Introdurre un livello di astrazione tipo `IPathAuditProvider` (o naming equivalente) con provider:
   - `NtfsLocalProvider`
   - `SmbUncProvider`
   - `DfsProvider`
   - `NfsProvider`
2. Il resolver deve classificare il path root e instradare al provider corretto.
3. Per DFS:
   - mantenere strategia target prioritario + fallback,
   - loggare target scelto e motivazione fallback.
4. Per NFS:
   - supportare almeno modalità best-effort: enumerazione, owner, ACL/permessi disponibili, error taxonomy chiara quando ACL non disponibili,
   - distinguere in output i permessi “nativi NTFS/SMB” da “NFS/POSIX-like” con campo `PermissionLayer`/`Source` coerente.
5. Aggiungere nel modello risultato un campo `PathKind` (`Local`, `UNC`, `DFS`, `NFS`, `Unknown`) e usarlo in UI/export.

### B) Performance
1. Ridurre allocazioni e lock contention in scansione:
   - batch di scrittura JSONL,
   - caching controllato su identity/group resolution,
   - evitare `ToList()` premature su hot path,
   - limitare chiamate ripetute a API costose (ACL, SID translation).
2. Aggiungere metriche runtime per benchmarking interno:
   - tempo enumerazione,
   - tempo ACL parsing,
   - tempo identity resolution,
   - throughput (nodi/sec),
   - memoria di picco (se disponibile).
3. UI responsiveness:
   - aggiornamenti progress throttled (es. 100–250 ms),
   - virtualizzazione verificata su TreeView/DataGrid,
   - caricamento dettagli nodo lazy.
4. Introdurre regressione performance guardrail con test/benchmark light (anche integration smoke).

### C) Filtri TreeView per “cartelle/file toccati sui permessi”
Aggiungere un pannello “Filtri Tree” con checkbox/toggle combinabili:
- Solo nodi con permessi espliciti.
- Solo nodi con ereditarietà disabilitata.
- Solo nodi con differenze vs parent (added/removed ACE).
- Solo nodi con `Deny` esplicito.
- Solo nodi con mismatch baseline.
- Solo file (se scansione file attiva) / solo cartelle.
- Filtro per livello rischio (High/Medium/Low).

Comportamento:
- i filtri si applicano **alla visibilità nodi tree** preservando gerarchia minima (mostra antenati necessari),
- badge esistenti restano e devono essere coerenti con i filtri,
- presenza di comando “Reset filtri tree”.

### D) Nuovo tab “Info generali permessi”
Aggiungere un TabItem (es. header: `Info Permessi`) per il nodo selezionato con:
- path completo + tipo path (`PathKind`),
- owner,
- stato ereditarietà,
- conteggio ACE totali / esplicite / ereditate / deny,
- layer presenti (NTFS, Share, Effective, NFS se applicabile),
- riepilogo rischio,
- ultimo timestamp scansione del nodo,
- eventuali warning di acquisizione (es. SACL non disponibile / ACL NFS non leggibile).

Vincoli UX:
- read-only,
- caricamento immediato al cambio selezione,
- placeholder elegante quando nessun nodo selezionato.

## Requisiti tecnici
1. Mantieni compatibilità con export `.ntaudit` legacy:
   - introdurre schema versioning incrementale,
   - fallback in import per campi mancanti.
2. Aggiornare serializzazione/deserializzazione (`ExportRecord`, `AnalysisArchive`) includendo nuovi campi.
3. Aggiornare filtri in ViewModel senza rompere filtri ACL esistenti.
4. Evitare breaking change pubbliche non necessarie.
5. Gestire eccezioni per protocollo con messaggi azionabili (non generici).

## File da toccare (indicativi)
- `src/NtfsAudit.App/Services/ScanService.cs`
- `src/NtfsAudit.App/Services/PathResolver.cs`
- `src/NtfsAudit.App/ViewModels/MainViewModel.cs`
- `src/NtfsAudit.App/ViewModels/FolderNodeViewModel.cs`
- `src/NtfsAudit.App/MainWindow.xaml`
- `src/NtfsAudit.App/Models/*` (scan/result metadata)
- `src/NtfsAudit.App/Services/AnalysisArchive.cs`
- `README.md` (nuove capability e limiti NFS)

## Piano implementativo richiesto
1. Refactor minimo del motore scansione su provider strategy.
2. Estensione modello dati + persistenza archive.
3. Introduzione filtri tree con stato in ViewModel e binding XAML.
4. Nuovo tab Info Permessi + DTO/ViewModel dedicato.
5. Ottimizzazioni performance e misurazioni.
6. Test e validazione.

## Test richiesti (obbligatori)
- Unit test:
  - classificazione path (`Local`/`UNC`/`DFS`/`NFS`),
  - filtro tree (logica include/exclude + antenati),
  - mapping summary info tab.
- Integration/smoke:
  - scansione root locale,
  - scansione UNC,
  - scansione DFS con fallback simulato,
  - scansione NFS best-effort (con expected warning se ambiente non supporta ACL complete).
- Performance check:
  - confronto baseline prima/dopo su dataset campione,
  - obiettivo: riduzione tempo scansione >=15% in scenario medium (o giustificazione tecnica).

## Criteri di accettazione
- L’app distingue chiaramente tipo percorso e layer permessi in UI/export.
- TreeView filtrabile per nodi “toccati” senza perdere navigabilità gerarchica.
- Nuovo tab Info Permessi completo e coerente col nodo selezionato.
- Nessuna regressione funzionale su import/export e filtri ACL esistenti.
- Build e test verdi; documentazione aggiornata con limiti noti NFS.

## Output atteso da te (Codex)
1. Patch completa sui file necessari.
2. Breve design note su scelte provider/performance.
3. Elenco test eseguiti con risultati.
4. Se alcune capability NFS dipendono dall’ambiente, implementa fallback esplicito + warning UX/documentazione.
