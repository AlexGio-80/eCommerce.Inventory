# eCommerce.Inventory - Specifiche Tecniche

## Principi Fondamentali del Progetto

Questo documento descrive le regole, i principi e gli standard che devono essere rispettati durante lo sviluppo di eCommerce.Inventory.

---

## 1. SOLID Principles (OBBLIGATORIO)

Tutti i nuovi componenti DEVONO rispettare i principi SOLID:

### Single Responsibility Principle (SRP)
Ogni classe deve avere **una sola ragione di cambiare**.

✅ **CORRETTO**:
```csharp
public class CardTraderApiClient : ICardTraderApiService
{
    // Responsabilità: Comunicare con Card Trader API
    public async Task<IEnumerable<Game>> SyncGamesAsync() { ... }
}

public class InventoryItemRepository : IInventoryItemRepository
{
    // Responsabilità: Accesso dati per InventoryItem
    public async Task AddAsync(InventoryItem item) { ... }
}
```

❌ **SBAGLIATO**:
```csharp
public class CardTraderService
{
    // Mix di responsabilità: API + Repository + Logging
    public void SyncAndSave() { ... }
    public void LogErrors() { ... }
}
```

### Open/Closed Principle (OCP)
Le classi devono essere **aperte per estensione, chiuse per modifica**.

✅ **CORRETTO**:
```csharp
// Per aggiungere eBay:
// 1. Creare Controllers/Ebay/
// 2. Creare IEbayApiService
// 3. Implementare EbayApiClient
// NO MODIFICHE al codice Card Trader!
```

❌ **SBAGLIATO**:
```csharp
public class ApiClient
{
    public async Task Sync(string marketplace)
    {
        if (marketplace == "cardtrader") { ... }
        else if (marketplace == "ebay") { ... }  // Modifica per ogni marketplace
    }
}
```

### Liskov Substitution Principle (LSP)
Qualsiasi implementazione di un'interfaccia deve essere intercambiabile.

✅ **CORRETTO**:
```csharp
ICardTraderApiService service = new CardTraderApiClient(...);
// Nel futuro:
// ICardTraderApiService service = new CardTraderApiClientV2(...);
// Funziona senza cambiare il codice client
```

### Interface Segregation Principle (ISP)
Interfacce **piccole e specifiche**, non generiche.

✅ **CORRETTO**:
```csharp
public interface IReadonlyRepository<T> { ... }  // Solo read
public interface IInventoryItemRepository : IReadonlyRepository<InventoryItem> { ... }  // Read + Write
```

❌ **SBAGLIATO**:
```csharp
public interface IRepository<T>  // Troppo generica
{
    Task<T> GetAsync();
    Task AddAsync(T item);
    Task UpdateAsync(T item);
    Task DeleteAsync(int id);
    Task<List<T>> SearchAsync(string query);
    Task BulkInsertAsync(List<T> items);
    // ... altre 10 operazioni
}
```

### Dependency Inversion Principle (DIP)
Dipendere da **astrazioni**, non da implementazioni concrete.

✅ **CORRETTO**:
```csharp
public class CardTraderSyncWorker
{
    private readonly ICardTraderApiService _apiService;  // Interfaccia!

    public CardTraderSyncWorker(ICardTraderApiService apiService)
    {
        _apiService = apiService;
    }
}
```

❌ **SBAGLIATO**:
```csharp
public class CardTraderSyncWorker
{
    private readonly CardTraderApiClient _apiService;  // Implementazione!

    public CardTraderSyncWorker()
    {
        _apiService = new CardTraderApiClient(...);
    }
}
```

---

## 2. Logging (OBBLIGATORIO)

Serilog DEVE essere usato per tutto il logging.

### Configurazione
- Livello: **Debug** in development, **Information** in production
- Output: **Console** + **File rolling giornaliero**
- Formato: Structured logging con context enrichment

### Best Practices

✅ **CORRETTO**:
```csharp
_logger.LogInformation("Syncing {ItemCount} items for marketplace {Marketplace}",
    items.Count(), "cardtrader");

_logger.LogWarning("Product {ProductId} not found on Card Trader", productId);

_logger.LogError(ex, "Failed to sync games. Retry in {RetryInterval}", TimeSpan.FromMinutes(15));
```

❌ **SBAGLIATO**:
```csharp
Console.WriteLine("Syncing items");  // NO! Usa Serilog!

_logger.LogInformation("Items synced");  // Non structured!

// Loggare dati sensibili:
_logger.LogInformation("Token: {Token}", apiToken);  // NO!
```

