# eCommerce.Inventory - Architettura

## Panoramica

eCommerce.Inventory è un sistema di gestione inventario per carte collezionabili (TCG: Trading Card Games), integrato con la piattaforma Card Trader. L'architettura segue i principi di **Clean Architecture** con pattern **CQRS** (MediatR) per i webhook e **Service + Repository** per le operazioni standard.

---

## Stack Tecnologico

- **Framework**: .NET 8 Web API
- **Database**: SQL Server con Entity Framework Core 8
- **Architettura**: Clean Architecture a 4 strati
- **Logging**: Serilog
- **Dependency Injection**: Built-in .NET DI Container
- **ORM**: Entity Framework Core 8
- **API Documentation**: Swagger/OpenAPI

## Struttura dei Progetti

```
eCommerce.Inventory/
├── eCommerce.Inventory.Domain/          (Entità e business logic)
├── eCommerce.Inventory.Application/     (Interfacce e CQRS)
├── eCommerce.Inventory.Infrastructure/  (Implementazioni, DB, API External)
└── eCommerce.Inventory.Api/             (Web API, Controllers)
```

### 1. **Domain Layer** (eCommerce.Inventory.Domain)

**Responsabilità**: Definire il modello di dominio e le entità di business.

**Componenti**:
- **Entities/**
  - `Game.cs`: Rappresenta un gioco TCG (Magic, YGO, etc.)
  - `Expansion.cs`: Espansione di un gioco
  - `Blueprint.cs`: Matrice di una carta (definizione)
  - `InventoryItem.cs`: Oggetto nell'inventario
  - `Order.cs`: Ordine ricevuto da un marketplace
  - `OrderItem.cs`: Riga di un ordine

**Caratteristiche**:
- ✅ No dependencies su altri strati
- ✅ Entities sono POCO (Plain Old CLR Objects)
- ✅ Business logic pura
- ✅ Relazioni One-to-Many configurate con navigation properties

---

### 2. **Application Layer** (eCommerce.Inventory.Application)

**Responsabilità**: Definire i contratti (interfacce) per repository e servizi.

**Componenti**:
- **Interfaces/**
  - `IApplicationDbContext.cs`: Abstrazione del DbContext
  - `IReadonlyRepository<T>.cs`: Interface generica per letture
  - `IInventoryItemRepository.cs`: CRUD specifico per InventoryItem
  - `ICardTraderApiService.cs`: Comunicazione con API Card Trader

**Caratteristiche**:
- ✅ Dipende solo da Domain
- ✅ Non contiene implementazioni
- ✅ Placeholder per CQRS (Commands/Queries)

---

### 3. **Infrastructure Layer** (eCommerce.Inventory.Infrastructure)

**Responsabilità**: Implementare i servizi e la persistenza dati.

**Componenti**:

#### Persistence
- **ApplicationDbContext.cs**: EF Core DbContext
  - Implementa `IApplicationDbContext`
  - Configura relazioni One-to-Many
  - Imposta precision per campi decimali
  - Migrations Assembly configured

- **Repositories/InventoryItemRepository.cs**
  - Implementa `IInventoryItemRepository`
  - CRUD con eager loading delle relazioni
  - Query ottimizzate con Include()

#### External Services
- **CardTrader/**
  - `CardTraderApiClient.cs`: Client HTTP per API Card Trader
    - Implementa `ICardTraderApiService`
    - Metodi per sincronizzare games, expansions, blueprints
    - Operazioni CRUD su prodotti
    - Fetch orders

  - `CardTraderSyncWorker.cs`: BackgroundService
    - Polling periodico (ogni 15 minuti)
    - Sincronizzazione dati da Card Trader
    - Gestione errori con retry

  - **DTOs/**: Modelli per deserializzazione risposte API
    - `CardTraderGameDto.cs`
    - `CardTraderExpansionDto.cs`
    - `CardTraderBlueprintDto.cs`
    - `CardTraderProductDto.cs`
    - `CardTraderOrderDto.cs`

**Caratteristiche**:
- ✅ Dipende da Domain e Application
- ✅ Contiene tutte le implementazioni concrete
- ✅ HttpClient configurato con Bearer Token
- ✅ Logging completo

---

### 4. **API Layer** (eCommerce.Inventory.Api)

**Responsabilità**: Esporre gli endpoint REST.

**Componenti**:

- **Program.cs**: Configurazione bootstrap
  - DI setup
  - EF Core DbContext registration
  - Serilog configuration
  - HttpClient for Card Trader
  - CORS configuration
  - Hosted Services registration

- **Controllers/CardTrader/**
  - `CardTraderInventoryController.cs`: CRUD per inventario
    - GET /api/cardtrader/inventory
    - GET /api/cardtrader/inventory/{id}
    - POST /api/cardtrader/inventory
    - PUT /api/cardtrader/inventory/{id}
    - DELETE /api/cardtrader/inventory/{id}

  - `Controllers/CardTraderWebhooksController.cs`: Webhook receiver
    - POST /api/cardtraderwebhooks/events
    - Notifiche real-time da Card Trader (order.create/update/destroy)
    - Verifica della firma HMAC via `WebhookSignatureVerificationService`

  - `CardTraderSyncController.cs`: Sync operations
    - POST /api/cardtrader/sync/games
    - POST /api/cardtrader/sync/expansions
    - POST /api/cardtrader/sync/blueprints
    - POST /api/cardtrader/sync/products
    - POST /api/cardtrader/sync/orders
    - POST /api/cardtrader/sync/full

- **appsettings.json**: Configurazione
  - Connection String SQL Server
  - Card Trader API Base URL e Token
  - Serilog settings

**Caratteristiche**:
- ✅ RESTful endpoints
- ✅ Marketplace-specific routing (`/api/cardtrader/...`)
- ✅ Swagger/OpenAPI documentation
- ✅ Serilog request/response logging

---

## Flusso Dati

```
┌─────────────────────────────────────────────────────────────┐
│                    External Systems                         │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ Card Trader API | SQL Server | Webhooks             │  │
│  └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                        ↕
┌─────────────────────────────────────────────────────────────┐
│              Infrastructure Layer                           │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ CardTraderApiClient | DbContext | Repositories     │  │
│  │ CardTraderSyncWorker | DTOs                        │  │
│  └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                        ↕
┌─────────────────────────────────────────────────────────────┐
│             Application Layer (Interfaces)                  │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ IApplicationDbContext | IInventoryItemRepository    │  │
│  │ ICardTraderApiService                              │  │
│  └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                        ↕
┌─────────────────────────────────────────────────────────────┐
│                Domain Layer (Entities)                      │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ Game | Expansion | Blueprint | InventoryItem |      │  │
│  │ Order | OrderItem                                   │  │
│  └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                        ↕
┌─────────────────────────────────────────────────────────────┐
│                   API Layer (Controllers)                   │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ CardTraderInventoryController                       │  │
│  │ CardTraderWebhooksController                        │  │
│  │ CardTraderSyncController                            │  │
│  └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                        ↕
┌─────────────────────────────────────────────────────────────┐
│                   HTTP Clients                              │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ Browsers | Mobile Apps | Internal Services         │  │
│  └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

---

## Database Schema

### Tabelle Principali

```
Games
├─ Id (PK)
├─ CardTraderId
├─ Name
└─ Code

Expansions
├─ Id (PK)
├─ CardTraderId
├─ Name
├─ Code
├─ GameId (FK → Games)
├─ AverageCardValue, TotalMinPrice, LastValueAnalysisUpdate
├─ AvgValueCommon, AvgValueUncommon, AvgValueRare, AvgValueMythic
├─ ReleaseDate, IconSvgUri
├─ PacksPerBox, CardsPerPack  ← configurazione box calcolatore
└─ BoxPrice                   ← prezzo box (decimal 18,2), usato per ROI%

Blueprints
├─ Id (PK)
├─ CardTraderId
├─ Name
├─ Version
├─ Rarity
└─ ExpansionId (FK → Expansions)

InventoryItems
├─ Id (PK)
├─ CardTraderProductId (nullable)
├─ BlueprintId (FK → Blueprints)
├─ PurchasePrice (decimal 18,2)
├─ DateAdded
├─ Quantity
├─ ListingPrice (decimal 18,2)
├─ Condition
├─ Language
├─ IsFoil
├─ IsSigned
└─ Location

Orders
├─ Id (PK)
├─ CardTraderOrderId
├─ DatePlaced
├─ Status
├─ TotalAmount (decimal 18,2)
└─ ShippingCost (decimal 18,2)

OrderItems
├─ Id (PK)
├─ OrderId (FK → Orders)
├─ InventoryItemId (FK → InventoryItems)
├─ QuantitySold
└─ PricePerItem (decimal 18,2)

PricingProfiles
├─ Id (PK)
├─ Name, IsActive, DryRun
├─ MinPrice, MaxChangePercentPerRun
├─ filtri venditore (IncludeProSellers, ExcludeVacationSellers, CountryCodesCsv, ...)
└─ criteri di comparabilità (MatchCondition, MatchLanguage, MatchFoil, ...)

PricingRules
├─ Id (PK)
├─ PricingProfileId (FK → PricingProfiles, CASCADE)
├─ FromPrice / ToPrice (fascia di applicazione)
├─ ReferenceMode / Position
└─ AdjustmentAmount, AdjustmentPercent, CanIncrease, CanDecrease, Priority

PricingRunLogs                    -- riepilogo di una esecuzione
├─ Id (PK)
├─ PricingProfileId (FK → PricingProfiles)
├─ Trigger, DryRun, StartedAt, CompletedAt
└─ contatori per esito + TotalPriceDelta

PriceChangeLogs                   -- una riga per carta valutata
├─ Id (PK)
├─ InventoryItemId (FK → InventoryItems, SET NULL, nullable)
├─ BlueprintId (FK → Blueprints, NO ACTION)
├─ PricingRunLogId (FK → PricingRunLogs, SET NULL, nullable)
├─ OldPrice, ProposedPrice, ReferencePrice (decimal 18,2)
├─ Outcome, Trigger
├─ ComparableOffersCount, OutliersRejectedCount
└─ Reason (nvarchar 1000)
```

PriceHistoryEntries               -- serie storica del prezzo esposto
├─ Id (PK)
├─ BlueprintId (FK → Blueprints, NO ACTION)
├─ InventoryItemId (FK → InventoryItems, SET NULL, nullable)
├─ CardTraderProductId            -- chiave stabile della serie
├─ Price (decimal 18,2)           -- scala venditore, come l'export
├─ Quantity
├─ Condition / Language / IsFoil  -- denormalizzati: la riga resta leggibile senza l'inserzione
└─ RecordedAt
```

> `PriceHistoryEntries` è **a delta**: una riga esiste solo quando prezzo o quantità cambiano
> rispetto alla rilevazione precedente, più un primo punto per ogni inserzione. Scrivere ogni notte
> tutte le 35.000 inserzioni produrrebbe milioni di righe l'anno per rappresentare in gran parte
> prezzi fermi. È alimentata dalla sincronizzazione, che scarica già l'export completo: la
> rilevazione non costa una sola chiamata alle API, che sono la risorsa scarsa.
>
> Va distinta da `PriceChangeLogs`: quella registra **cosa ha deciso l'autopricer** e con quale
> motivazione, e porta anche il riferimento di mercato; questa registra **il prezzo effettivamente
> esposto**, chiunque lo abbia cambiato — autopricer, mano dell'utente o autopricer nativo di
> Card Trader.

```
> `PriceChangeLogs.InventoryItemId` è **nullable con `ON DELETE SET NULL`**: la riga di registro deve sopravvivere alla carta, altrimenti la cancellazione delle carte vendute durante la sincronizzazione notturna porterebbe via lo storico proprio dei casi su cui conviene verificare se il prezzo proposto era corretto. `InventoryItemId IS NULL` identifica le valutazioni di carte non più a magazzino; la carta resta riconoscibile da `BlueprintId`.

---

## Design Patterns Utilizzati

### 1. **Repository Pattern**
- Abstrazione dell'accesso ai dati
- `IInventoryItemRepository` con `IReadonlyRepository<T>`
- Eager loading con `Include()`

### 2. **Dependency Injection**
- Built-in .NET DI Container
- Scoped lifetime per DbContext
- Factory pattern per HttpClient (IHttpClientFactory)

### 3. **Factory Pattern**
- HttpClientFactory per Card Trader API

### 4. **Adapter Pattern**
- DTOs convertono risposte API in entità Domain
- CardTraderApiClient adatta l'API esterna

### 5. **SOLID Principles**

**Single Responsibility**: Ogni classe ha una responsabilità
- `CardTraderApiClient`: solo comunicazione API
- `InventoryItemRepository`: solo accesso ai dati
- Controllers: solo coordinamento HTTP

**Open/Closed**: Aperto per estensione, chiuso per modifica
- Nuovi marketplace aggiunti senza modificare codice esistente
- Pattern `/api/{marketplace}/...`

**Liskov Substitution**: Implementazioni intercambiabili
- Qualsiasi classe implementi `ICardTraderApiService` è usabile
- Repository pattern permette swap di implementazioni

**Interface Segregation**: Interfacce granulari
- `IReadonlyRepository<T>` separato da CRUD
- `ICardTraderApiService` contiene solo metodi necessari

**Dependency Inversion**: Dipendere da astrazioni
- Controller dipende da `ICardTraderApiService`, non dalla implementazione
- DbContext injected via interfaccia `IApplicationDbContext`

---

## Logging

Serilog configurato per:
- Console output
- File rolling (giornaliero, 14 file conservati in produzione)
- Enrichment con LogContext
- Structured logging per tutte le operazioni
- Request/response logging middleware

Log file: `logs/ecommerce-inventory-.txt`

**Percorsi relativi e servizio Windows**: un servizio eredita come cartella corrente `C:\Windows\System32`, non quella dell'eseguibile. `Program.cs` la riallinea a `AppContext.BaseDirectory` quando rileva di girare come servizio, prima di costruire l'host: senza, il sink su file — e ogni altro percorso relativo, backup compresi — verrebbe risolto in System32, dove l'account del servizio non può scrivere, e il sink fallirebbe in silenzio.

**Diagnostica**: Serilog scarta senza avvisare i sink che non riescono a inizializzarsi. `SelfLog` è abilitato su `serilog-selflog.txt` accanto all'eseguibile, ed è il primo posto dove guardare quando i log non compaiono.

**Livelli**: `Debug` in sviluppo, `Information` in produzione con `Override` a `Warning` sui namespace `Microsoft`, `Microsoft.EntityFrameworkCore` e `System`. Il livello di produzione non va alzato a `Warning`: il sink su file non crea il file finché non si verifica un evento di quel livello, e i riepiloghi delle operazioni in background (sincronizzazione, autopricer) sono a livello `Information` — alzarlo equivale a rinunciare alla diagnostica di ciò che gira di notte.

**Il sink File va dichiarato solo nei file per ambiente**, mai in `appsettings.json`: gli array di configurazione in .NET si fondono per indice e non si concatenano, quindi un sink nella base più uno nel file per ambiente producono due sink sullo stesso percorso, di cui il secondo non riesce ad acquisire il lock e ripiega su un file con suffisso `_001`.

---

## Sicurezza

- ✅ Bearer Token per Card Trader API (appsettings.json)
- ✅ SQL Server connection string sicura
- ✅ CORS configurato
- ✅ Input validation nei DTOs (request binding)
- ✅ Logging di errori sensibili

---

## Estensibilità

L'architettura è progettata per aggiungere facilmente nuovi marketplace:

1. Creare `Controllers/Marketplace/` folder
2. Creare `IMarketplaceApiService` interface
3. Implementare client HTTP in `ExternalServices/Marketplace/`
4. Aggiungere DTOs per risposte API
5. Registrare servizi in `Program.cs`
6. Definire endpoints specifici in controller

**Nessuna modifica necessaria al codice esistente** (Open/Closed Principle).

---

## Decisioni Tecniche Rilevanti

| Data | Decisione | Motivazione |
|------|-----------|-------------|
| 2025-11-18 | CQRS con MediatR solo per webhook processing | Webhook ha logica complessa event-driven; il resto usa Service+Repository più semplice |
| 2025-11-26 | Backend come Windows Service, Frontend su IIS | Ambiente locale Windows, avvio automatico, nessuna dipendenza da Docker |
| 2025-11-28 | Rate limiter outbound custom (`CardTraderRateLimiter`) | Card Trader API: 20 req/min — rischio IP ban senza throttling |
| 2025-12-22 | `RunAnalyticsDuringSync = false` di default | Expansion analytics durante sync notturna causava stalli (100+ chiamate API) |
| 2026-02-06 | Fetch marketplace prices per `expansion_id` (non per `blueprint_id[]`) | Card Trader non supporta batch per blueprint — una call per espansione è più efficiente |
| 2026-05-19 | Migrazioni applicate via SQL diretto quando `dotnet ef` è bloccato | API + VS lockano le DLL Infrastructure; workaround: SQL diretto + insert in `__EFMigrationsHistory` + aggiornamento snapshot manuale |
| 2026-03-26 | `TotaleAcquistato` nel report Tag da `PendingListings.PurchasePrice` | `InventoryItems.PurchasePrice` spesso zero; `PendingListings` è la source of truth del costo d'acquisto |
| 2026-08-28 | I lookup costruiti da `PendingListings` raggruppano per `CardTraderProductId` invece di usare `ToDictionary` diretto | Lo stesso prodotto compare su più `PendingListings` (ripubblicazioni, riallineamenti): la chiave duplicata solleva un'eccezione che aborte l'intera sezione di sincronizzazione prima ancora dell'upsert. Vince la registrazione con `CreatedAt` più recente |
| 2026-08-28 | Un fallimento di sezione marca l'intera sincronizzazione come fallita | Ogni sezione cattura le proprie eccezioni per non far cadere le altre; senza propagazione all'esito complessivo un fallimento totale risultava `success` in log e metriche |
| 2026-08-28 | `PriceChangeLog` sopravvive all'`InventoryItem` (`SET NULL`, non `CASCADE`) | Il registro delle valutazioni serve a verificare a posteriori se il prezzo proposto era corretto: cancellarlo insieme alla carta venduta ne annullerebbe lo scopo proprio sui casi più istruttivi. La carta resta identificabile da `BlueprintId` |
| 2026-08-29 | Le regole di pricing ragionano sui prezzi di vetrina, non su quelli incassati | `ListingPrice` viene dall'export ed è il netto venditore, mentre il marketplace espone prezzi comprensivi del sovrapprezzo di Card Trader: sono due scale diverse. La posizione fra i venditori è un fatto di vetrina, quindi si calcola lì e si riconverte solo alla fine, quando si deve scrivere |
| 2026-08-29 | Il fattore di conversione si ricava dalla propria offerta nel feed | Il sovrapprezzo non è documentato né proporzionale (osservato fra 0,76% e 1,35%). Dedurlo dalla propria inserzione lo rende esatto e autoaggiornante |
| 2026-08-29 | Collocazione percentuale al posto dell'ordinale, e mai sull'offerta più cara | La profondità di mercato varia da 3 a 29 offerte comparabili: un ordinale fisso degenera in "sii il più caro" sui mercati sottili |
| 2026-08-29 | Guardrail asimmetrico fra rialzo e ribasso | I due errori non costano uguale: il rialzo è reversibile alla prossima esecuzione, il ribasso si traduce in una vendita immediata |
| 2026-08-29 | Lo storico prezzi si alimenta dalla sincronizzazione, non dall'autopricer | La sync scarica già l'export completo, quindi la rilevazione è gratuita in termini di chiamate API e copre tutte le inserzioni, non solo quelle che l'autopricer tocca quella notte. Intercetta anche i cambi manuali |
| 2026-08-29 | Serie a delta invece che snapshot completo | Un magazzino di 35.000 inserzioni scritto per intero ogni notte produrrebbe circa 12 milioni di righe l'anno per rappresentare in gran parte prezzi fermi: per ricostruire un andamento basta sapere quando è cambiato |
| 2026-09-02 | Una sola esecuzione dell'autopricer alla volta, con `PricingRunCoordinator` singleton davanti a tutte | Il limite di 20 richieste al minuto verso Card Trader è condiviso da tutta l'applicazione: due esecuzioni in parallelo non vanno il doppio più veloci, si dimezzano a vicenda e rallentano anche le sincronizzazioni. Ci passa anche la notturna, altrimenti una manuale ancora in corso alle 03:30 si troverebbe la notturna addosso |
| 2026-09-02 | L'esecuzione a richiesta risponde `202` e prosegue in background, invece di restare aperta | Una esecuzione reale dura ore: nessuna richiesta HTTP resta viva tanto a lungo, e tenerla aperta legherebbe l'utente alla pagina che l'ha lanciata. Il token di annullamento non può essere quello della richiesta — verrebbe annullato appena il chiamante riceve la risposta — ma uno collegato ad `ApplicationStopping` |
| 2026-09-02 | L'avanzamento si legge da `PricingRunLog`, non da uno stato in memoria | I contatori erano già scritti a database a ogni blueprint valutato: leggerli da lì evita una migration, sopravvive a un ricaricamento della pagina e resta corretto anche se il browser si disconnette. In memoria resta solo la fase della preparazione, quando la riga non esiste ancora |
| 2026-09-02 | «Applica» dall'anteprima rivaluta invece di scrivere i prezzi calcolati | I prezzi mostrati a schermo arrivano dal browser, e l'API non deve fidarsene. Rivalutare costa le chiamate al marketplace una seconda volta — accettabile su un campione — e in cambio scrive su dati di mercato freschi |
| 2026-09-02 | «Applica» scavalca il dry-run del profilo, l'anteprima no | Sono due gesti diversi: l'applicazione riguarda carte appena esaminate una per una, ed è il modo di uscire dalla simulazione gradualmente senza attivare la scrittura sulla notturna. L'anteprima invece è lo strumento con cui si prova, e deve restare innocua per costruzione: `forceApply` non prevale mai su `forceDryRun` |

---

## Configurazione Richiesta

File/sezioni necessarie in `appsettings.json`:

```json
{
  "ConnectionStrings": { "DefaultConnection": "Server=...;Database=ECommerceInventory;..." },
  "JwtSettings": { "SecretKey": "***", "Issuer": "...", "Audience": "...", "ExpiryMinutes": 1440 },
  "CardTraderSettings": { "BaseUrl": "https://api.cardtrader.com/api/v2", "BearerToken": "***" },
  "SyncSettings": { "RunAnalyticsDuringSync": false },
  "BackupSettings": { "Enabled": true, "Schedule": "0 2 * * *", "RetentionDays": 3 },
  "Serilog": { ... }
}
```

> Segreti (`SecretKey`, `BearerToken`, password DB) in `appsettings.Production.json` sul server (non committato).
