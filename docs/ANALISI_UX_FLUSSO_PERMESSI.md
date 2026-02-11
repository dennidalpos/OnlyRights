# Analisi UX e flusso permessi (Admin + Viewer)

## Obiettivo
Rendere l'interfaccia più comprensibile per utenti non tecnici e verificare la logica di confronto permessi lungo tutto il workflow (scansione, confronto, visualizzazione, export/import).

---

## 1) Migliorie UX concrete (best practice Microsoft)

## 1.1 Linguaggio: da tecnico a orientato al compito
Principio Microsoft: **Plain Language + Progressive Disclosure** (prima il "cosa", poi il dettaglio tecnico su richiesta).

Proposte operative:
- Rinominare azioni principali:
  - `Start` → **Avvia analisi**
  - `Export Analisi` → **Salva report completo (.ntaudit)**
  - `Export Excel` → **Esporta elenco permessi (Excel)**
- Raggruppare opzioni avanzate in una sezione collassabile con etichetta:
  - **Opzioni avanzate (per amministratori)**
- Per ogni opzione tecnica, aggiungere un sottotesto breve (microcopy) con impatto:
  - esempio: “Più dettagli, ma scansione più lenta”.

## 1.2 Icone: set coerente e semantico
Principio Microsoft: icone consistenti, non decorative, supportate da testo.

Proposte:
- Usare set **Fluent/Segoe MDL2** omogeneo (evitare mix eterogeneo di lettere badge + emoji).
- Standardizzare significati:
  - Rischio alto: `Warning`
  - Ereditarietà disabilitata: `Lock`
  - Deny: `Blocked`
  - Baseline mismatch: `Compare`
- Mantenere sempre etichetta testuale accanto all’icona (accessibilità e chiarezza).

## 1.3 Tooltip: brevi, orientati all’azione
Principio Microsoft: tooltip come supporto contestuale, non mini-documentazione.

Template consigliato:
- Riga 1: **Cosa mostra/fà**.
- Riga 2: **Quando usarlo**.
- Riga 3 (facoltativa): **Impatto performance/accuratezza**.

Esempio:
- “Confronta con baseline: evidenzia differenze rispetto alla cartella root. Usalo per verificare deviazioni dalla policy standard.”

## 1.4 Percorso guidato per utenti non tecnici
Proposta di flusso in 3 step (wizard leggero in pagina):
1. **Scegli cartella**
2. **Avvia analisi**
3. **Leggi risultato** (scheda “Cosa significa” con legenda semplice)

In più:
- CTA primaria unica: **Avvia analisi**.
- CTA secondarie meno evidenti: export, opzioni avanzate.

## 1.5 Differenziare meglio i 2 eseguibili
- **Admin App**: mantenere scansione + opzioni avanzate.
- **Viewer utenti reparto**:
  - nascondere completamente controlli di scansione/export tecnico,
  - mostrare solo: Apri report, Cerca reparto/utente, Filtri semplici (“Chi può leggere/scrivere?”), glossario.

## 1.6 Semplificazione colonne (tutte le tab)
Obiettivo: ridurre il carico cognitivo e rendere i confronti più rapidi per utenti non tecnici.

Criteri strutturali:
- Ridurre le colonne ad un **core set** comune e stabile tra tab:
  - Risorsa (nome/path breve),
  - Chi (utente/gruppo),
  - Accesso (Read/Write/Modify/Full),
  - Stato (Allow/Deny, Ereditato/Esplicito),
  - Rischio.
- Spostare i dettagli tecnici (SID, flags avanzati, source raw) in:
  - pannello dettagli laterale, oppure
  - tooltip “Dettaglio tecnico”.
- Eliminare ridondanze tra campi semanticamente sovrapposti (es. stati ripetuti su più colonne con lo stesso significato).

Linee guida per tipo colonna:
- Dove il valore è binario, usare **checkbox** invece di testo:
  - `IsInherited`, `IsDisabled`, `IsServiceAccount`, `IsAdminAccount`,
  - `HasExplicitPermissions`, `IsInheritanceDisabled`,
  - `AppliesToThisFolder`, `AppliesToSubfolders`, `AppliesToFiles`.
- Mantenere testuale solo ciò che richiede lettura semantica (es. `RightsSummary`, owner, warning acquisizione).

Uniformità richiesta:
- Stesso ordine logico colonne in tutte le tab (Gruppi, Utenti, ACL, Share, Effective).
- Stesso pattern visuale per colonne “stato” (checkbox allineate, header coerente).

