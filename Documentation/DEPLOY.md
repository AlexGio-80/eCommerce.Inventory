# eCommerce.Inventory - Deployment

---

## Ambienti

| Ambiente | URL / Host | Database | Note |
|----------|-----------|----------|------|
| Sviluppo | `http://localhost:5152` (API) / `http://localhost:4200` (UI) | `ECommerceInventory` su `DEV-ALEX\MSSQLSERVER01` | `appsettings.json` locale |
| Produzione | `http://inventory.local` (UI) / `http://localhost:5152` (API) | `ECommerceInventory` locale | `appsettings.Production.json` |

---

## Procedura di Deploy (Produzione)

### Metodo automatico (raccomandato)

```powershell
# Eseguire come Administrator dalla root del progetto
.\publish.ps1
```

Lo script esegue in sequenza:
1. Stop IIS site e Windows Service
2. Pulizia directory `Publish/`
3. Build backend (`dotnet publish` Release)
4. Build frontend (`ng build --configuration production`)
5. Preserva `appsettings.Production.json` esistente
6. Crea directory logs con permessi `NetworkService`
7. Aggiorna/configura Windows Service
8. Start IIS e Windows Service

### Verifica migration pendenti

Prima del deploy, verificare se ci sono migration non applicate:

```bash
dotnet ef migrations list --project src/eCommerce.Inventory.Infrastructure --startup-project src/eCommerce.Inventory.Api
```

Se ci sono migration pendenti, applicarle al DB prima di avviare la nuova versione:

```bash
dotnet ef database update --project src/eCommerce.Inventory.Infrastructure --startup-project src/eCommerce.Inventory.Api
```

> Fare sempre un backup del database prima di applicare migration in produzione.

Le migration pendenti vengono applicate **all'avvio del servizio, in ogni ambiente** (`Program.cs`).
Il log riporta prima l'elenco di quelle da applicare e poi l'esito, quindi resta traccia di quando
lo schema è cambiato e di cosa è cambiato. Se la migrazione fallisce l'applicazione non parte:
è voluto, perché un servizio fermo si nota mentre uno che scrive su uno schema sbagliato no.

Non esiste un interruttore per disattivarle: sarebbe un secondo modo di ritrovarsi con codice e
schema disallineati, e fino al 29/08/2026 quel disallineamento è stato possibile proprio perché le
migration giravano solo in Development. Il comando resta comunque utile per ispezionare in anticipo
cosa verrà applicato:

```bash
dotnet ef migrations script <ultima-applicata> <ultima-da-applicare> --project eCommerce.Inventory.Infrastructure --startup-project eCommerce.Inventory.Api --context ApplicationDbContext --output migrazioni.sql
```

Le migration già applicate si leggono da `SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC`.

> Il backup del database prima di un deploy che contiene migration resta comunque consigliato:
> l'applicazione automatica toglie il rischio di dimenticarsene, non quello di una migration sbagliata.

#### Deploy del 2026-08-28 — nota specifica

Questo rilascio contiene la migration `20260828071742_PreservaStoricoPrezziCarteVendute` (foreign key `PriceChangeLogs → InventoryItems` da `CASCADE` a `SET NULL`) e la correzione della sincronizzazione dell'inventario, ferma dal 03/12/2025.

**Alla prima sincronizzazione notturna dopo il deploy verranno cancellati 282 articoli e ne verranno inseriti 192**: è il recupero di otto mesi di deriva accumulata, non una perdita di dati. Fare un backup manuale prima del primo avvio e verificare il conteggio articoli il giorno seguente — deve coincidere con le carte dei giochi abilitati presenti su Card Trader.

### Gestione manuale del servizio

```powershell
Stop-Service  -Name "eCommerce.Inventory"
Start-Service -Name "eCommerce.Inventory"
Get-Service   -Name "eCommerce.Inventory"
```

### Verifica post-deploy

1. `http://inventory.local` — deve mostrare la login page Angular
2. `http://localhost:5152/health` — deve rispondere `{"status":"Healthy"}` *(se implementato)*
3. `Get-Service -Name "eCommerce.Inventory"` — deve essere `Running`
4. Controllare i log in `Publish/api/logs/` per errori all'avvio

---

## Prima Installazione

### 1. Configurare IIS (una sola volta)

```powershell
# Eseguire come Administrator
.\setup-iis.ps1
```

Crea:
- Application Pool `InventoryAppPool`
- IIS Site `InventorySite` su porta 80 → `Publish/ui`
- `web.config` per Angular routing (URL rewrite)
- Entry `inventory.local` nel file hosts

### 2. Configurare `appsettings.Production.json`

