# Setup Iniziale e Configurazione

Questa guida descrive il percorso piu semplice per preparare `OnlyRights` e iniziare una prima analisi con `NtfsAudit.App`.

## A chi serve

Usa questa procedura se:
- stai aprendo il repository per la prima volta;
- vuoi verificare rapidamente che l'app parta e compili;
- vuoi configurare una prima scansione senza entrare nei dettagli di servizio Windows o MSI.

## Prerequisiti minimi

Serve un ambiente Windows con:
- .NET SDK 8 installato;
- workload desktop .NET/WPF disponibile.

Il repository usa `global.json`, quindi il setup controlla automaticamente che la toolchain disponibile sia compatibile.

## Setup rapido

Dalla root del repository esegui:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1
```

Il comando esegue in sequenza:
1. restore delle dipendenze (`bootstrap`);
2. verifica prerequisiti minimi (`doctor`);
3. build `Release` (`build`).

Se vuoi solo preparare l'ambiente senza compilare:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1 -SkipBuild
```

Se devi usare anche packaging MSI o servizio Windows e vuoi rendere bloccanti quei controlli:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1 -RequireOptionalTools
```

## Avvio dell'app

Dopo il setup puoi avviare l'app principale con:

```powershell
dotnet run --project .\src\NtfsAudit.App\NtfsAudit.App.csproj -f net8.0-windows
```

Per una verifica veloce dell'ambiente puoi anche eseguire i test automatici:

```powershell
powershell -File .\scripts\test.ps1 -Configuration Release -SkipRestore
```

## Configurazione iniziale nell'interfaccia

Alla prima apertura usa questa sequenza minima.

### 1. Scegli le cartelle da analizzare

Nel riquadro `Scansione`:
1. usa `Sfoglia...` oppure inserisci il path nel campo testo;
2. premi `Aggiungi`;
3. verifica che la cartella compaia in `Cartelle da scansionare`.

Se devi analizzare piu root, aggiungile una alla volta.

### 2. Imposta la cartella di output

Nel campo `Output report .ntaudit`:
1. premi `Sfoglia...`;
2. scegli una cartella dove salvare i report;
3. verifica che il path resti valorizzato.

Se non imposti un output dedicato, non hai un archivio `.ntaudit` finale pronto per il riuso.

### 3. Lascia disattivato il servizio Windows, salvo necessita specifiche

L'opzione `Esegui tramite servizio Windows` e opzionale.

Per il primo utilizzo:
- lasciala disattivata;
- abilitala solo se devi eseguire scansioni lunghe in background o fuori dalla sessione interattiva.

### 4. Configura le credenziali solo se servono

Nella sezione `Credenziali scansione`:
- usa `Globali` se vuoi una credenziale comune a tutte le root;
- usa `Override root` solo per la cartella selezionata;
- se non hai esigenze particolari, non salvare nulla e usa l'utente Windows corrente.

Priorita effettiva:
`override root -> globali -> utente corrente`

### 5. Mantieni le opzioni iniziali semplici

Per un primo test, imposta solo l'essenziale:
- abilita `Analizza tutte le sottocartelle` se vuoi una scansione completa;
- usa `Profondita` solo se vuoi limitare il perimetro;
- lascia attivo `Includi permessi ereditati` se vuoi un quadro completo;
- abilita `Risolvi identita` solo se ti serve il mapping SID -> utente/gruppo leggibile.

Le opzioni avanzate possono aumentare tempi e complessita della scansione.

## Prima analisi consigliata

Per una prima prova semplice:
1. aggiungi una cartella di test limitata;
2. imposta una cartella di output dedicata;
3. lascia disattivato `Esegui tramite servizio Windows`;
4. premi `Avvia analisi`;
5. al termine verifica il report `.ntaudit` nella cartella scelta;
6. se serve, usa `Esporta Excel`.

## Quando usare il servizio Windows

Usa il servizio solo se hai davvero bisogno di:
- scansioni lunghe in background;
- esecuzione disaccoppiata dalla sessione utente;
- gestione job tramite host dedicato.

Se ti serve questa modalita, verifica prima i prerequisiti estesi:

```powershell
powershell -File .\scripts\doctor.ps1 -RequireOptionalTools
```

## Problemi comuni

### Il setup fallisce subito

Controlla:
- presenza di `dotnet`;
- SDK compatibile con `global.json`;
- workload desktop installato.

Per il dettaglio diagnostico:

```powershell
powershell -File .\scripts\doctor.ps1
```

### Vuoi ripartire da uno stato pulito

Esegui:

```powershell
powershell -File .\scripts\reset-repo-state.ps1
```

### Vuoi pulire anche i residui operativi dell'app

Esegui:

```powershell
powershell -File .\scripts\clean.ps1 -CleanOperationalData
```