### Levelli di Log
- `LogInformation`: Operazioni normali (sync completato, item creato)
- `LogWarning`: Situazioni non critiche (ritentativi, fallback)
- `LogError(ex, "msg")`: Errori con eccezione
- `LogDebug`: Dettagli per debugging (usare con parsimonia)

---

## 3. Marketplace Controller Separation (OBBLIGATORIO)

Ogni marketplace DEVE avere i propri controller in cartelle separate.

### Struttura

```
Controllers/
├── CardTrader/
│   ├── CardTraderInventoryController.cs
│   ├── CardTraderWebhooksController.cs
│   └── CardTraderSyncController.cs
├── Ebay/  (Futuro)
│   ├── EbayInventoryController.cs
│   ├── EbayWebhooksController.cs
│   └── EbaySyncController.cs
└── Wallapop/  (Futuro)
    ├── WallapopInventoryController.cs
    ├── WallapopWebhooksController.cs
    └── WallapopSyncController.cs
```

### Routing Pattern
- Card Trader: `/api/cardtrader/...`
- eBay: `/api/ebay/...`
- Wallapop: `/api/wallapop/...`

✅ **CORRETTO**:
```csharp
[ApiController]
[Route("api/cardtrader/inventory")]
public class CardTraderInventoryController : ControllerBase
{
    // Contiene solo logica per Card Trader
}
```

❌ **SBAGLIATO**:
```csharp
[ApiController]
[Route("api/inventory")]
public class InventoryController : ControllerBase
{
    // Unisce la logica di tutti i marketplace - VIOLAZIONE OCP
}
```

### Aggiungere un Nuovo Marketplace

1. **Creare cartella**: `Controllers/NewMarketplace/`
2. **Creare controllers**: `NewMarketplaceInventoryController`, `NewMarketplaceWebhooksController`, etc.
3. **Creare interface**: `INewMarketplaceApiService` in Application
4. **Creare implementazione**: `NewMarketplaceApiClient` in Infrastructure
5. **Registrare servizi**: In `Program.cs`
6. **NO modifiche** a codice Card Trader!

---

## 4. Dependency Injection (OBBLIGATORIO)

Tutte le dipendenze DEVONO essere iniettate via DI Container.

### Lifetime Scopes

- **Scoped**: DbContext, Repository (per request isolate)
- **Transient**: Stateless services, utilities
- **Singleton**: Configuration, factory, logger (con cautela)

✅ **CORRETTO**:
```csharp
// In Program.cs
builder.Services.AddScoped<ApplicationDbContext>();
builder.Services.AddScoped<IInventoryItemRepository, InventoryItemRepository>();
builder.Services.AddHttpClient<ICardTraderApiService, CardTraderApiClient>();

// Nel controller:
public class CardTraderInventoryController : ControllerBase
{
    public CardTraderInventoryController(
        IInventoryItemRepository repository,
        ILogger<CardTraderInventoryController> logger)
    {
        // Iniettate automaticamente!
    }
}
```

❌ **SBAGLIATO**:
```csharp
public class CardTraderInventoryController : ControllerBase
{
    private readonly IInventoryItemRepository _repository;

    public CardTraderInventoryController()
    {
        _repository = new InventoryItemRepository(...);  // NO!
    }
}
```

---

## 5. Entity Framework Core (OBBLIGATORIO)

### Configurazione
- Provider: **SQL Server**
- Migrations Assembly: `"eCommerce.Inventory.Infrastructure"`
- Lazy Loading: **Disabled** (usa explicit Include)

### Best Practices

✅ **CORRETTO**:
```csharp
// Eager loading
var item = await _context.InventoryItems
    .Include(i => i.Blueprint)
    .ThenInclude(b => b.Expansion)
    .ThenInclude(e => e.Game)
    .FirstOrDefaultAsync(i => i.Id == id);
```

❌ **SBAGLIATO**:
```csharp
// Lazy loading / N+1 query
var item = await _context.InventoryItems.FirstOrDefaultAsync(i => i.Id == id);
var blueprint = item.Blueprint;  // Extra query!
var expansion = blueprint.Expansion;  // Extra query!
```

### Relazioni
Tutte le relazioni DEVONO avere:
- Foreign Key definita
- Navigation properties su entrambi i lati
- Cascade Delete configurato (salvo necessità specifiche)
- Precision per decimali: `HasPrecision(18, 2)`

