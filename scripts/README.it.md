# Scripts

Questa cartella contiene solo entrypoint operativi principali nella root `scripts\` e script tecnici organizzati in sottocartelle.

## Script principali

| Script | Cosa fa | Quando usarlo | Admin |
|---|---|---|---|
| `install-dependencies.ps1` | Esegue `dotnet restore` sulla soluzione. | Primo setup locale o dopo modifiche alle dipendenze. | No |
| `build.ps1` | Compila la soluzione in `Release`. Supporta parametri `-Runtime` (win-x64), `-Configuration` (Release, Debug) e lo switch `-Installer` per generare l'installer NSIS 64-bit in una sola volta. | Build standard del repository e packaging installer. | No per la generazione, Sì in fase installazione |
| `package.ps1` | Esegue lo staging del layout di pacchetto (`App`, `Viewer`, `Service`) sotto `artifacts\packages` e genera gli installer NSIS 64-bit (`OnlyRights-NtfsAudit-1.0.0-x64.exe` e `OnlyRights-NtfsAudit-Viewer-1.0.0-x64.exe`). | Staging e compilazione completa degli installer distribuibili. | No per la generazione, Sì in fase installazione |
| `start-app.ps1` | Avvia `NtfsAudit.App`. | Esecuzione locale dell'app principale. | No |
| `prepare-network-share-app.ps1` | Prepara la cartella applicativa condivisibile sotto `artifacts\publish`. | Quando serve una cartella `app` distribuibile in rete. | No |
| `clean-repo.ps1` | Pulisce output build/package/publish e cartelle temporanee del repository. Supporta i parametri `-OperationalData`, `-ImportExportData` e `-ResetState` per cleanup mirati. | Prima di una build pulita o a fine verifica locale. | No |

## Ordine consigliato

1. `install-dependencies.ps1`
2. `maintenance\check-prerequisites.ps1`
3. `build.ps1` (opzionalmente con `-Runtime win-x64` e `-Installer`)
4. `start-app.ps1` per uso locale
5. `prepare-network-share-app.ps1` se serve la cartella condivisibile

## Sottocartelle

- `build\`: script tecnici per staging package e build installer NSIS.
- `maintenance\`: test, diagnostica, cleanup e test installer/service.
- `run\`: avvio componenti secondari non esposti nella root.
- `internal\`: funzioni e script di supporto condivisi; non sono entrypoint operativi.

## Note operative

- Gli installer NSIS prodotti dal repository sono configurati per architettura 64-bit (`ProgramFiles64Folder`), privilegio elevato e collegamenti desktop/menu Start.
- Lo script di service management `maintenance\manage-service.ps1` e lo script di installer test `maintenance\test-installer.ps1` richiedono un parametro `-Action` per specificare l'azione da eseguire.
- Gli script in `maintenance\` possono richiedere elevazione amministrativa in fase di esecuzione.
- La build dell'installer richiede il compilatore NSIS `makensis.exe`.