Creare `src/eCommerce.Inventory.Api/appsettings.Production.json` (non committare):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server={server};Database=ECommerceInventory;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "CardTraderSettings": {
    "BaseUrl": "https://api.cardtrader.com/api/v2",
    "BearerToken": "***",
    "SharedSecret": "***"
  },
  "JwtSettings": {
    "SecretKey": "***chiave-di-almeno-32-caratteri***",
    "Issuer": "eCommerce.Inventory",
    "Audience": "eCommerce.Inventory",
    "ExpiryMinutes": 1440
  }
}
```

### 3. Creare il Windows Service (prima volta)

```powershell
sc create "eCommerce.Inventory" binPath="C:\{install-path}\Publish\api\eCommerce.Inventory.Api.exe" start=auto
sc description "eCommerce.Inventory" "eCommerce Inventory API - Card Trader Management"
sc config "eCommerce.Inventory" obj="NT AUTHORITY\NetworkService"
```

### 4. Permessi cartella logs

```powershell
icacls "C:\{install-path}\Publish\api\logs" /grant "*S-1-5-20:(OI)(CI)M"
```

### 5. Primo deploy

```powershell
.\publish.ps1
```

---

## Variabili di Configurazione (Produzione)

| Chiave | Descrizione |
|--------|-------------|
| `ConnectionStrings:DefaultConnection` | Connection string SQL Server |
| `CardTraderSettings:BearerToken` | Token API Card Trader |
| `CardTraderSettings:SharedSecret` | Shared secret per verifica webhook HMAC |
| `JwtSettings:SecretKey` | Chiave firma JWT (min 32 caratteri) |

> In produzione: usare `appsettings.Production.json` (non committato) o variabili d'ambiente di sistema.

---

## Rollback

```powershell
Stop-Service -Name "eCommerce.Inventory"
# Ripristinare i file della versione precedente nella cartella Publish/
Start-Service -Name "eCommerce.Inventory"
```

Per rollback DB: ripristinare dal backup (il backup giornaliero automatico è in `BackupSettings:BackupPath`).

---

## Troubleshooting

| Sintomo | Causa probabile | Soluzione |
|---------|----------------|-----------|
| Frontend mostra pagina IIS default | IIS non configurato | Eseguire `setup-iis.ps1` come Admin |
| API restituisce 404 | Windows Service non in esecuzione | `Start-Service "eCommerce.Inventory"` |
| Service non si avvia | `appsettings.Production.json` mancante o errato | Verificare il file e i permessi |
| Log non scritti | `NetworkService` non ha permessi sulla cartella | Vedi sezione "Permessi cartella logs" |
| Cartella `logs` vuota, ma il servizio gira | Tre cause possibili, in ordine di frequenza osservata | 1) La working directory di un servizio Windows è `C:\Windows\System32`, quindi un percorso di log **relativo** viene risolto lì. `Program.cs` la riallinea con `Directory.SetCurrentDirectory(AppContext.BaseDirectory)`; se il file non compare, leggere `serilog-selflog.txt` accanto all'eseguibile. 2) `Serilog:MinimumLevel` troppo alto: il sink su file non crea il file finché non arriva un evento di quel livello. 3) Permessi mancanti sulla cartella |
| Serilog non scrive e non si capisce perché | I sink che non riescono a inizializzarsi vengono scartati in silenzio | `serilog-selflog.txt` accanto all'eseguibile riporta l'eccezione vera (percorso negato, sink non trovato, enricher inesistente). È la prima cosa da guardare |
| `icacls` non concede i permessi ma il deploy sembra riuscito | Il nome `NT AUTHORITY\NETWORK SERVICE` è localizzato e non si risolve su Windows non inglese, e `icacls` segnala l'errore con l'exit code, non con un'eccezione | Usare sempre il SID `*S-1-5-20`. `publish.ps1` lo fa e controlla `$LASTEXITCODE` |
| File di log con suffisso `_001` | Due sink File sullo stesso percorso: gli array di configurazione si fondono per indice, non si concatenano | Dichiarare il sink File solo in `appsettings.{Environment}.json`, mai anche in `appsettings.json` |
| Sync notturna riportata come riuscita ma i dati non cambiano | Una singola sezione fallisce e l'esito complessivo non la riflette (corretto il 2026-08-28) | Controllare nei log la riga `Sync completata con N sezioni fallite`, e la metrica `ecommerce_sync_total{status="failure"}` su `/metrics` |
| Errore connessione DB | SQL Server non raggiungibile | Verificare connection string e stato SQL Server |

Log applicazione: `Publish/api/logs/`
Event Viewer: `Windows Logs → Application`
Log IIS: `C:\inetpub\logs\LogFiles\`

---

## Checklist Deploy

- [ ] Backup database eseguito
- [ ] Migration pendenti verificate (si applicano da sole all'avvio; controllare nel log la riga `Migration applicate correttamente`)
- [ ] `appsettings.Production.json` sul server presente e aggiornato
- [ ] Script `publish.ps1` eseguito come Administrator
- [ ] Windows Service in stato `Running`
- [ ] `http://inventory.local` risponde correttamente
- [ ] Log puliti (nessun errore all'avvio) — e la cartella `logs` **contiene un file**: se è vuota, la diagnostica non sta funzionando
- [ ] Test funzionale rapido (login, sync, lista ordini)
- [ ] Il giorno dopo il deploy: verificare nei log l'esito della sincronizzazione notturna e che il conteggio articoli coincida con Card Trader
