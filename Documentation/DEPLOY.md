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

#### Effetti alla prima sincronizzazione dopo un deploy

Alcuni rilasci si manifestano solo alla prima esecuzione notturna, non all'avvio del servizio: il
giorno del deploy sembra non essere successo nulla e le sorprese arrivano la mattina dopo. Vale la
pena sapere cosa aspettarsi prima di andare a dormire.

- **Un rilascio che tocca la sincronizzazione può muovere molti articoli in un colpo solo.** Il
  2026-08-29, dopo la correzione del blocco che durava da dicembre, la prima notturna ha cancellato
  282 articoli venduti e ne ha inseriti 192: era il recupero della deriva accumulata, non una
  perdita di dati. Verificare il giorno dopo che il conteggio articoli coincida con le carte dei
  giochi abilitati presenti su Card Trader.
- **La prima rilevazione dello storico prezzi scrive una riga per ogni inserzione**, circa 35.000,
  perché ogni serie ha bisogno di un punto di partenza. Dalla notte successiva vengono scritti solo
  i prezzi che cambiano, quindi poche decine o centinaia. Un secondo giro con numeri ancora
  nell'ordine delle decine di migliaia segnalerebbe che il confronto con la rilevazione precedente
  non sta funzionando.

Fare comunque un backup manuale prima di un deploy che contiene migration.

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
  },
  "CardmarketImport": {
    "Enabled": true,
    "RunTime": "07:00",
    "SinglesTrackingMonths": 12
  },
  "Email": {
    "Enabled": false,
    "Host": "smtp.gmail.com",
    "Port": 587,
    "EnableSsl": true,
    "UserName": "",
    "Password": "***",
    "From": "",
    "To": ""
  }
}
```

> **Email degli avvisi sugli acquisti**: con Gmail `UserName` e `From` sono l'indirizzo Gmail e
> `Password` è una **password per le app** (myaccount.google.com/apppasswords, richiede la verifica in
> due passaggi), non la password dell'account. Poi `Enabled` a `true`, pubblicare e usare "Email di
> prova" nella scheda Avvisi della pagina Acquisti. L'invio è solo in uscita: nessuna porta da aprire.

> **Accesso a internet in uscita**: oltre a Card Trader e Scryfall, il servizio scarica ogni giorno
> i file pubblici di Cardmarket (`downloads.s3.cardmarket.com`, ~50 MB) e i file di MTGJSON
> (`mtgjson.com`, ~12 MB più qualche MB per uscita).

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

## Accesso dal telefono, fuori casa (Tailscale)

> Fase 0 della feature 006 (`Documentation/Features/006-AppSmartphone.md`). **Nessuna porta aperta sul
> router**: il PC e il telefono entrano nella stessa rete privata Tailscale con connessioni in uscita, e
> il gestionale resta invisibile da internet. Tailscale Serve dà l'HTTPS con un certificato valido, che
> il browser del telefono richiede per la fotocamera e per installare l'app.

Schema finale:

| Dal telefono | Tailscale Serve inoltra a | Cosa c'è |
|---|---|---|
| `https://<pc>.<tailnet>.ts.net` | `http://127.0.0.1:8080` | IIS, il frontend |
| `https://<pc>.<tailnet>.ts.net:8443` | `http://127.0.0.1:5152` | Kestrel, l'API e l'hub SignalR |

Il frontend capisce da solo dove si trova: aperto da un indirizzo `*.ts.net` chiama l'API sulla porta
8443 dello stesso nome; aperto da `http://inventory.local` chiama `http://localhost:5152` come sempre
(`environment.prod.ts`). Kestrel e il binding `inventory.local` di IIS non cambiano.

### 1. Tailscale sul PC (una sola volta)

1. Scaricare e installare Tailscale per Windows da `https://tailscale.com/download/windows` e accedere
   con l'account scelto (piano personale gratuito).
2. Tenerlo attivo anche senza nessun utente collegato a Windows, dal prompt dei comandi come Administrator:
   ```powershell
   tailscale up --unattended
   ```
3. Nel pannello web di Tailscale (`https://login.tailscale.com/admin`):
   - **DNS**: MagicDNS attivo e **HTTPS Certificates** abilitati;
   - **Machines**: segnare il nome completo del PC, `<pc>.<tailnet>.ts.net` (serve sotto), e
     disattivare la scadenza della chiave del PC (**Disable key expiry**), altrimenti dopo qualche mese
     il PC esce dalla rete e il telefono non lo raggiunge più.

### 2. Binding di IIS solo locale (una sola volta)

Il sito IIS risponde solo al nome `inventory.local`, mentre le richieste che arrivano da Tailscale Serve
hanno un altro nome. Si aggiunge un binding sulla porta 8080 **solo su 127.0.0.1**, senza nome: dalla rete
di casa non è raggiungibile, ed è la porta a cui Tailscale Serve inoltra.

```powershell
# Come Administrator
& "$env:windir\system32\inetsrv\appcmd.exe" set site /site.name:"InventorySite" /+"bindings.[protocol='http',bindingInformation='127.0.0.1:8080:']"
```

`publish.ps1` non tocca i binding di un sito che esiste già, quindi resta anche dopo le pubblicazioni.
Controllo: `http://127.0.0.1:8080` nel browser del PC apre il gestionale.

### 3. Tailscale Serve (una sola volta)

