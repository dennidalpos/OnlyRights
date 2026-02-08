# UI refactor proposal — NTFS Audit WPF

## Struttura UI proposta (layout)

**Layout a tre colonne (Split View)**

| Colonna | Sezioni | Obiettivo UX |
| --- | --- | --- |
| Sinistra | **Albero Cartelle** + **Legenda** | Navigazione rapida del filesystem con stato visivo immediato.
| Centro | **Risultati** + **Filtri persistenti** + **Sottoviste** | Lettura e analisi immediata dei dati senza cambiare contesto.
| Destra | **Stato Scansione** + **Scansione** + **Opzioni** | Controllo continuo su esecuzione e impostazioni.

**Motivazione**: riduce scroll verticale, mantiene sempre visibili le aree chiave (albero, risultati, stato). Favorisce il confronto visivo continuo tra cartella selezionata, risultati e progressi della scansione.

---

## Linee guida globali

### Tipografia e gerarchia
Scala tipografica coerente (valori suggeriti):
- **Titoli principali di sezione (H1)**: 16–18px, semibold
- **Sottosezioni (H2)**: 14–15px, semibold
- **Testo standard**: 12–13px, regular
- **Microcopy / hint**: 11–12px, italic o light

Applicazione:
- Ogni pannello contiene **titolo in alto** + divisore sottile.
- Le sezioni interne usano H2 con spaziatura consistente.

### Spaziature e allineamenti
- **Padding pannello**: 12–16px
- **Gap tra gruppi**: 8–12px
- **Righe dati**: altezza 28–32px per leggibilità
- **Allineamento**: griglie con label allineate a sinistra, input allineati a destra

### Coerenza cromatica
Palette semantica suggerita:
- **Info**: #2F80ED
- **Successo**: #27AE60
- **Warning**: #F2C94C
- **Errore**: #EB5757
- **Neutro**: #2D2D2D / #BDBDBD / #F5F5F5

Usi:
- Badge di stato, indicatori, righe con evidenze, legenda permessi.

---

## Riquadro “Albero Cartelle”

**Componenti suggeriti**
- TreeView con icone cartella (chiusa/aperta).
- Badge di stato per ogni nodo:
  - **Protetto** (shield)
  - **Deny** (rosso)
  - **Baseline** (blu)
  - **Share** (viola)
  - **NTFS** (grigio)

**Interazioni**
- **Espansione intelligente**: espansione iniziale max 2 livelli.
- **Bottoni evidenti**: “Espandi tutto” / “Comprimi tutto”.

**Legenda**
- Pannello collassabile (accordion) o tooltip esteso.
- Raggruppata per categoria:
  - **Tipo permesso**
  - **Stato**
  - **Sorgente**

---

## Riquadro “Risultati”

### Sottoviste (TabControl chiaro)
1. **Ricerca ACL / Gruppo / Utente**
2. **Vista cartella corrente** (riepilogo sintetico)
3. **Vista gruppi**
4. **Vista utenti**
5. **Vista ACL**
6. **Vista errori**

### Filtri persistenti
- Area filtri sempre visibile sopra ai risultati.
- Filtri raggruppati:
  - **Permessi** (Read/Write/Modify/Full/Deny)
  - **Identità** (Utente/Gruppo/Ruolo)
  - **Rischio** (High/Medium/Low)
- **Preset salvabili**:
  - “Solo Deny”
  - “High Risk”
  - “Solo modifiche”

### Color coding diritti
Legenda fissa in alto a destra:
- **Read**: blu
- **Write**: verde
- **Modify**: arancio
- **Full**: viola
- **Deny**: rosso

### Ordinamento e raggruppamento
- Sorting multi-colonna (clic con Ctrl).
- Raggruppamento per:
  - Principal
  - Rischio
  - Tipo permesso

---

## Riquadro “Stato Scansione”

**Badge di stato**
- **Idle** (grigio)
- **Running** (verde)
- **Stopped** (rosso)

**Visualizzazione cartella corrente**
- Evidenziata con background chiaro + icona “in scan”.

**Contatori**
- Successo: verde
- Warning: giallo
- Errore: rosso

**Comportamento**
- Refresh visivo discreto, no flicker.

---

## Riquadro “Scansione”

**Struttura per priorità**
1. **Selettore cartella + path** (in alto)
2. **DFS target** (se presente)
3. **Accordion opzioni base**:
   - profondità
   - include ereditati
4. **Sezione avanzata** (collassata di default):
   - audit avanzato
   - controlli extra

---

## Checkbox e Opzioni

**Raggruppamento**
- **Identità** (es. “Risolvi identità”)
- **Audit avanzato**
- **Esclusioni**

**Microcopy**
- Es. “Risolvi identità: aumenta accuratezza, impatta performance”.

---

## Toolbar e scalabilità futura

**Toolbar persistente**
- Start / Stop / Export / Import sempre visibili.

**Layout modulare**
- Struttura già compatibile con future viste:
  - Compliance
  - Audit comparativo

**Modalità Viewer / Admin**
- Controlli bloccati o ridotti in Viewer.
- Indicatored visivo modalità attiva.

---

## Linee guida cromatiche riassuntive

| Stato / Permesso | Colore |
| --- | --- |
| Info | #2F80ED |
| Successo | #27AE60 |
| Warning | #F2C94C |
| Errore | #EB5757 |
| Read | #2F80ED |
| Write | #27AE60 |
| Modify | #F2994A |
| Full | #9B51E0 |
| Deny | #EB5757 |

---

## Motivazioni chiave

- **Split View** riduce spostamenti e mantiene un contesto visivo costante.
- **Filtri persistenti** aumentano la produttività nelle analisi iterative.
- **Color coding unificato** accelera la lettura e riduce ambiguità.
- **Accordion e sezioni collassate** gestiscono la complessità senza overload.

