# eCommerce.Inventory - Roadmap & Prossimi Passi

## Status Attuale

✅ **Completato**:
- Architettura Clean Architecture a 4 strati
- Entità Domain (Game, Expansion, Blueprint, InventoryItem, Order, OrderItem)
- DbContext EF Core con relazioni configurate
- Repository pattern per accesso dati
- Interfacce per dependency injection
- CardTrader API client (skeleton)
- CardTrader background sync worker (skeleton)
- API controllers marketplace-specific
- Serilog logging configuration
- Dependency injection setup
- **Phase 1: Database & Migrations** ✅
  - Initial migration `20251118071405_InitialCreate` creata e applicata
  - Database `ECommerceInventory` creato su `DEV-ALEX\MSSQLSERVER01`
  - Schema con 6 tabelle (Games, Expansions, Blueprints, InventoryItems, Orders, OrderItems)
  - Seed data per Games (MTG, Yu-Gi-Oh, Pokémon), Expansions (2 per game), Blueprints (5 per expansion)
  - 5 InventoryItems di esempio con prezzi realistici
  - Tutti gli indici e FK configurati con cascade delete

🔨 **In Progress**: Phase 2 - Card Trader API Integration

⏳ **TODO**: Tutti i prossimi passi (Phase 2+)

---

## Phase 1: Database & Migrations (PRIORITY: HIGH)

### 1.1 Create Initial Migration
```bash
cd eCommerce.Inventory.Api
dotnet ef migrations add InitialCreate --project ../eCommerce.Inventory.Infrastructure
dotnet ef database update
```

**Outcome**: Schema completo su SQL Server

### 1.2 Seed Data (Optional)
Aggiungere dati di test per:
- Games (MTG, YGO, Pokemon)
- Expansions (almeno 2 per gioco)
- Blueprints (almeno 5 per espansione)

**File**: `Infrastructure/Persistence/SeedData.cs`

**Timeline**: 30 minuti

---

## Phase 2: Card Trader API Integration (PRIORITY: HIGH)

### 2.1 Parse DTOs → Domain Entities

Implementare conversione dal DTO alla entità:

```csharp
// In CardTraderApiClient.cs
private List<Game> MapGames(List<CardTraderGameDto> dtos)
{
    return dtos.Select(d => new Game
    {
        CardTraderId = d.Id,
        Name = d.Name,
        Code = d.Abbreviation
    }).ToList();
}
```

**Apply to**:
- Games, Expansions, Blueprints
- Products → InventoryItems
- Orders → Order + OrderItems

**File**: `Infrastructure/ExternalServices/CardTrader/Mappers/` (nuova cartella)

**Timeline**: 2 ore

### 2.2 Database Merge Logic

Quando sincronizziamo dal marketplace, gestire:
- **INSERT**: Nuovo item non in DB
- **UPDATE**: Item già in DB, aggiornare
- **DELETE**: Item in DB ma non su marketplace

```csharp
public async Task SyncProductsAsync(List<CardTraderProductDto> dtos)
{
    var existingProducts = await _context.InventoryItems
        .Where(i => i.CardTraderProductId.HasValue)
        .ToListAsync();

    foreach (var dto in dtos)
    {
        var existing = existingProducts.FirstOrDefault(p => p.CardTraderProductId == dto.Id);

        if (existing == null)
            _context.InventoryItems.Add(MapToEntity(dto));
        else
            UpdateEntity(existing, dto);
    }

    await _context.SaveChangesAsync();
}
```

**Files**:
- `Infrastructure/ExternalServices/CardTrader/CardTraderApiClient.cs` (extend)
- `Infrastructure/Services/InventorySyncService.cs` (nuovo)

**Timeline**: 2 ore

### 2.3 Complete CardTraderSyncWorker

Implementare la logica nel `SyncCardTraderDataAsync()`:

