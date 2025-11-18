# eCommerce.Inventory - Architettura

## Panoramica

eCommerce.Inventory è un sistema di gestione inventario modulare per piattaforme di commercio elettronico specializzate in carte collezionabili (TCG: Trading Card Games). L'architettura segue i principi di **Clean Architecture** con pattern **CQRS** (Command Query Responsibility Segregation) per separare le operazioni di lettura e scrittura.

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

  - `CardTraderWebhooksController.cs`: Webhook receiver
    - POST /api/cardtrader/webhooks/notification
    - Notifiche real-time da Card Trader

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
└─ GameId (FK → Games)

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
```

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
- File rolling (giornaliero)
- Enrichment con LogContext
- Structured logging per tutte le operazioni
- Request/response logging middleware

Log file: `logs/ecommerce-inventory-.txt`

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
