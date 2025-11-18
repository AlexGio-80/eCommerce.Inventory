# eCommerce.Inventory - Dettagli Implementativi

## Panoramica dello Sviluppo

Questo documento descrive le implementazioni sviluppate nel progetto eCommerce.Inventory.

---

## Fase 1: Domain Layer

### Entità Implementate

#### 1. **Game.cs**
```csharp
- Id: int (Primary Key)
- CardTraderId: int (ID su Card Trader)
- Name: string
- Code: string (Es. "mtg", "ygo")
- Expansions: ICollection<Expansion> (Navigation property)
```
**Responsabilità**: Rappresentare un gioco TCG nel sistema.

#### 2. **Expansion.cs**
```csharp
- Id: int (Primary Key)
- CardTraderId: int
- Name: string
- Code: string (Es. "MOM", "LTR")
- GameId: int (Foreign Key)
- Game: Game (Navigation)
- Blueprints: ICollection<Blueprint> (Navigation)
```
**Responsabilità**: Rappresentare un'espansione di un gioco.

#### 3. **Blueprint.cs**
```csharp
- Id: int (Primary Key)
- CardTraderId: int
- Name: string
- Version: string (Es. "Showcase", "Borderless")
- Rarity: string
- ExpansionId: int (Foreign Key)
- Expansion: Expansion (Navigation)
- InventoryItems: ICollection<InventoryItem> (Navigation)
```
**Responsabilità**: Definire la "matrice" di una carta.

#### 4. **InventoryItem.cs**
```csharp
- Id: int (Primary Key)
- CardTraderProductId: int? (nullable - solo se su Card Trader)
- BlueprintId: int (Foreign Key)
- Blueprint: Blueprint (Navigation)
- PurchasePrice: decimal (18,2)
- DateAdded: DateTime
- Quantity: int
- ListingPrice: decimal (18,2)
- Condition: string (Es. "Near Mint", "Played")
- Language: string
- IsFoil: bool
- IsSigned: bool
- Location: string (Es. "Scatola A")
- OrderItems: ICollection<OrderItem> (Navigation)
```
**Responsabilità**: Rappresentare un oggetto fisico nel mio inventario.

#### 5. **Order.cs**
```csharp
- Id: int (Primary Key)
- CardTraderOrderId: int
- DatePlaced: DateTime
- Status: string (Es. "Paid", "Shipped")
- TotalAmount: decimal (18,2)
- ShippingCost: decimal (18,2)
- OrderItems: ICollection<OrderItem> (Navigation)
```
**Responsabilità**: Rappresentare un ordine ricevuto da Card Trader.

#### 6. **OrderItem.cs**
```csharp
- Id: int (Primary Key)
- OrderId: int (Foreign Key)
- Order: Order (Navigation)
- InventoryItemId: int (Foreign Key)
- InventoryItem: InventoryItem (Navigation)
- QuantitySold: int
- PricePerItem: decimal (18,2)
```
**Responsabilità**: Rappresentare una riga di un ordine.

**Relazioni Configurate**:
- Game ← 1-to-Many → Expansions (Cascade Delete)
- Expansion ← 1-to-Many → Blueprints (Cascade Delete)
- Blueprint ← 1-to-Many → InventoryItems (Cascade Delete)
- Order ← 1-to-Many → OrderItems (Cascade Delete)
- InventoryItem ← 1-to-Many → OrderItems (Cascade Delete)

---

## Fase 2: Application Layer (Interfaces)

### IApplicationDbContext.cs
```csharp
DbSet<Game> Games { get; }
DbSet<Expansion> Expansions { get; }
DbSet<Blueprint> Blueprints { get; }
DbSet<InventoryItem> InventoryItems { get; }
DbSet<Order> Orders { get; }
DbSet<OrderItem> OrderItems { get; }

Task<int> SaveChangesAsync(CancellationToken cancellationToken);
```
**Scopo**: Astrazione del DbContext per testabilità.

### IReadonlyRepository<T>
```csharp
Task<T> GetByIdAsync(int id, CancellationToken cancellationToken);
Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken);
```
**Scopo**: Base generica per operazioni di lettura.

### IInventoryItemRepository
Estende `IReadonlyRepository<InventoryItem>` con:
```csharp
Task AddAsync(InventoryItem item, CancellationToken cancellationToken);
Task UpdateAsync(InventoryItem item, CancellationToken cancellationToken);
Task DeleteAsync(int id, CancellationToken cancellationToken);
Task<InventoryItem> GetByCardTraderProductIdAsync(int cardTraderProductId, CancellationToken cancellationToken);
```
**Scopo**: CRUD specifico per inventario.

