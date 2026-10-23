# Istruzioni del repository: OnlyRights

<!-- secondbrain:repository:begin -->
Direttive comuni applicate: revisione **3.0** — 2026-10-19.
<!-- secondbrain:source global=DC59C089490D9EE3E7E7F389FCC087D199EB12C16ADD2D20EA78076D9BB3BBDB workstation=3090606D3A18890581CDAD79BE8A65348612B40AAF40D5EAEB4E62AE0215D430 -->
Dossier SecondBrain di questo repository: 02-areas/products/OnlyRights, relativo alla radice di SecondBrain indicata nelle istruzioni globali.
- Leggi a inizio task: questo AGENTS.md, 02-areas/products/OnlyRights/context.md, 02-areas/products/OnlyRights/memory.md e PROJECT_STATUS.json (o 02-areas/products/OnlyRights/tracker.md).
- Nei task di scrittura aggiorna solo novità pertinenti: decisioni e gotcha in 02-areas/products/OnlyRights/memory.md; architettura e stato in 02-areas/products/OnlyRights/context.md; attività nel tracker preservandone schema e storico.
- Nei task di scrittura pulisci temporanei del task e residui confermati, preservando dati e lavoro unico; rivedi diff e git status --ignored. Esegui secondbrain.ps1 -Sync dopo modifiche a dossier o direttive, oppure su richiesta. Nei task di sola lettura segnala i residui senza rimuoverli.
<!-- secondbrain:repository:end -->

Revisione specifica del progetto 2.4 — 2026-10-18. Vincoli specifici del progetto; i contenuti comuni sono gestiti separatamente.

## Contesto

- **Scopo**: Suite NtfsAudit per Windows: applicazione WPF desktop, archivio read-only e Windows Service host per l'auditing dei permessi NTFS e condivisioni SMB su file server Windows.
- **Modalità**: Conservativa (integrità controlli di sicurezza e permessi NTFS)
- **Target Applicativo**: Desktop Windows WPF & Windows Service
- **Stack & Toolchain**: C# / .NET, WPF, Windows Security & Identity APIs, xUnit, pwsh

## Vincoli specifici

  * Operazioni di audit in sola lettura sui file server; gestione accurata dei permessi ereditati e delle ACL complesse; gestione memoria per alberi di directory estesi.

## Comandi e verifica

Comandi verificati staticamente contro i file del progetto; questa revisione delle direttive non certifica l'esecuzione di build o test applicativi. Consultare anche i gate della CI, quando presenti.

| Attività | Comando | Ambiente | Evidenza e limiti |
|---|---|---|---|
| **Full checks** | `dotnet test NtfsAudit.sln -c Release --nologo && dotnet build NtfsAudit.sln -c Release --nologo` | pwsh / root | **Gate obbligatorio pre-commit** (deve uscire con exit code 0) |

## Riferimenti e collaborazione

- Consultare README e documentazione effettivamente presenti; versioni dai manifest/lockfile, non da ipotesi sullo stack.
- 02-areas/products/OnlyRights/memory.md e 02-areas/products/OnlyRights/context.md (nel dossier SecondBrain) conservano decisioni tecniche durevoli, gotcha e architettura; aggiornarli a fine task.
- PROJECT_STATUS.json è il tracker esistente: contiene lavoro aperto, senza storico dei task conclusi; preservarne lo schema.
- Eseguire i controlli previsti per la modifica prima di un commit richiesto. Nessun commit o push automatico.
- Preservare dati persistenti e configurazioni esterne; non estendere la manutenzione a operazioni distruttive non autorizzate.
