# Progetto: Gestione Inventario TCG - Taffel
## Stack Tecnologico
- **Backend:** .NET 8 Web API
- **Database:** SQL Server
- **ORM:** Entity Framework Core 8
- **Architettura:** Clean Architecture

## Requisiti Chiave
- Aderire ai principi SOLID.
- Usare massicciamente la Dependency Injection (DI).
- Implementare un sistema di logging con Serilog.
- Usare `appsettings.json` per stringhe di connessione e chiavi API.
- Creare un servizio in background per il polling periodico delle API di Card Trader.
- Esporre un endpoint Webhook per ricevere notifiche in tempo reale da Card Trader.
- Tracciare i prezzi di acquisto per future analisi di Business Intelligence.

## Struttura del Progetto

### 1. `Taffel.Inventory.Domain`
- **Entities:**
  - `Game.cs`: Rappresenta un gioco (Magic, YGO, etc.). Proprietà: `Id`, `CardTraderId`, `Name`, `Code`.
  - `Expansion.cs`: Rappresenta un'espansione. Proprietà: `Id`, `CardTraderId`, `Name`, `Code`, `GameId`.
  - `Blueprint.cs`: Rappresenta la "matrice" di una carta. Proprietà: `Id`, `CardTraderId`, `Name`, `Version`, `Rarity`, `ExpansionId`.
  - `InventoryItem.cs`: Rappresenta un OGGETTO nel mio inventario. Proprietà: `Id`, `CardTraderProductId` (nullable), `BlueprintId`, `PurchasePrice`, `DateAdded`, `Quantity`, `ListingPrice`, `Condition`, `Language`, `IsFoil`, `IsSigned`, `Location`.
  - `Order.cs`: Rappresenta un ordine ricevuto. Proprietà: `Id`, `CardTraderOrderId`, `DatePlaced`, `Status`, `TotalAmount`, `ShippingCost`.
  - `OrderItem.cs`: Rappresenta una riga di un ordine. Proprietà: `Id`, `OrderId`, `InventoryItemId`, `QuantitySold`, `PricePerItem`.

### 2. `Taffel.Inventory.Application`
- **Interfaces:**
  - `IApplicationDbContext.cs`: Interfaccia per il DbContext.
  - `IInventoryItemRepository.cs`: Interfaccia per le operazioni CRUD sugli `InventoryItem`.
  - `IReadonlyRepository<T>.cs`: Interfaccia generica per la lettura di entità di supporto come `Game`, `Expansion`, `Blueprint`.
  - `ICardTraderApiService.cs`: Interfaccia per la comunicazione con le API di Card Trader. Metodi:
    - `Task<IEnumerable<Game>> SyncGamesAsync();`
    - `Task<IEnumerable<Expansion>> SyncExpansionsAsync();`
    - `Task<IEnumerable<Blueprint>> SyncBlueprintsForExpansionAsync(int expansionId);`
    - `Task<int> CreateProductOnCardTraderAsync(InventoryItem item);` // Restituisce il CardTraderProductId
    - `Task UpdateProductOnCardTraderAsync(InventoryItem item);`
    - `Task DeleteProductOnCardTraderAsync(int cardTraderProductId);`
    - `Task<IEnumerable<Order>> FetchNewOrdersAsync();`
    - `Task<IEnumerable<InventoryItem>> FetchMyProductsAsync();`

- **Features (CQRS):**
  - **Commands:**
    - `AddInventoryItemCommand.cs`: Aggiunge un nuovo item al DB e opzionalmente lo mette in vendita su Card Trader.
    - `UpdateInventoryItemCommand.cs`: Aggiorna un item nel DB e su Card Trader.
    - `ProcessCardTraderWebhookCommand.cs`: Gestisce la logica di una notifica ricevuta dal webhook.
  - **Queries:**
    - `GetInventoryItemByIdQuery.cs`
    - `GetAllInventoryItemsQuery.cs`
    - `GetProfitabilityReportQuery.cs`: Calcola il profitto basandosi su `PurchasePrice` e dati degli `OrderItem`.

### 3. `Taffel.Inventory.Infrastructure`
- **Persistence (EF Core):**
  - `ApplicationDbContext.cs`: Implementazione di `IApplicationDbContext`. Configura le relazioni tra le entità sopra definite.
  - `Repositories/InventoryItemRepository.cs`: Implementazione di `IInventoryItemRepository`.
- **External Services/HttpClients:**
  - `CardTrader/CardTraderApiClient.cs`: Implementazione di `ICardTraderApiService`. Utilizza `IHttpClientFactory` per creare un client HTTP configurato con l'autenticazione Bearer Token per Card Trader. Gestisce la serializzazione/deserializzazione dei DTO.
- **Background Services:**
  - `CardTraderSyncWorker.cs`: Un `BackgroundService` che:
    1.  A intervalli regolari, chiama `ICardTraderApiService.FetchMyProductsAsync()` e `FetchNewOrdersAsync()` per sincronizzare lo stato dei prodotti e degli ordini con il database locale.
    2.  Potrebbe avere un task per sincronizzare nuovi `Blueprint` ed `Expansion`.

### 4. `Taffel.Inventory.Api` (Progetto Web API)
- **Controllers:**
  - `InventoryController.cs`: Endpoint RESTful per gestire l'inventario (`GET`, `POST`, `PUT` per `InventoryItem`).
  - `SyncController.cs`: Endpoint per avviare manualmente i processi di sincronizzazione (es. `POST /api/sync/blueprints?expansionId=123`).
  - `WebhooksController.cs`:
    - `POST /api/webhooks/cardtrader`: Endpoint che riceve le notifiche da Card Trader. La richiesta viene validata e poi passata al `ProcessCardTraderWebhookCommand` per essere gestita in modo asincrono.
- **Startup/Program.cs:**
  - Configurazione della DI.
  - Configurazione di EF Core con la stringa di connessione.
  - Configurazione di Serilog.
  - Registrazione del `CardTraderSyncWorker` come Hosted Service.
  - Configurazione del client HTTP per Card Trader.
- **appsettings.json:**
  ```json
  {
    "ConnectionStrings": {
      "DefaultConnection": "Server=...;Database=TaffelInventory;User Id=...;Password=...;TrustServerCertificate=True;"
    },
    "CardTraderApi": {
      "BaseUrl": "https://api.cardtrader.com/api/v2",
      "BearerToken": "IL_TUO_TOKEN_JWT_QUI"
    },
    ...
  }
  ```

## Istruzioni per l'Agente AI
1.  Crea una soluzione .NET 8 con i 4 progetti descritti.
2.  Implementa le classi entità nel progetto Domain, includendo le relazioni virtuali per la navigation di EF Core.
3.  Nel progetto Application, definisci le interfacce per i repository e il servizio API.
4.  Nel progetto Infrastructure, implementa il `DbContext` configurando le relazioni (es. one-to-many tra Expansion e Blueprint). Implementa lo scheletro del `CardTraderApiClient`.
5.  Nel progetto Api, crea i controller `InventoryController` e `WebhooksController`. Configura la DI in `Program.cs` per tutti i servizi, repository e per l'HttpClient di Card Trader.
6.  Aggiungi un middleware per il logging di tutte le richieste HTTP in entrata e in uscita.