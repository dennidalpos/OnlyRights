# Filtri, checkbox e processi Import/Export

Questo documento descrive il comportamento aggiornato di:

- filtri ACL (checkbox + testo libero),
- filtri TreeView,
- import/export `.ntaudit` e export Excel,
- script di build/clean usati nel ciclo operativo.

## 1) Logica checkbox ACL (griglie risultati)

I filtri ACL sono applicati in **AND** su tutte le collection view:

- `FilteredGroupEntries`
- `FilteredUserEntries`
- `FilteredAllEntries`
- `FilteredShareEntries`
- `FilteredEffectiveEntries`

Ogni modifica a checkbox o testo chiama refresh immediato.

### 1.1 Famiglia Allow / Deny

- `Allow` disattivo nasconde solo le ACE `Allow`.
- `Deny` disattivo nasconde solo le ACE `Deny`.
- Se `Allow` e `Deny` sono entrambi disattivi, il risultato è vuoto.

### 1.2 Famiglia Ereditato / Esplicito

- `Ereditati` disattivo nasconde ACE con `IsInherited=true`.
- `Espliciti` disattivo nasconde ACE con `IsInherited=false`.
- Se entrambi disattivi, il risultato è vuoto.

### 1.3 Stato protezione/disabilitazione

- `Protetto` disattivo nasconde ACE con `IsInheritanceDisabled=true`.
- `Disabilitato` disattivo nasconde ACE con `IsDisabled=true`.

### 1.4 Categorie principal

Categorie valutate:

- `Everyone`
- `Authenticated Users`
- `Service Accounts`
- `Admin Accounts`
- `Other Principals`

Se tutte disattive, il risultato è vuoto.

### 1.5 Filtro testuale libero

Il filtro testo confronta (contains, case-insensitive) su più colonne chiave: principal, SID, layer, rights, path, owner, rischio, source, membri espansi.

---

## 2) Logica checkbox TreeView

I filtri TreeView lavorano su `FolderDetail` e vengono combinati in **AND**.

### 2.1 Regole

I flag sono progettati come "mostra tutto" di default (`true`).
Quando un flag viene disattivato (`false`), la relativa categoria viene esclusa.

- `NTFS espliciti`: usa `HasExplicitPermissions`
- `Protetto`: usa `IsInheritanceDisabled`
- `Diff parent`: usa `DiffSummary`
- `Deny espliciti`: usa `DiffSummary.DenyExplicitCount`
- `Baseline mismatch`: usa `BaselineSummary`
- `Con file`: usa `ResourceType == File`
- `Solo cartelle`: usa `ResourceType == Cartella`

### 2.2 Mutual exclusion file/cartelle

Per evitare combinazioni senza output utile:

- se `Con file` viene portato a filtro attivo, `Solo cartelle` torna automaticamente neutro,
- se `Solo cartelle` viene portato a filtro attivo, `Con file` torna automaticamente neutro.

### 2.3 Reset filtri tree

`Reset filtri tree` riporta tutti i flag allo stato neutro e ricarica l’albero sulla root effettiva della scansione/import.

---

## 3) Processo Export Excel

- Verifica preliminare del file dati scansione (`TempDataPath`).
- Export in streaming OpenXML.
- Split automatico fogli Users/Groups oltre il limite Excel.
- Validazione output: file creato e non vuoto.
- Warning dedicato in caso di split; error popup in caso di eccezioni.

## 4) Processo Export Analisi (`.ntaudit`)

Pacchetto ZIP contenente:

- `data.jsonl`
- `errors.jsonl`
- `tree.json`
- `folderflags.json`
- `meta.json`

Comportamento:

- fallback tree map da dataset ACL se la mappa persistita manca,
- serializzazione opzioni scansione,
- validazione path output e creazione directory.

## 5) Processo Import Analisi (`.ntaudit`)

- blocco import se UI busy,
- verifica esistenza archivio prima dell’apertura,
- estrazione in temp import dedicata,
- normalizzazione `Details`/`TreeMap` se null,
- applicazione diff e validazione compatibilità,
- selezione root usando mappa completa con fallback legacy,
- **cleanup automatico della directory temporanea in caso di import fallito**.

---

## 6) Aggiornamento script build/clean

## 6.1 `scripts/build.ps1`

Migliorata la fase `-RunClean`:

- inoltro coerente di `Configuration/Framework/Runtime/TempRoot/DistRoot`,
- mapping esplicito flag `Keep*` per evitare clean involontari,
- supporto `-CleanExports` in pre-build.

## 6.2 `scripts/clean.ps1`

Aggiornamenti principali:

- helper centralizzati per rimozione sicura path,
- modalità `-CleanExports` dedicata alla sola pulizia file export (`*.xlsx`, `*.ntaudit`) in `dist/`, `artifacts/`, `exports/` e temp export,
- compatibilità con pulizie selettive (`ImportsOnly`, `CacheOnly`, `CleanAllTemp`, `CleanLogs`).