```powershell
# Come Administrator
tailscale serve --bg --https=443 http://127.0.0.1:8080
tailscale serve --bg --https=8443 http://127.0.0.1:5152
tailscale serve status
```

La configurazione resta salvata e riparte da sola con il servizio Tailscale. Per toglierla:
`tailscale serve reset`. **Mai `tailscale funnel`**: pubblicherebbe il gestionale su internet.

### 4. Origine Tailscale nell'API

In `appsettings.Production.json` (non versionato: il nome della rete Tailscale non va nel repository):

```json
"Cors": {
  "ExtraOrigins": [ "https://<pc>.<tailnet>.ts.net" ]
}
```

Poi pubblicare con `publish.ps1` (o riavviare il servizio). Senza questa voce la pagina si apre dal
telefono ma ogni chiamata all'API è rifiutata dal browser (errore CORS nella console).

### 5. Il telefono (Android)

1. Installare **Tailscale** dal Play Store e accedere con lo **stesso account** del PC.
2. Con Tailscale acceso, aprire in Chrome `https://<pc>.<tailnet>.ts.net` e fare login.
3. Facoltativo, dalla Fase 1 in poi: menu di Chrome → "Aggiungi a schermata Home".

### 6. Limitare il telefono alle due porte (consigliato)

Il criterio predefinito di Tailscale lascia ogni dispositivo dell'account libero di raggiungere tutti gli
altri su ogni porta. Per far arrivare il telefono solo al gestionale, nel pannello web → **Access controls**,
sostituire il criterio con (l'indirizzo `100.x.y.z` del PC è nella pagina Machines):

```json
{
  "hosts": { "inventory-pc": "100.x.y.z" },
  "grants": [
    { "src": ["autogroup:member"], "dst": ["inventory-pc"], "ip": ["tcp:443", "tcp:8443"] }
  ]
}
```

Attenzione: è l'unica regola, quindi ogni altro accesso fra i dispositivi dell'account smette di
funzionare, ping compreso: dopo il salvataggio il ping dall'app del telefono al PC può non rispondere, ed
è normale. Se ci sono altri dispositivi in rete che si parlano, aggiungere le loro regole.

### 7. Verifica

- Dal telefono **in 4G, con il Wi-Fi spento**: `https://<pc>.<tailnet>.ts.net` apre la pagina di login
  con il lucchetto, il login riesce, la dashboard mostra i dati (quindi l'API sulla 8443 risponde).
- Dal telefono con **Tailscale spento**: l'indirizzo non risponde.
- Dal PC: `http://inventory.local` funziona come prima.
- Telefono perso: pannello web → Machines → rimuovere il telefono. L'accesso finisce subito, anche
  se il JWT (7 giorni) è ancora valido.

| Problema | Causa probabile | Soluzione |
|---|---|---|
| La pagina si apre ma il login fallisce, errore CORS nella console | `Cors:ExtraOrigins` mancante o diverso dall'indirizzo esatto (con `https://`, senza barra finale) | Correggere `appsettings.Production.json` e riavviare il servizio |
| Pagina IIS 404 o pagina predefinita di IIS | Binding `127.0.0.1:8080` mancante | Punto 2 |
| Errore di certificato o `tailscale serve` rifiuta l'HTTPS | HTTPS Certificates non abilitati nel pannello | Punto 1.3 |
| Dal PC `https://<pc>.<tailnet>.ts.net` va in timeout, dal telefono funziona | Il PC non raggiunge il proprio indirizzo Tailscale (osservato il 2026-10-16, con NordVPN attivo sul PC) | Niente da correggere: sul PC si usa `http://inventory.local` |
| Dal telefono non risponde niente, con Tailscale acceso | PC fuori dalla rete (chiave scaduta, servizio fermo) o criterio di accesso troppo stretto | `tailscale status` sul PC; Machines nel pannello; Access controls |

---

## Variabili di Configurazione (Produzione)

| Chiave | Descrizione |
|--------|-------------|
| `ConnectionStrings:DefaultConnection` | Connection string SQL Server |
| `CardTraderSettings:BearerToken` | Token API Card Trader |
| `CardTraderSettings:SharedSecret` | Shared secret per verifica webhook HMAC |
| `JwtSettings:SecretKey` | Chiave firma JWT (min 32 caratteri) |
| `CardmarketImport:Enabled` / `RunTime` | Import giornaliero listino Cardmarket, catalogo MTGJSON, classifica e avvisi (in produzione `true`, 07:00) |
| `Purchasing:*` | Parametri del valore atteso (soglia e prezzo del bulk, costi di vendita) e lotto giornaliero di uscite MTGJSON |
| `Email:*` | SMTP per le email degli avvisi (password solo qui) |
| `Cors:ExtraOrigins` | Origini in più accettate dall'API, oltre a `localhost:4200` e `inventory.local`: l'indirizzo Tailscale del PC per il telefono (vedi "Accesso dal telefono") |

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
- [ ] Pagina Espansioni: il pulsante "Listino Cardmarket" riporta un import riuscito (registro in `CardmarketImportLogs`); pagina Acquisti: la scheda Opportunità ha la classifica del giorno
- [ ] Il giorno dopo il deploy: verificare nei log l'esito della sincronizzazione notturna, che il conteggio articoli coincida con Card Trader, e la riga `Storico prezzi: N rilevazioni registrate` (vedi "Effetti alla prima sincronizzazione")
