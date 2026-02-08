# Audit completo NTFS/Share in dominio Windows (note e fonti)

Questa nota raccoglie i requisiti tipici per un audit completo in dominio Windows (share + NTFS) con riferimenti a fonti attendibili.  
Le fonti principali sono la documentazione Microsoft su:  
- **Advanced Audit Policy** (Object Access / File System),  
- **SACL** e auditing di file/cartelle,  
- **Condivisioni SMB** e permessi di share,  
- **Access-based enumeration** e visibilità delle risorse.

## Requisiti minimi per audit completo

1. **Abilitare la policy di audit per il file system**  
   È necessario abilitare la subcategoria **“File System”** in **Advanced Audit Policy** (Object Access).  
   Senza questa policy, anche con SACL configurate, gli eventi non vengono loggati.  
   Fonte: Microsoft — Advanced Audit Policy “Object Access > File System”.  
   https://learn.microsoft.com/windows/security/threat-protection/auditing/advanced-audit-policy-configuration

2. **Configurare SACL su cartelle/oggetti**  
   Le voci SACL definiscono *cosa* viene auditato (success/failure, diritti specifici).  
   Per impostarle serve il privilegio **SeSecurityPrivilege** (local policy “Manage auditing and security log”).  
   Fonte: Microsoft — auditing file and folder access (SACL).  
   https://learn.microsoft.com/windows/security/threat-protection/auditing/audit-file-system

3. **Permessi di lettura ACL / Security Descriptor**  
   Per leggere ACL/SACL serve accesso all’oggetto (tipicamente **Read permissions** o **Read attributes/Read permissions**);  
   per leggere SACL serve il privilegio sopra.  
   Fonte: Microsoft — File/Folder permissions and security descriptors.  
   https://learn.microsoft.com/windows/security/identity-protection/access-control/access-control

4. **Condivisioni SMB: permessi di share + NTFS**  
   L’accesso effettivo è l’intersezione **Share permissions** e **NTFS permissions**.  
   Per audit completo delle share servono entrambi i livelli.  
   Fonte: Microsoft — Share and NTFS permissions.  
   https://learn.microsoft.com/windows-server/storage/file-server/share-and-ntfs-permissions

5. **Event Log**  
   I log di sicurezza devono essere adeguatamente dimensionati e centralizzati (es. SIEM) per conservare eventi di audit.  
   Fonte: Microsoft — Security event log management.  
   https://learn.microsoft.com/windows/security/threat-protection/auditing/security-auditing-overview

## Mappatura concetti “Windows UI” vs audit

| Concetto UI Windows | Cosa rappresenta | Impatto su audit |
|---|---|---|
| **Permessi Share** | ACL della condivisione SMB | Limita accesso remoto; audit share richiede visibilità di questi permessi |
| **Permessi NTFS** | ACL sul filesystem | Determina accesso effettivo; audit SACL applicato qui |
| **SACL** | Regole di auditing (success/failure) | Genera eventi di sicurezza |
| **Access-based enumeration (ABE)** | Nasconde cartelle non accessibili | Influenza visibilità percorsi ai client |
| **Owner** | Proprietario dell’oggetto | Rilevante per conformità e deleghe |

Fonte ABE: Microsoft — Access-based enumeration.  
https://learn.microsoft.com/windows-server/storage/dfs-namespaces/enable-access-based-enumeration-on-a-dfs-namespace

## Errori tipici se l’audit non è “completo”

- **SACL vuote o non leggibili**: mancano privilegi `SeSecurityPrivilege` o policy di audit abilitata.  
- **Audit eventi mancanti**: policy avanzata disabilitata o SACL non impostate sull’oggetto.  
- **Accesso effettivo errato**: analisi basata solo su NTFS, senza permessi di share.  

## Suggerimenti per UI / analisi coerente con Windows

- Mostrare **NTFS + Share** in un’unica vista “Effective Access” (intersezione), separando:  
  - “NTFS effective”  
  - “Share effective”  
  - “Overall effective (Share ∩ NTFS)”
- Visualizzare **eredità** e **protezione** come badge (P) e indicatori in TreeView.  
- Esporre “Scope” dell’ACE (This folder only / Subfolders / Files) come etichetta esplicita.

## Prompt per refactoring (Codex)

> Refactor the NTFS Audit WPF app to model share + NTFS permissions explicitly and compute effective access as the intersection of share and NTFS ACLs.  
> 1) Introduce domain models for SharePermission, NtfsPermission, and EffectivePermission with explicit flags for inheritance scope, allow/deny, and source (share vs NTFS).  
> 2) Extend ScanService to optionally query SMB share permissions (server-side) and merge them with NTFS results.  
> 3) Update UI: add a “Share” tab and an “Effective Access” tab; show badges for inheritance/protection/explicit deny.  
> 4) Add treeview markers for changes vs baseline and explicit NTFS/share changes.  
> 5) Ensure imports/exports serialize the new permission layers and add versioning to the .ntaudit archive.  
> Provide migration steps for legacy archives and ensure unit tests cover permission merges, inheritance scopes, and baseline comparisons.
