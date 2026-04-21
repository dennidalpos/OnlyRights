# Scripts

Questa cartella contiene solo entrypoint operativi principali nella root `scripts\` e script tecnici organizzati in sottocartelle.

## Script principali

| Script | Cosa fa | Quando usarlo | Admin |
|---|---|---|---|
| `install-dependencies.ps1` | Esegue `dotnet restore` sulla soluzione. | Primo setup locale o dopo modifiche alle dipendenze. | No |
| `build.ps1` | Compila la soluzione in `Release`. | Build standard del repository. | No |
| `build-x64.ps1` | Compila la soluzione per `win-x64`. | Prima di packaging o installer x64. | No |
| `build-x86.ps1` | Compila la soluzione per `win-x86`. | Prima di packaging o installer x86. | No |
| `start-app.ps1` | Avvia `NtfsAudit.App`. | Esecuzione locale dell'app principale. | No |
| `prepare-network-share-app.ps1` | Prepara la cartella applicativa condivisibile sotto `artifacts\publish`. | Quando serve una cartella `app` distribuibile in rete. | No |
| `generate-installer-x64.ps1` | Genera l'MSI x64 con installazione per-machine e privilegi elevati. | Packaging installer x64. | No per la generazione, Sì in fase installazione MSI |
| `generate-installer-x86.ps1` | Genera l'MSI x86 con installazione per-machine e privilegi elevati. | Packaging installer x86. | No per la generazione, Sì in fase installazione MSI |
| `clean-repo.ps1` | Pulisce output build/package/publish e cartelle temporanee del repository. | Prima di una build pulita o a fine verifica locale. | No |

## Ordine consigliato

1. `install-dependencies.ps1`
2. `maintenance\check-prerequisites.ps1`
3. `build.ps1` oppure `build-x64.ps1` / `build-x86.ps1`
4. `start-app.ps1` per uso locale
5. `prepare-network-share-app.ps1` se serve la cartella condivisibile
6. `generate-installer-x64.ps1` o `generate-installer-x86.ps1` se serve l'installer

## Sottocartelle

- `build\`: script tecnici per staging package e build MSI.
- `maintenance\`: test, diagnostica, cleanup specializzati, script Windows Service e smoke MSI.
- `run\`: avvio componenti secondari non esposti nella root.
- `internal\`: funzioni e script di supporto condivisi; non sono entrypoint operativi.
- `legacy\`: script mantenuti solo per compatibilità temporanea.

## Note operative

- Gli installer MSI prodotti dal repository sono già configurati per installazione `perMachine`, privilegio elevato e collegamento desktop.
- Gli script di service management in `maintenance\` possono richiedere elevazione amministrativa in fase di esecuzione.
- Gli script MSI di smoke test in `maintenance\` richiedono `msiexec.exe`; la build MSI richiede WiX sotto `tools\wix314-binaries`.