---

## 6. API Design (OBBLIGATORIO)

### RESTful Endpoints

```
GET    /api/{marketplace}/inventory           → List all items
GET    /api/{marketplace}/inventory/{id}      → Get item by ID
POST   /api/{marketplace}/inventory           → Create item
PUT    /api/{marketplace}/inventory/{id}      → Update item
DELETE /api/{marketplace}/inventory/{id}      → Delete item

GET    /api/{marketplace}/products            → List products on marketplace
POST   /api/{marketplace}/webhooks/...        → Receive webhooks
POST   /api/{marketplace}/sync/...            → Trigger manual sync
```

### Request/Response Models

✅ **CORRETTO**:
```csharp
public class CreateInventoryItemRequest
{
    public int BlueprintId { get; set; }
    public decimal PurchasePrice { get; set; }
    public int Quantity { get; set; }
    [Required]
    public string Condition { get; set; }
}

[HttpPost("inventory")]
public async Task<ActionResult<InventoryItem>> Create(
    [FromBody] CreateInventoryItemRequest request)
{
    // Validation automatica
    // 400 Bad Request se invalid
}
```

### HTTP Status Codes
- `200 OK`: Operazione riuscita
- `201 Created`: Risorsa creata
- `204 No Content`: Cancellazione riuscita
- `400 Bad Request`: Validation error
- `404 Not Found`: Risorsa non trovata
- `500 Internal Server Error`: Errore server

---

## 7. Async/Await (OBBLIGATORIO)

Tutte le operazioni I/O (DB, HTTP, File) DEVONO essere async.

✅ **CORRETTO**:
```csharp
public async Task<InventoryItem> GetAsync(int id)
{
    return await _context.InventoryItems
        .AsNoTracking()
        .FirstOrDefaultAsync(i => i.Id == id);
}
```

❌ **SBAGLIATO**:
```csharp
public InventoryItem Get(int id)  // NO async!
{
    return _context.InventoryItems.FirstOrDefault(i => i.Id == id);
}
```

### CancellationToken
Passare `CancellationToken` dove appropriato:

✅ **CORRETTO**:
```csharp
public async Task<IEnumerable<Game>> SyncGamesAsync(CancellationToken cancellationToken = default)
{
    var response = await _httpClient.GetAsync("games", cancellationToken);
    // Rispetta cancellazione!
}
```

---

## 8. Code Organization (OBBLIGATORIO)

### Folder Structure

```
eCommerce.Inventory.{Layer}/
├── {Feature}/
│   ├── {Entities/Interfaces/Implementation}.cs
│   └── ...
└── ...
```

✅ **CORRETTO**:
```
Infrastructure/
├── Persistence/
│   ├── ApplicationDbContext.cs
│   └── Repositories/
│       └── InventoryItemRepository.cs
├── ExternalServices/
│   └── CardTrader/
│       ├── CardTraderApiClient.cs
│       ├── CardTraderSyncWorker.cs
│       └── DTOs/
```

### Naming Conventions
- **Classes**: PascalCase (`CardTraderApiClient`)
- **Methods**: PascalCase (`SyncGamesAsync`)
- **Variables**: camelCase (`_apiService`)
- **Constants**: PascalCase (`MaxRetries = 3`)
- **Interfaces**: IPascalCase (`ICardTraderApiService`)

---

## 9. Error Handling (OBBLIGATORIO)

### Try-Catch Pattern

✅ **CORRETTO**:
```csharp
try
{
    var games = await _cardTraderApiService.SyncGamesAsync(cancellationToken);
    _logger.LogInformation("Synced {GameCount} games", games.Count());
}
catch (HttpRequestException ex)
{
    _logger.LogError(ex, "Failed to connect to Card Trader API");
    throw;  // O gestire appropriatamente
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unexpected error during game sync");
    throw;
}
```

❌ **SBAGLIATO**:
```csharp
try
{
    var games = await _cardTraderApiService.SyncGamesAsync();
}
catch (Exception ex)
{
    // Niente logging!
}
```

### HttpClient Error Handling
```csharp
response.EnsureSuccessStatusCode();  // Lancia HttpRequestException
```

---

## 10. Configuration Management (OBBLIGATORIO)

### appsettings.json