```csharp
private async Task SyncCardTraderDataAsync(CancellationToken cancellationToken)
{
    // 1. Sync Games & Expansions
    var games = await _cardTraderApiService.SyncGamesAsync(cancellationToken);
    var gameEntities = _mapper.MapGames(games);
    await _inventoryRepository.BulkInsertOrUpdateAsync(gameEntities);

    // 2. Sync Products
    var products = await _cardTraderApiService.FetchMyProductsAsync(cancellationToken);
    await _syncService.SyncProductsAsync(products);

    // 3. Sync Orders
    var orders = await _cardTraderApiService.FetchNewOrdersAsync(cancellationToken);
    await _syncService.SyncOrdersAsync(orders);

    _logger.LogInformation("Full sync completed successfully");
}
```

**Timeline**: 1 ora

### 2.4 Webhook Processing (ProcessCardTraderWebhookCommand)

Implementare CQRS Command per webhook:

```csharp
public class ProcessCardTraderWebhookCommand : IRequest<Unit>
{
    public string Type { get; set; }
    public object Data { get; set; }
}

public class ProcessCardTraderWebhookHandler : IRequestHandler<ProcessCardTraderWebhookCommand, Unit>
{
    public async Task<Unit> Handle(ProcessCardTraderWebhookCommand request, CancellationToken ct)
    {
        switch (request.Type)
        {
            case "order.placed":
                await HandleNewOrder((OrderData)request.Data, ct);
                break;
            case "order.paid":
                await HandleOrderPaid((OrderData)request.Data, ct);
                break;
            case "product.updated":
                await HandleProductUpdated((ProductData)request.Data, ct);
                break;
        }
        return Unit.Value;
    }
}
```

**Framework**: MediatR (aggiungere NuGet)

**Timeline**: 2 ore

---

## Phase 3: API Controller Enhancement (PRIORITY: MEDIUM)

### 3.1 Response Envelopes

Standardizzare le risposte:

```csharp
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T Data { get; set; }
    public string Message { get; set; }
    public List<string> Errors { get; set; }
}
```

Uso:
```csharp
[HttpGet("{id}")]
public async Task<ActionResult<ApiResponse<InventoryItem>>> GetById(int id)
{
    var item = await _repository.GetByIdAsync(id);
    if (item == null)
        return NotFound(new ApiResponse<InventoryItem>
        {
            Success = false,
            Message = "Item not found"
        });

    return Ok(new ApiResponse<InventoryItem>
    {
        Success = true,
        Data = item
    });
}
```

**File**: `Api/Models/ApiResponse.cs` (nuovo)

**Timeline**: 1 ora

### 3.2 Pagination Support

Aggiungere a `CardTraderInventoryController`:

```csharp
[HttpGet]
public async Task<ActionResult<PagedResponse<InventoryItem>>> GetAllPaged(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 10)
{
    var items = await _repository.GetPagedAsync(page, pageSize);
    return Ok(new PagedResponse<InventoryItem>
    {
        Items = items,
        Page = page,
        PageSize = pageSize,
        TotalCount = await _repository.GetCountAsync()
    });
}
```

**Files**:
- `Api/Models/PagedResponse.cs` (nuovo)
- `Application/Interfaces/IInventoryItemRepository.cs` (extend)
- `Infrastructure/Persistence/Repositories/InventoryItemRepository.cs` (implement)

**Timeline**: 1.5 ore

### 3.3 Error Handling Middleware

Centralizzare error handling:

```csharp
public class GlobalExceptionHandlerMiddleware
{
    public async Task InvokeAsync(HttpContext context, ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new ApiResponse<object>
            {
                Success = false,
                Message = "Internal server error",
                Errors = new List<string> { ex.Message }
            });
        }
    }
}

// In Program.cs:
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
```

**Timeline**: 1 ora

---

## Phase 4: Testing (PRIORITY: MEDIUM)

### 4.1 Unit Tests

Pacchetti: `xUnit`, `Moq`, `FluentAssertions`

```bash
dotnet new xunit -n eCommerce.Inventory.Tests
dotnet add reference ../eCommerce.Inventory.Domain
dotnet add reference ../eCommerce.Inventory.Application
dotnet add reference ../eCommerce.Inventory.Infrastructure
```