## 1.7 Layout risultati: filtri e legenda diritti
Requisiti richiesti per ridurre rumore visivo e migliorare orientamento:

- **Filtri persistenti sempre collassati** all’apertura (stato UI conservato ma pannello non espanso di default).
- **Legenda diritti sempre visibile** nell’area risultati (non dentro expander collassabile).
- La voce **"cartella trattata"** va spostata **sotto la legenda diritti** per mantenere priorità informativa coerente (prima chiave di lettura, poi contesto cartella).
- Evitare duplicazioni dello stesso contenuto (se la cartella è mostrata in header globale, mantenerla in versione sintetica nella sezione risultati).

---

## 2) Criticità logiche nel workflow permessi

## 2.1 Effective Access: fallback che può sovrastimare i diritti
Nel calcolo corrente, se `ComputeEffectiveAccess` è attivo e la maschera effettiva risulta `0` su una ACE `Allow`, viene forzata ai diritti della regola originale.

Rischio:
- possibile sovrastima dell’accesso reale in presenza di deny/intersezione con share ACL.

Best practice:
- non fare fallback automatico a `rule.FileSystemRights` se il risultato effettivo è `0`.
- distinguere in UI:
  - **Effective calcolato**
  - **Effective non determinabile con precisione** (badge “Stima”).

## 2.2 Baseline globale su root: rischio falsi positivi sui livelli profondi
La baseline viene derivata dalle ACL della root e confrontata su tutti i nodi.

Rischio:
- cartelle figlie con deleghe legittime possono risultare “mismatch” anche se corrette per design.

Best practice:
- introdurre baseline per livello/ramo (policy profile), oppure
- confronto “vs parent + eccezioni consentite”, con whitelist di deviazioni attese.

## 2.3 Confronto set-based: perdita del peso di ACE duplicate
Il confronto usa `HashSet<AclDiffKey>`: ACE duplicate equivalenti vengono collassate.

Rischio:
- differenze quantitative (stessa ACE ripetuta) possono non emergere.

Best practice:
- usare confronto multiset (chiave + conteggio occorrenze).

## 2.4 Classificazione NFS euristica potenzialmente ambigua
`PathResolver` classifica NFS con euristiche string-based (es. pattern path).

Rischio:
- misclassificazione di percorsi speciali/ibridi.

Best practice:
- aggiungere stato confidenza (`Certain/Heuristic`) e renderlo visibile in UI,
- consentire override manuale in fase di scansione.

## 2.5 Filtri Tree "solo file/cartelle": assenza feedback quando risultato vuoto
I filtri possono produrre albero vuoto senza spiegazione esplicita.

Rischio:
- utente non tecnico interpreta “nessun permesso” invece di “filtro troppo restrittivo”.

Best practice:
- banner contestuale: “Nessun nodo soddisfa i filtri correnti. Reimposta filtri”.
- pulsante inline “Reset filtri”.

## 2.6 Legenda e filtri TreeView separati: rischio incoerenza visiva/funzionale
Attualmente legenda e filtri sono presentati in aree con possibile sovrapposizione concettuale.

Rischio:
- l’utente vede due sistemi “simili ma diversi” e fatica a capire cosa filtra davvero.

Best practice:
- unificare legenda + filtri in un **unico componente** “Stati Tree”.
- usare un set unico di icone/stati (esempio: Esplicito, Protetto, Deny, Baseline mismatch, File, Cartella, Rischio H/M/L).
- associare ad ogni stato una checkbox nello stesso elenco.

Comportamento checkbox (obbligatorio):
- tutte selezionate di default;
- deselezione = rimozione immediata dalla UI degli elementi con quello stato;
- aggiornamento live senza pulsante “Applica”;
- stato filtri persistito per utente/sessione, ma pannello filtri reso sempre collassato all’apertura.

Regole anti-ridondanza:
- rimuovere duplicazioni funzionali (stesso filtro esposto in sezioni diverse);
- rimuovere duplicazioni visive (stessa icona/concetto con label diverse);
- mantenere un solo punto di reset (“Reimposta stati Tree”).

---

## 3) Piano di miglioramento pragmatico (30/60/90 giorni)

## 30 giorni (quick wins)
- Revisione copy di pulsanti/etichette/tooltip in linguaggio naturale.
- Introduzione glossario in-app (termine tecnico → spiegazione semplice).
- Messaggio esplicito quando filtri tree azzerano i risultati.
- Differenziazione visuale netta modalità Admin vs Viewer.
- Razionalizzazione colonne su tutte le tab con conversione stati binari a checkbox.
- Componente unico “Stati Tree” (legenda + filtri) con checkbox tutte ON di default.