### ICardTraderApiService
```csharp
Task<IEnumerable<Game>> SyncGamesAsync(CancellationToken cancellationToken);
Task<IEnumerable<Expansion>> SyncExpansionsAsync(CancellationToken cancellationToken);
Task<IEnumerable<Blueprint>> SyncBlueprintsForExpansionAsync(int expansionId, CancellationToken cancellationToken);
Task<int> CreateProductOnCardTraderAsync(InventoryItem item, CancellationToken cancellationToken);
Task UpdateProductOnCardTraderAsync(InventoryItem item, CancellationToken cancellationToken);
Task DeleteProductOnCardTraderAsync(int cardTraderProductId, CancellationToken cancellationToken);
Task<IEnumerable<Order>> FetchNewOrdersAsync(CancellationToken cancellationToken);
Task<IEnumerable<InventoryItem>> FetchMyProductsAsync(CancellationToken cancellationToken);
```
**Scopo**: Comunicazione con Card Trader API.

---

## Fase 3: Infrastructure Layer

### ApplicationDbContext.cs
**Implementa**: `IApplicationDbContext`

**Configurazione EF Core**:
- One-to-Many relazioni con Cascade Delete
- Decimal precision (18,2) per campi monetari
- Migrations Assembly: "eCommerce.Inventory.Infrastructure"
- SQL Server provider

**OnModelCreating**:
```csharp
Game → Expansions (1-to-Many, Cascade)
Expansion → Blueprints (1-to-Many, Cascade)
Blueprint → InventoryItems (1-to-Many, Cascade)
Order → OrderItems (1-to-Many, Cascade)
InventoryItem → OrderItems (1-to-Many, Cascade)

All Decimals: HasPrecision(18, 2)
```

### InventoryItemRepository.cs
**Implementa**: `IInventoryItemRepository`

**Metodi**:
- `GetByIdAsync(id)`: Ritorna item con Blueprint → Expansion → Game loaded
- `GetAllAsync()`: Ritorna tutti gli item con eager loading completo
- `AddAsync(item)`: Aggiunge e salva
- `UpdateAsync(item)`: Aggiorna e salva
- `DeleteAsync(id)`: Cancella per ID
- `GetByCardTraderProductIdAsync(productId)`: Ricerca per Card Trader Product ID

**Eager Loading Pattern**:
```csharp
Include(i => i.Blueprint)
  .ThenInclude(b => b.Expansion)
  .ThenInclude(e => e.Game)
```

### CardTraderApiClient.cs
**Implementa**: `ICardTraderApiService`

**Inizializzazione**:
- HttpClient injected via Factory
- Base URL e Bearer Token da appsettings.json
- Timeout: 30 secondi
- Default headers: Authorization, Accept application/json

**Metodi Implementati**:
1. `SyncGamesAsync()`:
   - GET `/games`
   - Parsing placeholder per CardTraderGameDto
   - Mapping a Game entities

2. `SyncExpansionsAsync()`:
   - GET `/expansions`
   - Parsing CardTraderExpansionDto
   - Mapping a Expansion entities

3. `SyncBlueprintsForExpansionAsync(expansionId)`:
   - GET `/expansions/{expansionId}/cards`
   - Parsing CardTraderBlueprintDto
   - Mapping a Blueprint entities

4. `CreateProductOnCardTraderAsync(item)`:
   - POST `/products`
   - Payload: blueprint_id, price, quantity, condition, language, foil, signed, user_data_field
   - Ritorna Card Trader Product ID

5. `UpdateProductOnCardTraderAsync(item)`:
   - PUT `/products/{cardTraderProductId}`
   - Aggiorna: price, quantity, condition, language, foil, signed, user_data_field

6. `DeleteProductOnCardTraderAsync(productId)`:
   - DELETE `/products/{productId}`

7. `FetchMyProductsAsync()`:
   - GET `/products`
   - Parsing CardTraderProductDto
   - Mapping a InventoryItem entities

8. `FetchNewOrdersAsync()`:
   - GET `/orders`
   - Parsing CardTraderOrderDto
   - Mapping a Order entities con OrderItems

**Error Handling**:
- Try-catch con logging
- EnsureSuccessStatusCode() per HTTP errors
- Structured logging di errori

### CardTraderSyncWorker.cs
**Implementa**: `BackgroundService`