✅ **CORRETTO**:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=...;Database=eCommerceInventory;..."
  },
  "CardTraderApi": {
    "BaseUrl": "https://api.cardtrader.com/api/v2",
    "BearerToken": "YOUR_TOKEN_HERE"
  }
}
```

### Secrets Management
- **Sviluppo**: User Secrets (`dotnet user-secrets`)
- **Production**: Azure Key Vault, AWS Secrets Manager, etc.
- **NEVER**: Hardcode secrets nel codice!

---

## 11. Testing Strategy (TODO - Implementation)

### Unit Tests
- Test entità Domain
- Test Repository (InMemory EF Core)
- Test servizi con Mock

### Integration Tests
- Test controller con InMemory DB
- Test EF Core migrations

### E2E Tests
- Test workflow completi
- Test con database reale

**Test Pattern**:
```csharp
[TestClass]
public class InventoryItemRepositoryTests
{
    [TestMethod]
    public async Task GetByIdAsync_WithValidId_ReturnsItem()
    {
        // Arrange
        var expectedItem = new InventoryItem { Id = 1, ... };

        // Act
        var result = await _repository.GetByIdAsync(1);

        // Assert
        Assert.AreEqual(expectedItem.Id, result.Id);
    }
}
```

---

## 12. Performance Guidelines (OBBLIGATORIO)

### Database
- ✅ Sempre usare async operations
- ✅ Eager loading con Include() per evitare N+1
- ✅ AsNoTracking() per query read-only
- ✅ Pagination per large datasets (TODO: implement)

### HTTP Client
- ✅ Reuse HttpClientFactory (non creare nuovi HttpClient)
- ✅ Configurare timeout
- ✅ Implement retry logic con Polly (TODO)

### Caching
- TODO: Implement caching strategy
- TODO: Redis for distributed cache

---

## 13. Security Guidelines (OBBLIGATORIO)

- ✅ Bearer Token in appsettings, non in codice
- ✅ SQL Server connection string sicura
- ✅ CORS configurato (restrittivo in production)
- ✅ Input validation su requests
- ✅ HTTPS only in production
- TODO: API key validation
- TODO: Rate limiting
- TODO: Request sanitization

---

## 14. Game Enabled Filter (OBBLIGATORIO)

### Regola Fondamentale
**TUTTE le entità importate dalla Card Trader API DEVONO filtrare in base al flag `Games.IsEnabled`**

Questo impedisce di importare dati inutili per games disabilitati e ottimizza le risorse.

### Entità Interessate
- ✅ **Expansions**: Filtrare durante UpsertExpansionsAsync
- ✅ **Blueprints**: Filtrare durante SyncBlueprintsAsync (solo expansioni di games abilitati)
- ✅ **Categories**: Filtrare durante SyncCategoriesAsync (solo categorie di games abilitati)
- ⚠️ **Future entities**: Deve essere applicato IMMEDIATAMENTE a nuove entità importate

### Implementazione Pattern

Tutte le entità importate DEVONO seguire questo pattern:

```csharp
private async Task SyncEntitiesAsync(SyncResponseDto response, CancellationToken cancellationToken)
{
    // 1. Load enabled games
    var enabledGames = await _dbContext.Games
        .AsNoTracking()
        .Where(g => g.IsEnabled)
        .ToListAsync(cancellationToken);

    _logger.LogInformation("Found {EnabledGameCount} enabled games", enabledGames.Count);

    // 2. Fetch from API
    var dtos = await _cardTraderApiService.SyncEntitiesAsync(cancellationToken);

    // 3. Filter by enabled games
    var filteredEntities = dtos
        .Where(e => enabledGames.Any(g => g.CardTraderId == e.GameId))
        .ToList();

    _logger.LogInformation("Filtered to {FilteredCount} entities for enabled games (skipped {SkippedCount})",
        filteredEntities.Count, dtos.Count - filteredEntities.Count);

    // 4. Continue with sync
    await UpsertEntitiesAsync(filteredEntities, cancellationToken);
}
```

### Logging Obbligatorio
Loggare SEMPRE:
- Numero di entità fetched dall'API
- Numero di entità dopo filtering
- Numero di entità skippate (disabilitate)

**Esempio log**:
```
[INF] Found 14 enabled games
[INF] Fetched 500 categories from Card Trader API
[INF] Filtered to 180 categories for enabled games (skipped 320)
```

### Performance
- Carica games in memoria (lista piccola, ~14 games max)
- Usa `AsNoTracking()` per ottimizzare query
- Filter in-memory con LINQ (meno query al DB)

---

## 15. Grid UI Standards (OBBLIGATORIO)

Tutte le griglie di dati nell'applicazione Angular DEVONO usare **AG-Grid** con le seguenti funzionalità standard.

### Libreria Obbligatoria
- **AG-Grid Community Edition** (già installata in package.json)
- Versione: `^34.3.1` o superiore

### Funzionalità Obbligatorie

Ogni griglia DEVE implementare:

1. **Column Sorting** - Click su header per ordinare
2. **Column Reordering** - Drag & drop per riordinare colonne
3. **Column Visibility Toggle** - Sidebar per mostrare/nascondere colonne
4. **Grid State Persistence** - Salvataggio configurazione in localStorage
5. **Auto-save** - Salvataggio automatico su modifiche
6. **Manual Controls** - Pulsanti Save/Reset nel menu

### Implementazione Standard

✅ **CORRETTO**:
```typescript
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, GridApi, GridReadyEvent } from 'ag-grid-community';
import { GridStateService } from '../../../core/services/grid-state.service';