## 60 giorni
- Refactor calcolo Effective Access con stato di affidabilità (Calcolato/Stima/Non determinabile).
- Baseline “a profili” (root, reparto, eccezioni note).
- KPI di comprensione UX (tempo medio per trovare “chi ha Modify/Full”).
- Introduzione preset di vista colonne (Base / Avanzata) per Admin; preset unico semplificato per Viewer.
- Telemetria UX locale su uso dei filtri Tree (quali stati vengono disattivati più spesso).

## 90 giorni
- Wizard per utenza business.
- Libreria icone Fluent unificata + accessibilità WCAG AA (contrasto/focus/keyboard).
- Telemetria locale anonima su errori di interpretazione (es. filtri contraddittori).

---

## 4) Proposta terminologia semplificata (pronta da implementare)

| Termine attuale | Proposta semplice | Tooltip consigliato |
|---|---|---|
| ACL | Regole di accesso | Elenco di chi può fare cosa nella cartella |
| Deny | Accesso negato | Blocca l’accesso anche se altre regole lo concedono |
| Inherited | Ereditato | Regola presa dalla cartella superiore |
| Effective Access | Accesso effettivo | Permesso finale dopo aver combinato tutte le regole |
| Baseline mismatch | Differenza dalla policy standard | Questa cartella non segue le regole previste |
| SID | ID account | Codice tecnico univoco dell’utente o gruppo |

---

## 5) Checklist qualità (allineata a best practice Microsoft)
- Etichette azionabili (verbo + risultato).
- Un solo obiettivo principale per schermata.
- Errori spiegati in linguaggio naturale + azione suggerita.
- Tooltip brevi e coerenti.
- Contrasto colori + navigazione tastiera.
- Coerenza tra App Admin e Viewer (stesso lessico, complessità diversa).
- Colonne uniformi tra tab, senza ridondanze.
- Stati binari resi con checkbox e non con testo ambiguo.
- Componente Tree unico per legenda+filtri, con stati ON di default e filtro immediato in deselezione.
- Filtri persistenti ma pannello sempre collassato; legenda diritti sempre visibile.
- Posizione “cartella trattata” sotto la legenda diritti nella sezione risultati.

---

## 6) Piano implementativo dettagliato richiesto (strutturale)

### 6.1 Razionalizzazione colonne (tutte le viste)
1. Inventario colonne per tab (Utenti, Gruppi, ACL, Share, Effective, Errori).
2. Mappatura colonne in 3 classi:
   - Essenziali (sempre visibili),
   - Contestuali (visibili in preset avanzato),
   - Ridondanti (da rimuovere).
3. Conversione campi binari a checkbox in DataGridTemplateColumn.
4. Uniformazione ordine colonne e naming header.
5. Validazione con utenti non tecnici: task “trova chi ha Modify su cartella X”.

### 6.2 Unificazione legenda + filtri TreeView
1. Creare ViewModel unico `TreeStateFilterItem`:
   - `Key`, `Label`, `Icon`, `IsChecked`, `Description`.
2. Popolare collezione unica stati Tree da usare sia per legenda sia per filtro.
3. Rendere il pannello filtri persistente ma sempre collassato in apertura.
4. Mantenere la legenda diritti sempre visibile (non collassabile) nella stessa area risultati.
5. Spostare il campo “cartella trattata” sotto la legenda diritti.
6. Binding checkbox con filtro immediato (PropertyChanged → refresh tree).
7. Default iniziale: tutti gli stati `IsChecked = true`.
8. Aggiungere comando singolo di reset (riattiva tutto).

### 6.3 Regole comportamento checkbox
- Nessuna checkbox deselezionata al primo caricamento.
- Deselezione stato = esclusione immediata elementi UI con quello stato.
- Stato filtri persistito in sessione (raccomandato per Admin e Viewer).
- UI filtri sempre collassata al load, indipendentemente dalla persistenza dello stato.
- In caso risultato vuoto: banner guida + CTA reset.

### 6.4 Eliminazione ripetizioni
- Rimuovere controlli duplicati tra toolbar, expander e pannelli laterali.
- Un solo lessico per stesso concetto (es. “Protetto” ovunque, non alias multipli).
- Un solo set icone ufficiale documentato in tabella di design system interno.