**Test Fixtures**:
- `Domain/Entities/` (Domain validation)
- `Infrastructure/Persistence/InventoryItemRepositoryTests.cs`
- `Infrastructure/ExternalServices/CardTraderApiClientTests.cs`

**Coverage Goal**: 80%+

**Timeline**: 4 ore

### 4.2 Integration Tests

```csharp
[TestClass]
public class CardTraderInventoryControllerTests
{
    private WebApplicationFactory<Program> _factory;
    private HttpClient _client;

    [TestInitialize]
    public void Setup()
    {
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [TestMethod]
    public async Task GetAllInventoryItems_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/cardtrader/inventory");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
```

**Timeline**: 3 ore

---

## Phase 5: Advanced Features (PRIORITY: LOW)

### 5.1 Polly Resilience Policies

Aggiungere retry logic per API calls:

```csharp
var retryPolicy = Policy.Handle<HttpRequestException>()
    .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attempt =>
            TimeSpan.FromSeconds(Math.Pow(2, attempt)),
        onRetry: (outcome, ts, retryCount, context) =>
            _logger.LogWarning("Retry {RetryCount} after {Delay}ms",
                retryCount, ts.TotalMilliseconds));

builder.Services
    .AddHttpClient<ICardTraderApiService, CardTraderApiClient>()
    .AddPolicyHandler(retryPolicy);
```

**NuGet**: `Polly.Extensions.Http`

**Timeline**: 1 ora

### 5.2 Caching Strategy

Redis per cache distribuita:

```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});

// Nell'ApiClient:
var cachedGames = await _cache.GetAsync("cardtrader:games");
if (cachedGames == null)
{
    var games = await _httpClient.GetAsync("/games");
    await _cache.SetAsync("cardtrader:games", games, TimeSpan.FromHours(1));
}
```

**NuGet**: `StackExchange.Redis`

**Timeline**: 2 ore

### 5.3 Rate Limiting

```csharp
app.UseRateLimiter();

// In Program.cs:
builder.Services.AddRateLimiter(options =>
    options.AddFixedWindowLimiter(
        policyName: "cardtrader",
        configure: options =>
        {
            options.PermitLimit = 100;
            options.Window = TimeSpan.FromMinutes(1);
        }));

[Route("api/cardtrader/inventory")]
[RateLimiter("cardtrader")]
public class CardTraderInventoryController { ... }
```

**Timeline**: 1 ora

### 5.4 OpenAPI/Swagger Documentation

Aggiungere XML docs ai controller:

```csharp
/// <summary>
/// Get all inventory items for Card Trader
/// </summary>
/// <returns>List of inventory items</returns>
[HttpGet]
[ProducesResponseType(typeof(ApiResponse<List<InventoryItem>>), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status500InternalServerError)]
public async Task<ActionResult<ApiResponse<List<InventoryItem>>>> GetAll()
{
    // ...
}
```

**Timeline**: 2 ore

---

## Phase 6: Marketplace Expansion (PRIORITY: LOW)

Template per aggiungere nuovo marketplace (es. eBay):

### 6.1 File Structure
```
Controllers/Ebay/
├── EbayInventoryController.cs
├── EbayWebhooksController.cs
└── EbaySyncController.cs

Infrastructure/ExternalServices/Ebay/
├── IEbayApiService.cs (in Application)
├── EbayApiClient.cs
├── EbaySyncWorker.cs
└── DTOs/
    ├── EbayProductDto.cs
    └── EbayOrderDto.cs
```

### 6.2 Steps
1. Creare interface `IEbayApiService`
2. Implementare `EbayApiClient`
3. Creare controller `/api/ebay/...`
4. Registrare servizi in `Program.cs`
5. (Opzionale) Shared sync logic in `InventorySyncService`

**Timeline**: 6-8 ore per marketplace (una sola volta)

---

## Phase 7: DevOps & Deployment (PRIORITY: MEDIUM)

