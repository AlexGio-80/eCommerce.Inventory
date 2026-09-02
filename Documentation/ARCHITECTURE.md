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
- **Entities/** — catalogo e magazzino
  - `Game.cs`: Rappresenta un gioco TCG (Magic, YGO, etc.)
  - `Expansion.cs`: Espansione di un gioco, con configurazione e prezzo del box
  - `Blueprint.cs`: Matrice di una carta (definizione), con nome italiano e id Scryfall
  - `Category.cs`, `Property.cs`, `PropertyValue.cs`: tassonomia Card Trader
  - `SealedCategoryIds.cs`: quali categorie sono prodotto sigillato, per gioco
  - `InventoryItem.cs`: Oggetto nell'inventario
  - `PendingListing.cs`: Inserzione compilata dalla maschera, in attesa di pubblicazione su Card Trader. È la fonte del costo d'acquisto e del tag, che Card Trader non espone
  - `ExpansionROI.cs`: vista di sola lettura sulla redditività per espansione
- **Entities/** — ordini e utenti
  - `Order.cs`: Ordine ricevuto da un marketplace
  - `OrderItem.cs`: Riga di un ordine
  - `User.cs`: Utente applicativo (autenticazione JWT)
- **Entities/** — autopricer
  - `PricingProfile.cs`: Modalità, guardrail, filtri sui venditori e criteri di comparabilità
  - `PricingRule.cs`: Regola per fascia di prezzo (riferimento, collocazione, scostamento)
  - `PricingRunLog.cs`: Riepilogo di una esecuzione, con i contatori per esito
  - `PriceChangeLog.cs`: Una riga per carta valutata, con esito e motivazione
  - `PriceHistoryEntry.cs`: Serie storica del prezzo effettivamente esposto

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
  - `IInventoryItemRepository.cs`, `IBlueprintRepository.cs`, `IOrderRepository.cs`: repository specifici
  - `ICardTraderApiService.cs`: Comunicazione con API Card Trader
  - `IAuthService.cs`: Autenticazione e cambio password
  - `ICacheService.cs`: Cache dei dati statici Card Trader (implementazione Redis, oggi disattivata)
  - `INotificationService.cs`: Notifiche verso il frontend (SignalR)
  - `IExpansionAnalyticsService.cs`, `IGradingService.cs`
  - `IPriceRefreshQueue.cs`: Coda dei blueprint da riprezzare fuori dall'esecuzione notturna — vendite e nuove inserzioni. Chi accoda risponde subito, il consumo avviene in background
  - `IPricingRunCoordinator.cs`: Tiene **una sola** esecuzione dell'autopricer per volta e la porta avanti fuori dal ciclo di richiesta HTTP
- **Pricing/** — logica di prezzo pura, senza dipendenze da rete o database
  - `PricingEngine.cs`: Decide il prezzo di una carta date le offerte comparabili
  - `PricingDecision.cs`: Esito della valutazione, con la motivazione
  - `PriceHistoryRecorder.cs`: Decide quali rilevazioni vale la pena registrare (serie a delta)
- **Metrics/BusinessMetrics.cs**: Metriche Prometheus di dominio

**Caratteristiche**:
- ✅ Dipende solo da Domain
- ✅ Le interfacce non contengono implementazioni; `Pricing/` sì, ma è logica pura e testabile senza infrastruttura

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

  - `CardTraderSyncOrchestrator.cs`: Orchestrazione della sincronizzazione completa
    - Ogni sezione (games, expansions, blueprints, inventario, ordini) cattura le proprie
      eccezioni per non far cadere le altre, ma un fallimento di sezione marca comunque
      l'intera esecuzione come fallita
  - **DTOs/**: Modelli per deserializzazione risposte API
    - `CardTraderGameDto.cs`
    - `CardTraderExpansionDto.cs`
    - `CardTraderBlueprintDto.cs`
    - `CardTraderProductDto.cs`
    - `CardTraderOrderDto.cs`
- **Scryfall/ScryfallApiClient.cs**: Icone e date di rilascio delle espansioni, nomi italiani
- **MtgJson/MtgJsonClient.cs**: Fonte primaria dei nomi italiani (copertura molto più ampia di Scryfall); match su `identifiers.scryfallId`

#### Services
- `AutoPricingService.cs`: Orchestra l'autopricer — seleziona le carte, recupera le offerte, invoca il motore, scrive su Card Trader e registra ogni valutazione
- `PricingRunCoordinator.cs`: Implementa `IPricingRunCoordinator`. **Singleton**: lo slot occupato dev'essere lo stesso per tutti
- `PriceRefreshQueue.cs`: Coda in memoria dei blueprint da riprezzare. In memoria è sufficiente, perché una richiesta persa per un riavvio viene comunque recuperata dall'esecuzione notturna
- `AuthService.cs`, `BackupService.cs`, `ExpansionAnalyticsService.cs`, `RedisCacheService.cs`, `XimilarGradingService.cs`

#### BackgroundJobs

| Servizio | Quando gira | Interruttore |
|----------|-------------|--------------|
| `ScheduledProductSyncWorker` | Ogni notte all'orario configurato (default 03:00) | `SyncSettings:ProductSyncTime` |
| `AutoPricingWorker` | Ogni notte dopo la sincronizzazione (default 03:30) | `AutoPricing:Enabled` |
| `PriceRefreshWorker` | In continuo, consuma `IPriceRefreshQueue` | Decide chi accoda: `AutoPricing:RepriceOnOrder`, `AutoPricing:RepriceOnListingSync` |
| `PopulateItalianNamesService` | One-shot all'avvio | `SyncSettings:PopulateItalianNamesOnStartup` |
| `SealedProductPriceService` | One-shot all'avvio | `SyncSettings:PopulateSealedPricesOnStartup` |
| `BackupService` | Giornaliero | `BackupSettings:Enabled` |

> `AutoPricingWorker` non esegue da sé: passa da `IPricingRunCoordinator` come l'esecuzione
> manuale e l'applicazione dall'anteprima. Se una manuale è ancora in corso all'orario previsto,
> la notturna lo registra a log e salta invece di sovrapporsi — il limite di 20 richieste al
> minuto verso Card Trader è condiviso, e due esecuzioni in parallelo si dimezzano a vicenda.

> **⚠️ I due servizi one-shot terminano con `Environment.Exit(0)`.** La guardia sul flag precede
> l'uscita, quindi con flag `false` sono innocui — ma non vanno mai abilitati in
> `appsettings.Production.json`, pena lo spegnimento del servizio Windows a ogni avvio. Si
> lanciano dagli endpoint dedicati (es. `POST /api/expansions/sync-sealed-prices`).

> `CardTraderSyncWorker` (polling ogni 15 minuti) esiste ancora nel codice ma **è disattivato**:
> la registrazione in `Program.cs` è commentata. La sincronizzazione periodica la fa
> `ScheduledProductSyncWorker`, una volta a notte, perché il polling frequente consumava
> chiamate API senza che i dati di catalogo cambiassero con quella frequenza.

**Caratteristiche**:
- ✅ Dipende da Domain e Application
- ✅ Contiene tutte le implementazioni concrete
- ✅ HttpClient configurato con Bearer Token e rate limiter condiviso (20 req/min)
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

- **Controllers/CardTrader/** — routing specifico del marketplace
  - `CardTraderInventoryController.cs`: CRUD per inventario
  - `CardTraderBlueprintsController.cs`: Ricerca blueprint (nome, espansione, collector number, nome italiano)
  - `CardTraderOrdersController.cs`: Ordini, articoli da preparare, backfill dei tag
  - `CardTraderSyncController.cs`: Operazioni di sincronizzazione
  - `CardTraderSeedingController.cs`: Popolamento iniziale del catalogo

  > **⚠️ `POST /api/cardtrader/sync/products` e `/orders` non scrivono a database**: recuperano
  > i dati e restituiscono un conteggio. La sincronizzazione reale è `POST /api/cardtrader/sync`
  > con i flag della sezione desiderata.

- **Controllers/** — resto dell'applicazione
  - `AuthController.cs`: Login e cambio password. La registrazione non esiste
  - `CardTraderWebhooksController.cs`: `POST /api/cardtraderwebhooks/events`, notifiche `order.create/update/destroy`, firma HMAC via `WebhookSignatureVerificationService`
  - `AutoPricingController.cs`: `/api/pricing` — profili e regole, anteprima, esecuzione, applicazione, storico, copertura
  - `PendingListingsController.cs`: Inserzioni compilate dalla maschera e loro pubblicazione su Card Trader
  - `InventoryController.cs`, `GamesController.cs`, `ExpansionsController.cs`
  - `ReportingController.cs`: Endpoint di reporting (query SQL pesanti)
  - `GradingController.cs`: AI grading (oggi su servizio mock)

- **Hubs/NotificationHub.cs**: SignalR, avanzamento delle sincronizzazioni e notifiche ordini
- **HealthChecks/**: `/health` — database, Card Trader, Redis
- **appsettings.json**: Configurazione senza segreti (i segreti stanno in `appsettings.Production.json`, non committato)

**Caratteristiche**:
- ✅ RESTful endpoints, risposte incapsulate in `ApiResponse<T>`
- ✅ Marketplace-specific routing (`/api/cardtrader/...`)
- ✅ Swagger/OpenAPI documentation
- ✅ Serilog request/response logging con Correlation ID
- ✅ **Chiusa per difetto**: criterio globale che richiede utente autenticato con ruolo `Admin`. Le eccezioni sono dichiarate una per una con `[AllowAnonymous]` — login, webhook, `/health`, `/health-ui`, `/metrics`, hub SignalR

---

## Flusso Dati

```
┌─────────────────────────────────────────────────────────────┐
│                    Sistemi esterni                          │
│  Card Trader API (20 req/min) | Scryfall | MTGJSON          │
│  SQL Server | Webhook Card Trader                           │
└─────────────────────────────────────────────────────────────┘
                        ↕
┌─────────────────────────────────────────────────────────────┐
│              Infrastructure Layer                           │
│  CardTraderApiClient | CardTraderSyncOrchestrator           │
│  ScryfallApiClient | MtgJsonClient                          │
│  DbContext | Repositories | DTOs                            │
│  AutoPricingService | PricingRunCoordinator                 │
│  PriceRefreshQueue | AuthService | BackupService            │
│  BackgroundJobs: ScheduledProductSync | AutoPricing         │
│                 PriceRefresh | Backup | one-shot            │
└─────────────────────────────────────────────────────────────┘
                        ↕
┌─────────────────────────────────────────────────────────────┐
│        Application Layer (interfacce + logica pura)         │
│  IApplicationDbContext | I*Repository                       │
│  ICardTraderApiService | ICacheService | IAuthService        │
│  IPriceRefreshQueue | IPricingRunCoordinator                │
│  Pricing: PricingEngine | PriceHistoryRecorder              │
│  Metrics: BusinessMetrics                                   │
└─────────────────────────────────────────────────────────────┘
                        ↕
┌─────────────────────────────────────────────────────────────┐
│                Domain Layer (Entities)                      │
│  Game | Expansion | Blueprint | Category                    │
│  InventoryItem | PendingListing | Order | OrderItem | User   │
│  PricingProfile | PricingRule | PricingRunLog               │
│  PriceChangeLog | PriceHistoryEntry                         │
└─────────────────────────────────────────────────────────────┘
                        ↕
┌─────────────────────────────────────────────────────────────┐
│                   API Layer (Controllers)                   │
│  Auth | AutoPricing | PendingListings | Inventory           │
│  Games | Expansions | Reporting | Grading                   │
│  CardTrader/: Inventory | Blueprints | Orders | Sync         │
│  CardTraderWebhooks | NotificationHub | HealthChecks         │
└─────────────────────────────────────────────────────────────┘
                        ↕
┌─────────────────────────────────────────────────────────────┐
│                        Client                               │
│  Frontend Angular (IIS) | Webhook Card Trader | Prometheus  │
└─────────────────────────────────────────────────────────────┘
```

> Il Domain non dipende da nulla; l'Application dipende solo dal Domain; Infrastructure e API
> dipendono da entrambi. Le frecce qui sopra indicano il **flusso dei dati**, non la direzione
> delle dipendenze, che punta sempre verso il centro.

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
├─ ItalianName                -- popolato da MTGJSON, fallback Scryfall; usato dalla ricerca
├─ Version
├─ Rarity, CategoryId
├─ FixedProperties / EditableProperties (JSON)  -- il collector number sta qui
├─ ScryfallId, CardMarketIds, TcgPlayerId
├─ ImageUrl, BackImageUrl
├─ GameId (FK → Games)
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
├─ IsSigned, IsAltered
├─ Location
├─ Tag                        -- organizzazione interna, inviato anche a Card Trader
└─ Description                -- descrizione visibile sull'inserzione Card Trader

PendingListings               -- inserzione compilata dalla maschera, poi pubblicata su CT
├─ Id (PK)
├─ BlueprintId (FK → Blueprints)
├─ InventoryItemId (FK → InventoryItems, nullable)
├─ CardTraderProductId        -- nullable, valorizzato dopo la pubblicazione
├─ Quantity, SellingPrice, PurchasePrice
├─ Condition, Language, IsFoil, IsSigned
├─ Tag, Description
├─ IsUpdate                   -- distingue creazione e modifica di un'inserzione esistente
├─ IsSynced, SyncedAt, SyncError
└─ Grading* (score, condizione, sotto-punteggi)  -- oggi da servizio mock

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
├─ MinPrice
├─ MaxIncreasePercentPerRun / MaxDecreasePercentPerRun  ← guardrail asimmetrico
├─ MaxMedianRatio                 -- scarta i prezzi di comodo anche con poche offerte
├─ scarto anomalie (EnableOutlierRejection, OutlierMadThreshold, MinOffersForOutlierRejection)
├─ MinComparableOffers, SkipWhenFewerOffersThanPosition
├─ filtri venditore (IncludeProSellers, IncludeNormalSellers, ExcludeVacationSellers,
│                    MinSellerDailyCapacity, CountryCodesCsv)
└─ criteri di comparabilità (MatchCondition, MatchLanguage, MatchFoil,
                             ExcludeSigned, ExcludeAltered, ExcludeGraded)

PricingRules
├─ Id (PK)
├─ PricingProfileId (FK → PricingProfiles, CASCADE)
├─ FromPrice / ToPrice (fascia di applicazione)
├─ ReferenceMode / Position / Percentile
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

> `PendingListings` è la **fonte del costo d'acquisto e del tag**: Card Trader non li espone,
> quindi la sincronizzazione notturna li recupera da qui, altrimenti li azzererebbe
> sull'`InventoryItem`. Lo stesso `CardTraderProductId` può comparire su più righe
> (ripubblicazioni, riallineamenti): i lookup vanno costruiti raggruppando e tenendo la
> registrazione più recente, non con un `ToDictionary` diretto — la chiave duplicata solleva
> un'eccezione, ed è così che la sincronizzazione dell'inventario è rimasta ferma otto mesi.

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