@Component({
  imports: [AgGridAngular, MatMenuModule, ...],
  // ...
})
export class MyListComponent {
  @ViewChild(AgGridAngular) agGrid!: AgGridAngular;
  private gridApi!: GridApi;
  private readonly GRID_ID = 'my-grid'; // Unique ID per localStorage

  columnDefs: ColDef[] = [
    {
      headerName: 'Name',
      field: 'name',
      sortable: true,
      filter: true,
      width: 200
    }
    // ... altre colonne
  ];

  defaultColDef: ColDef = {
    resizable: true,
    sortable: true,
    filter: true
  };

  gridOptions = {
    domLayout: 'autoHeight' as const,
    animateRows: true,
    sideBar: {
      toolPanels: [{
        id: 'columns',
        labelDefault: 'Columns',
        toolPanel: 'agColumnsToolPanel'
      }]
    }
  };

  constructor(private gridStateService: GridStateService) {}

  onGridReady(params: GridReadyEvent): void {
    this.gridApi = params.api;
    const savedState = this.gridStateService.loadGridState(this.GRID_ID);
    if (savedState?.columnState) {
      this.gridApi.applyColumnState({ 
        state: savedState.columnState, 
        applyOrder: true 
      });
    }
  }

  saveGridState(): void {
    const columnState = this.gridApi.getColumnState();
    this.gridStateService.saveGridState(this.GRID_ID, {
      columnState,
      sortModel: columnState.filter(col => col.sort != null)
    });
  }

  // Event handlers per auto-save
  onColumnMoved(): void { this.saveGridState(); }
  onColumnVisible(): void { this.saveGridState(); }
  onSortChanged(): void { this.saveGridState(); }
}
```

### Template Standard
```html
<ag-grid-angular
  style="width: 100%; height: 600px;"
  class="ag-theme-material"
  [rowData]="data"
  [columnDefs]="columnDefs"
  [defaultColDef]="defaultColDef"
  [gridOptions]="gridOptions"
  (gridReady)="onGridReady($event)"
  (columnMoved)="onColumnMoved()"
  (columnVisible)="onColumnVisible()"
  (sortChanged)="onSortChanged()"
>
</ag-grid-angular>
```

### Stili Standard
```scss
@import 'ag-grid-community/styles/ag-grid.css';
@import 'ag-grid-community/styles/ag-theme-material.css';

.ag-theme-material {
  --ag-header-background-color: #3f51b5;
  --ag-header-foreground-color: white;
  --ag-odd-row-background-color: #f5f5f5;
}
```

### GridStateService
Usare il servizio condiviso `GridStateService` per gestire la persistenza:
- **Path**: `src/app/core/services/grid-state.service.ts`
- **Metodi**: `saveGridState()`, `loadGridState()`, `clearGridState()`

### Grid ID Naming Convention
Ogni griglia DEVE avere un ID univoco per localStorage:
- Orders: `'orders-grid'`
- Inventory: `'inventory-grid'`
- Products: `'products-grid'`
- Blueprints: `'blueprints-grid'`

### Performance Best Practices
- Usare `domLayout: 'autoHeight'` per griglie piccole/medie
- Abilitare `animateRows` per UX migliore
- Configurare `defaultColDef` per evitare ripetizioni
- Usare `valueFormatter` per formattazione custom (date, valute)

### Documentazione
Consultare `Documentation/AG-Grid-Implementation-Guide.md` per:
- Pattern completi di implementazione
- Esempi di column definitions
- Custom cell renderers
- Troubleshooting

❌ **VIETATO**:
```typescript
// NO Material Table per nuove griglie!
import { MatTableModule } from '@angular/material/table';