**Configurazione**:
- Intervallo: 15 minuti
- Delay iniziale: 5 secondi (per lasciare time all'app di startare)

**Metodo ExecuteAsync**:
```csharp
while (!stoppingToken.IsCancellationRequested)
{
    await SyncCardTraderDataAsync(stoppingToken);
    await Task.Delay(_syncInterval, stoppingToken);
}
```

**SyncCardTraderDataAsync**:
1. Crea scope per DI
2. Sincronizza: Games, Expansions
3. Fetch: My Products, New Orders
4. Merge con database (placeholder)
5. Logging completo

**Error Handling**:
- Try-catch outer loop
- Continua loop anche con errori
- Log errors, retry next interval

### DTOs Card Trader
Modelli per deserializzazione:
- `CardTraderGameDto`: id, name, abbreviation
- `CardTraderExpansionDto`: id, name, abbreviation, gameId
- `CardTraderBlueprintDto`: id, name, imageUrl, rarity, expansionId
- `CardTraderProductDto`: id, blueprintId, price, quantity, condition, language, isFoil, isSigned, userDataField, updatedAt
- `CardTraderOrderDto`: id, createdAt, state, total, shippingPrice, items[]

---

## Fase 4: API Layer

### Program.cs - Configurazione

**1. Serilog**:
```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/ecommerce-inventory-.txt", rollingInterval: RollingInterval.Day)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();
```

**2. DbContext**:
```csharp
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
        sqlOptions.MigrationsAssembly("eCommerce.Inventory.Infrastructure")));
```

**3. Repository**:
```csharp
builder.Services.AddScoped<IInventoryItemRepository, InventoryItemRepository>();
builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
```

**4. HttpClient for Card Trader**:
```csharp
builder.Services.AddHttpClient<ICardTraderApiService, CardTraderApiClient>(client =>
{
    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearerToken}");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

**5. Background Service**:
```csharp
builder.Services.AddHostedService<CardTraderSyncWorker>();
```

**6. CORS**:
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
```

**7. Middleware**:
```csharp
app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseSerilogRequestLogging();
app.MapControllers();
```

### Controllers

#### CardTraderInventoryController
- **Route**: `/api/cardtrader/inventory`
- **GET /**: List all inventory items
- **GET /{id}**: Get item by ID
- **POST /**: Create new item (accepts CreateInventoryItemRequest)
- **PUT /{id}**: Update item (accepts UpdateInventoryItemRequest)
- **DELETE /{id}**: Delete item

**Requests**:
```csharp
CreateInventoryItemRequest:
- BlueprintId: int
- PurchasePrice: decimal
- Quantity: int
- ListingPrice: decimal
- Condition: string
- Language: string
- IsFoil: bool
- IsSigned: bool
- Location: string

UpdateInventoryItemRequest:
- ListingPrice?: decimal
- Quantity?: int
- Condition?: string
- Location?: string
```

#### CardTraderWebhooksController
- **Route**: `/api/cardtrader/webhooks`
- **POST /notification**: Receive Card Trader webhook
  - Accepts `CardTraderWebhookNotification`
  - Placeholder: ProcessCardTraderWebhookCommand
  - Types: order.placed, order.paid, order.shipped, product.updated, etc.

#### CardTraderSyncController
- **Route**: `/api/cardtrader/sync`
- **POST /games**: Sync games
- **POST /expansions**: Sync expansions
- **POST /blueprints**: Sync blueprints (query param: expansionId)
- **POST /products**: Fetch my products
- **POST /orders**: Fetch orders
- **POST /full**: Full sync (all of above)

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=eCommerceInventory;Trusted_Connection=True;..."
  },
  "CardTraderApi": {
    "BaseUrl": "https://api.cardtrader.com/api/v2",
    "BearerToken": "YOUR_TOKEN_HERE"
  }
}
```

---

## Flussi Principali

### 1. Creazione Nuovo Item
```
Client (POST /api/cardtrader/inventory)
  ↓
CardTraderInventoryController.AddInventoryItem()
  ↓
InventoryItemRepository.AddAsync()
  ↓
ApplicationDbContext.SaveChangesAsync()
  ↓
SQL Server Insert
  ↓
201 Created
```

### 2. Sincronizzazione Card Trader (Background)
```
CardTraderSyncWorker (ogni 15 minuti)
  ↓
CardTraderApiClient.SyncGamesAsync() → Games update
  ↓
CardTraderApiClient.SyncExpansionsAsync() → Expansions update
  ↓
CardTraderApiClient.FetchMyProductsAsync() → InventoryItems sync
  ↓
CardTraderApiClient.FetchNewOrdersAsync() → Orders sync
  ↓
ApplicationDbContext.SaveChangesAsync()
  ↓
SQL Server Updates
  ↓
Logging risultati
```

### 3. Ricezione Webhook
```
Card Trader API
  ↓
POST /api/cardtrader/webhooks/notification
  ↓
CardTraderWebhooksController.ReceiveCardTraderNotification()
  ↓
Validation
  ↓
ProcessCardTraderWebhookCommand (TODO)
  ↓
200 OK
```

---

## Logging

Tutti i servizi utilizzano `ILogger<T>`:

```csharp
_logger.LogInformation("Message with {Param}", param);
_logger.LogWarning("Warning");
_logger.LogError(ex, "Error occurred");
```

Log File: `logs/ecommerce-inventory-YYYYMMDD.txt`

---

## Testing Strategy (TODO)

- Unit tests per entità Domain
- Mock tests per Repository (InMemory EF Core)
- Mock tests per CardTraderApiClient (HttpClientFactory)
- Integration tests per controller
- E2E tests per workflow completi

---

## Performance Considerations

- ✅ Eager loading per evitare N+1 queries
- ✅ Async/await per non bloccare thread
- ✅ Scoped DI per DbContext
- ✅ Background worker per sync non-blocking
- ✅ Http timeout configurato (30s)
- TODO: Implement pagination
- TODO: Add caching (Redis)
- TODO: Add Polly resilience policies

---

## Sicurezza Implementata

- ✅ Bearer Token per API (in appsettings, non in codice)
- ✅ CORS configurato (attualmente permissivo - limitare in prod)
- ✅ SQL Server connection string sicura
- ✅ Serilog non loga dati sensibili
- TODO: API key validation
- TODO: Rate limiting
- TODO: Input validation