### 7.1 Docker Support

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY . .
RUN dotnet restore
RUN dotnet build -c Release -o /out
ENTRYPOINT ["dotnet", "/out/eCommerce.Inventory.Api.dll"]
```

**File**: `Dockerfile` (root)

**Timeline**: 1 ora

### 7.2 CI/CD Pipeline (GitHub Actions)

```yaml
name: Build & Test

on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - uses: actions/setup-dotnet@v2
        with:
          dotnet-version: 8.0
      - run: dotnet restore
      - run: dotnet build
      - run: dotnet test
```

**File**: `.github/workflows/build.yml`

**Timeline**: 1.5 ore

### 7.3 Deployment Configuration

Azure App Service / AWS Lambda setup con:
- Connection string from Key Vault
- Card Trader API token from secrets manager
- Health check endpoints

**Timeline**: 2 ore

---

## Phase 8: Monitoring & Analytics (PRIORITY: LOW)

### 8.1 Application Insights

```csharp
builder.Services.AddApplicationInsightsTelemetry();

// Track custom events:
_telemetryClient.TrackEvent("InventoryItemCreated",
    new Dictionary<string, string> { { "marketplace", "cardtrader" } });
```

**NuGet**: `Microsoft.ApplicationInsights.AspNetCore`

**Timeline**: 1 ora

### 8.2 Health Check Endpoints

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>()
    .AddUrlGroup(new Uri("https://api.cardtrader.com/health"), "CardTrader API");

app.UseHealthChecks("/health");
```

**Timeline**: 30 minuti

---

## Summary Timeline

| Phase | Tasks | Estimated Hours | Priority |
|-------|-------|-----------------|----------|
| 1 | Database & Migrations | 1.5 | HIGH |
| 2 | Card Trader API Integration | 7 | HIGH |
| 3 | API Enhancement | 3.5 | MEDIUM |
| 4 | Testing | 7 | MEDIUM |
| 5 | Advanced Features | 7 | LOW |
| 6 | Marketplace Expansion | 6-8 | LOW |
| 7 | DevOps & Deployment | 4.5 | MEDIUM |
| 8 | Monitoring | 1.5 | LOW |
| **TOTAL** | | **~38-40 ore** | |

---

## Recommended Development Order

1. **Week 1**:
   - Phase 1: Database (1.5h)
   - Phase 2: Card Trader API (7h)
   - Phase 3: API Controllers (3.5h)
   - **Total**: ~12h

2. **Week 2**:
   - Phase 4: Testing (7h)
   - Phase 5: Advanced Features (7h)
   - **Total**: ~14h

3. **Week 3**:
   - Phase 6: Marketplace Expansion (6-8h)
   - Phase 7: DevOps (4.5h)
   - Phase 8: Monitoring (1.5h)
   - **Total**: ~12-13.5h

---

## Version History

- **v0.1** (Current): Architecture, Domain, Infrastructure, API skeleton
- **v0.2** (Next): Database, API client, Webhooks
- **v1.0**: Full Card Trader integration, Testing, Monitoring
- **v2.0**: Multi-marketplace support, Advanced features

---

## Success Criteria

✅ **Phase 1 Complete**: Database creato e migrazioni funzionanti

✅ **Phase 2 Complete**: Sync da Card Trader funzionante, data in DB

✅ **Phase 3 Complete**: API endpoints rispondono correttamente, pagination attiva

✅ **Phase 4 Complete**: 80%+ test coverage

✅ **Phase 5 Complete**: API resiliente con retry e caching

✅ **Phase 6 Complete**: Almeno 1 marketplace aggiunto (eBay o Wallapop)

✅ **Phase 7 Complete**: CI/CD pipeline attiva, deployable su cloud

✅ **Phase 8 Complete**: Monitoring e health checks funzionanti

---

## Questions & Support

Consulta:
- `ARCHITECTURE.md` per design decisions
- `IMPLEMENTATION.md` per dettagli di codice
- `SPECIFICATIONS.md` per linee guida di development