// NO implementazioni custom di sorting/filtering
// Usare AG-Grid built-in features
```

### Migrazione Griglie Esistenti
Griglie già implementate con Material Table:
- **Orders List**: ✅ Migrata ad AG-Grid
- **Inventory List**: ✅ Migrata ad AG-Grid
- **Blueprints List**: ⏳ Da migrare (complessa, con filtri)

Nuove griglie DEVONO usare AG-Grid da subito.

---

## Checklist per Nuovo Feature

Prima di fare un commit, verificare:

- [ ] Seguiti i principi SOLID
- [ ] Tutto il logging fatto con Serilog
- [ ] Dipendenze iniettate via DI
- [ ] Async/await su I/O operations
- [ ] Error handling con try-catch e logging
- [ ] Code organizzato in folder corrette

---

## 16. Export & Bulk Operations (OBBLIGATORIO)

Le griglie AG-Grid DEVONO supportare funzionalità avanzate di export e operazioni massive.

### Export Standard
Ogni griglia deve implementare l'export tramite `ExportService`:

1. **CSV Export**: Utilizzare funzionalità nativa AG-Grid `gridApi.exportDataAsCsv()`.
2. **Excel Export**: Utilizzare libreria `xlsx` tramite `ExportService.exportToExcel()`.
3. **Export Selected**: Permettere l'export delle sole righe selezionate.

✅ **CORRETTO**:
```typescript
exportToExcel(): void {
  const data = this.getAllRows();
  this.exportService.exportToExcel(data, 'filename');
}

exportSelectedRows(): void {
  const selectedData = this.gridApi.getSelectedRows();
  this.exportService.exportToExcel(selectedData, 'filename-selected');
}
```

### Advanced Filtering
Ogni griglia deve supportare:
1. **Quick Filter**: Input di ricerca globale che filtra su tutte le colonne visibili.
2. **Filter Presets**: Dropdown con filtri predefiniti comuni (es. "Oggi", "Incompleti").
3. **Clear Filters**: Pulsante per resettare tutti i filtri e la ricerca.
4. **Persistence**: Salvare lo stato dei filtri e del quick filter in `localStorage` tramite `GridStateService`.

### Bulk Operations
Per operazioni su più righe (es. Mark Complete, Delete):
1. **Multi-selection**: Abilitare selezione multipla (`rowSelection="multiple"`).
2. **Bulk Actions Toolbar**: Mostrare toolbar dedicata quando `selectedRows.length > 0`.
3. **Confirmation Dialog**: Usare SEMPRE `ConfirmDialogComponent` prima di eseguire l'azione.
4. **Feedback**: Mostrare notifica (SnackBar) con conteggio successi/errori.
5. **Refresh**: Ricaricare la griglia o aggiornare le righe localmente dopo l'operazione.

✅ **CORRETTO**:
```typescript
bulkDelete(): void {
  const selected = this.gridApi.getSelectedRows();
  const dialogRef = this.dialog.open(ConfirmDialogComponent, {
    data: { title: 'Delete Items', message: `Delete ${selected.length} items?` }
  });

  dialogRef.afterClosed().subscribe(confirmed => {
    if (confirmed) {
      // Execute logic...
    }
  });
}
```

- [ ] Naming conventions rispettate
- [ ] EF Core best practices (eager loading, etc.)
- [ ] RESTful endpoints se API
- [ ] Marketplace-specific controller per nuovi marketplace
- [ ] Nessun secret hardcodato
- [ ] Build compila senza errori/warning
- [ ] Documentazione aggiornata
- [ ] **[SE IMPORTAZIONE]** Filtro IsEnabled applicato per Games (sezione 14)
- [ ] **[SE IMPORTAZIONE]** Logging obbligatorio aggiunto (fetched, filtered, skipped counts)
- [ ] **[SE GRIGLIA DATI]** Usato AG-Grid con tutte le funzionalità obbligatorie (sezione 15)
- [ ] **[SE GRIGLIA DATI]** GridStateService configurato per persistenza
- [ ] **[SE GRIGLIA DATI]** SideBar abilitato per column visibility

---

## Contatti e Domande

Consultare:
- `ARCHITECTURE.md` per overview dell'architettura
- `IMPLEMENTATION.md` per dettagli implementativi
- `ROADMAP.md` per i prossimi passi
