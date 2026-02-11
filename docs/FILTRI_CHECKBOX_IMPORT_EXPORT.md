# Filtri, checkbox e processi Import/Export

## Logica checkbox e filtri (sintesi operativa)

### Filtri Tree (Legenda + filtri)
I filtri tree sono combinati in **AND**: un nodo è mostrato solo se rispetta tutti i filtri attivi.

- `NTFS espliciti (N)`: `detail.HasExplicitPermissions`
- `Protetto (P)`: `detail.IsInheritanceDisabled`
- `Diff parent (Δ)`: almeno una differenza in `DiffSummary.Added/Removed`
- `Deny espliciti (D)`: `DiffSummary.DenyExplicitCount > 0`
- `Baseline mismatch (B)`: differenze in `BaselineSummary`
- `Con file (F)`: almeno una entry `ResourceType == File`
- `Solo cartelle (C)`: almeno una entry `ResourceType == Cartella`

Comportamento reset:
- `Reset filtri tree` disattiva tutti i filtri.
- La root viene riallineata alla root effettiva della scansione/import (`ScanResult.RootPath`) per evitare lock su sotto-cartelle dopo import legacy.

### Filtri persistenti ACL (tab risultati)
I filtri persistenti (Allow/Deny, Ereditato/Esplicito, identità, testo libero) vengono applicati alle collection view (`FilteredGroupEntries`, `FilteredUserEntries`, `FilteredAllEntries`, `FilteredShareEntries`, `FilteredEffectiveEntries`) con refresh immediato al cambio di checkbox/testo.

## Processo Import/Export aggiornato

### Export Excel
- Verifica preliminare della presenza del file temporaneo dati scansione (`TempDataPath`).
- Se assente: warning bloccante con messaggio esplicito.
- Su successo: nessun popup "completato" (UX silenziosa).
- Su warning tecnici (es. split fogli): popup warning dedicato.
- Su errore: popup errore + aggiornamento `ProgressText`.

### Export Analisi (`.ntaudit`)
- Processo centralizzato con gestione errori uniforme.
- Nessun popup di successo.
- Su errore: popup errore + `ProgressText`.

### Import Analisi
- Blocco quando l'app è già busy.
- Normalizzazione strutture importate: `Details` e `TreeMap` inizializzate se `null`.
- Ricostruzione/risoluzione albero tramite `ResolveFullTreeMap` con fallback compatibile legacy.
- Root selezionata tramite `ResolveTreeRoot` su mappa completa, non solo su `TreeMap` persistita.

## Esclusioni DFS/DFSR in scansione
Per ridurre rumore e tempi, il servizio esclude cartelle tecniche DFS/replica:
- `System Volume Information`
- `DfsrPrivate`
- `DfsPrivate`
- `DFSR` sotto `System Volume Information`
- sotto-cartelle tipiche replica (`ConflictAndDeleted`, `Deleted`, `PreExisting`, `Staging`, `Staging Areas`) quando annidate in contesti DFSR.

## Note operative
- Se un import è molto vecchio e con metadata incompleti, la risoluzione full tree usa i dettagli ACL o i record export come fallback.
- In presenza di filtri molto restrittivi, tree vuota è un esito possibile e corretto.
