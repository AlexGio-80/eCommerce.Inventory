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

- **Phase 2 Part 1: Card Trader Sync Integration** ✅
  - CardTraderDtoMapper con mapping completo (Games, Expansions, Blueprints, Products, Orders)
  - InventorySyncService con logica INSERT/UPDATE/DELETE
  - CardTraderSyncWorker completo (3-step orchestration)
  - CardTraderApiClient refactored per DTOs
  - Tutti i servizi registrati in DI container

- **Phase 2 Part 2: Webhook Processing** ✅
  - WebhookSignatureVerificationService (HMAC SHA256 verification)
  - WebhookDto per payload deserialization
  - ProcessCardTraderWebhookCommand (MediatR)
  - ProcessCardTraderWebhookHandler (order.create, order.update, order.destroy)
  - CardTraderWebhooksController con endpoint POST /api/cardtraderwèbhooks/events
  - MediatR registration nel DI container
  - appsettings.json aggiornato con SharedSecret
  - JSON deserialization con System.Text.Json
  - Build completato con successo (28 warnings, 0 errori)

- **Phase 2 Part 3: Backend Testing** ✅
  - xUnit test project (eCommerce.Inventory.Tests) creato
  - 3 test classes con 14 comprehensive test cases
  - WebhookSignatureVerificationServiceTests (6 tests): HMAC SHA256 signature validation
  - CardTraderWebhooksControllerIntegrationTests (5 tests): webhook endpoint behavior
  - ProcessCardTraderWebhookHandlerTests (3 tests): MediatR handler logic
  - All tests passing (14/14)
  - Code coverage report generated with Coverlet XPlat
  - Key coverage: signature verification, tampering detection, payload validation

🔨 **In Progress**: Phase 3 - Angular Frontend

⏳ **TODO**: Phase 4 Advanced Features, Phase 5 Deployment

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

## Phase 2.3: Backend Testing (PRIORITY: HIGH)

### 2.5 Unit Tests for Handlers

Testare i MediatR handlers per webhook processing:

```csharp
[TestClass]
public class ProcessCardTraderWebhookHandlerTests
{
    private IApplicationDbContext _context;
    private InventorySyncService _syncService;
    private ProcessCardTraderWebhookHandler _handler;

    [TestInitialize]
    public void Setup()
    {
        // Setup mocks e in-memory database
    }

    [TestMethod]
    public async Task HandleOrderCreated_InsertOrder_Success()
    {
        var command = new ProcessCardTraderWebhookCommand(...);
        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.AreEqual(Unit.Value, result);
        // Verify order was inserted
    }
}
```

**Framework**: xUnit, Moq, FluentAssertions

**Timeline**: 2 ore

### 2.6 Integration Tests for Webhook Endpoint

Testare l'endpoint completo:

```csharp
[TestClass]
public class CardTraderWebhooksControllerTests
{
    private WebApplicationFactory<Program> _factory;
    private HttpClient _client;

    [TestMethod]
    public async Task HandleWebhookEvent_ValidSignature_Returns204()
    {
        var request = new WebhookDto { ... };
        var response = await _client.PostAsJsonAsync("/api/cardtraderwèbhooks/events", request);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    public async Task HandleWebhookEvent_InvalidSignature_Returns401()
    {
        // Test with wrong signature header
    }
}
```

**Timeline**: 2 ore

### 2.7 Sync Worker Integration Tests

Testare la sincronizzazione completa:

```csharp
[TestClass]
public class CardTraderSyncWorkerTests
{
    [TestMethod]
    public async Task SyncCardTraderData_FetchesAndSyncs_Successfully()
    {
        // Setup mock API responses
        // Run sync worker
        // Verify database state
    }
}
```

**Timeline**: 1.5 ore

---

## Phase 3: Angular Frontend - Inventory Management UI (PRIORITY: HIGH)

### Overview

Sviluppare un'interfaccia utente moderna in Angular per:
- **Step 1**: Consultare il database locale e visualizzare tabelle (Games, Expansions, Blueprints, InventoryItems, Orders)
- **Step 2**: Sincronizzare dati generici da Card Trader (Games, Expansions, Blueprints)
- **Step 3**: Creare nuove inserzioni prodotto su Card Trader
- **Step 4**: Aggiornare il magazzino tramite webhook events
- **Step 5**: Reporting e BI per analisi vendite

---

## Phase 3.0: Project Setup (PRIORITY: HIGH)

### 3.0.1 Angular Project Setup

```bash
ng new ecommerce-inventory-ui --routing --skip-git
cd ecommerce-inventory-ui
ng add @angular/material
npm install axios @auth0/angular-jwt
npm install chart.js ng2-charts  # For BI/Reporting
npm install date-fns              # For date handling
```

**Timeline**: 30 minuti

### 3.0.2 Core Project Structure

```
src/
├── app/
│   ├── core/
│   │   ├── guards/
│   │   │   └── auth.guard.ts
│   │   ├── interceptors/
│   │   │   └── auth.interceptor.ts
│   │   ├── models/
│   │   │   ├── inventory-item.ts
│   │   │   ├── order.ts
│   │   │   ├── game.ts
│   │   │   ├── expansion.ts
│   │   │   └── blueprint.ts
│   │   └── services/
│   │       ├── cardtrader-api.service.ts
│   │       ├── inventory.service.ts
│   │       ├── auth.service.ts
│   │       └── notification.service.ts
│   ├── features/
│   │   ├── inventory/
│   │   │   ├── components/
│   │   │   ├── pages/
│   │   │   └── inventory.module.ts
│   │   ├── products/
│   │   │   ├── components/
│   │   │   ├── pages/
│   │   │   └── products.module.ts
│   │   ├── orders/
│   │   │   ├── components/
│   │   │   ├── pages/
│   │   │   └── orders.module.ts
│   │   ├── reporting/
│   │   │   ├── components/
│   │   │   ├── pages/
│   │   │   └── reporting.module.ts
│   │   └── sync/
│   │       ├── components/
│   │       └── sync.module.ts
│   ├── shared/
│   │   ├── components/ (reusable UI)
│   │   ├── pipes/
│   │   └── shared.module.ts
│   └── app.module.ts
└── ...
```

**Timeline**: 45 minuti

---

## Phase 3.1: Database Consultation UI - View Existing Data

### 3.1.1 Create Data Models

Creare le interfacce TypeScript per mappare le entità del backend:

```typescript
// core/models/game.ts
export interface Game {
  id: number;
  name: string;
  code: string;
  cardTraderId?: number;
  createdAt: Date;
  updatedAt: Date;
}

// core/models/expansion.ts
export interface Expansion {
  id: number;
  gameId: number;
  name: string;
  code: string;
  cardTraderEmberId?: string;
  createdAt: Date;
}

// core/models/blueprint.ts
export interface Blueprint {
  id: number;
  expansionId: number;
  cardName: string;
  cardTraderProductId?: number;
  rarity?: string;
  condition?: string;
  createdAt: Date;
}

// core/models/inventory-item.ts
export interface InventoryItem {
  id: number;
  blueprintId: number;
  blueprint?: Blueprint;
  quantity: number;
  price: number;
  cardTraderProductId?: number;
  status: 'active' | 'inactive' | 'sold';
  createdAt: Date;
  updatedAt: Date;
}

// core/models/order.ts
export interface Order {
  id: number;
  cardTraderOrderId?: string;
  status: 'pending' | 'paid' | 'shipped' | 'delivered' | 'cancelled';
  totalPrice: number;
  createdAt: Date;
  updatedAt: Date;
}

export interface OrderItem {
  id: number;
  orderId: number;
  inventoryItemId: number;
  quantity: number;
  price: number;
}
```

**Timeline**: 1 ora

### 3.1.2 Create API Service

```typescript
// core/services/cardtrader-api.service.ts
@Injectable({ providedIn: 'root' })
export class CardTraderApiService {
  private apiUrl = 'http://localhost:5000/api/cardtrader';

  constructor(private http: HttpClient) {}

  // Games
  getGames(): Observable<Game[]> {
    return this.http.get<Game[]>(`${this.apiUrl}/games`);
  }

  // Expansions
  getExpansions(gameId?: number): Observable<Expansion[]> {
    const params = gameId ? { gameId: gameId.toString() } : {};
    return this.http.get<Expansion[]>(`${this.apiUrl}/expansions`, { params });
  }

  // Blueprints
  getBlueprints(expansionId?: number): Observable<Blueprint[]> {
    const params = expansionId ? { expansionId: expansionId.toString() } : {};
    return this.http.get<Blueprint[]>(`${this.apiUrl}/blueprints`, { params });
  }

  // Inventory Items
  getInventoryItems(page = 1, pageSize = 20): Observable<PagedResponse<InventoryItem>> {
    const params = { page: page.toString(), pageSize: pageSize.toString() };
    return this.http.get<PagedResponse<InventoryItem>>(`${this.apiUrl}/inventory`, { params });
  }

  // Orders
  getOrders(page = 1, pageSize = 20): Observable<PagedResponse<Order>> {
    const params = { page: page.toString(), pageSize: pageSize.toString() };
    return this.http.get<PagedResponse<Order>>(`${this.apiUrl}/orders`, { params });
  }

  // Get single item
  getInventoryItem(id: number): Observable<InventoryItem> {
    return this.http.get<InventoryItem>(`${this.apiUrl}/inventory/${id}`);
  }
}
```

**Timeline**: 1.5 ore

### 3.1.3 Create Inventory List Component

```typescript
// features/inventory/pages/inventory-list/inventory-list.component.ts
@Component({
  selector: 'app-inventory-list',
  templateUrl: './inventory-list.component.html',
  styleUrls: ['./inventory-list.component.scss']
})
export class InventoryListComponent implements OnInit {
  items$: Observable<InventoryItem[]>;
  games$: Observable<Game[]>;
  selectedGame$: Observable<Game | null>;

  page = 1;
  pageSize = 20;
  totalItems = 0;

  displayedColumns = ['id', 'cardName', 'game', 'expansion', 'quantity', 'price', 'status', 'actions'];

  constructor(
    private apiService: CardTraderApiService,
    private store: Store
  ) {}

  ngOnInit() {
    this.items$ = this.apiService.getInventoryItems(this.page, this.pageSize);
    this.games$ = this.apiService.getGames();
  }

  onGameSelected(game: Game) {
    this.store.dispatch(selectGame({ game }));
  }

  onPageChange(event: PageEvent) {
    this.page = event.pageIndex + 1;
    this.pageSize = event.pageSize;
    this.items$ = this.apiService.getInventoryItems(this.page, this.pageSize);
  }

  openDetails(item: InventoryItem) {
    // Open detail modal
  }

  deleteItem(id: number) {
    // Delete item
  }
}
```

**Html Template**:
```html
<div class="inventory-container">
  <mat-toolbar color="primary">
    <h1>📦 Inventario</h1>
    <span class="spacer"></span>
    <button mat-raised-button color="accent" (click)="openCreateDialog()">
      <mat-icon>add</mat-icon> Nuovo Articolo
    </button>
  </mat-toolbar>

  <mat-card class="filters-card">
    <mat-form-field appearance="fill">
      <mat-label>Gioco</mat-label>
      <mat-select (selectionChange)="onGameSelected($event.value)">
        <mat-option *ngFor="let game of games$ | async" [value]="game">
          {{game.name}}
        </mat-option>
      </mat-select>
    </mat-form-field>
  </mat-card>

  <mat-table [dataSource]="items$ | async" class="inventory-table">
    <ng-container matColumnDef="cardName">
      <mat-header-cell *matHeaderCellDef>Nome Carta</mat-header-cell>
      <mat-cell *matCellDef="let item">{{item.blueprint?.cardName}}</mat-cell>
    </ng-container>

    <ng-container matColumnDef="quantity">
      <mat-header-cell *matHeaderCellDef>Quantità</mat-header-cell>
      <mat-cell *matCellDef="let item">{{item.quantity}}</mat-cell>
    </ng-container>

    <ng-container matColumnDef="price">
      <mat-header-cell *matHeaderCellDef>Prezzo</mat-header-cell>
      <mat-cell *matCellDef="let item">€ {{item.price | number:'1.2-2'}}</mat-cell>
    </ng-container>

    <ng-container matColumnDef="status">
      <mat-header-cell *matHeaderCellDef>Stato</mat-header-cell>
      <mat-cell *matCellDef="let item">
        <mat-chip [color]="item.status === 'active' ? 'primary' : 'warn'" selected>
          {{item.status}}
        </mat-chip>
      </mat-cell>
    </ng-container>

    <ng-container matColumnDef="actions">
      <mat-header-cell *matHeaderCellDef>Azioni</mat-header-cell>
      <mat-cell *matCellDef="let item">
        <button mat-icon-button (click)="openDetails(item)">
          <mat-icon>edit</mat-icon>
        </button>
        <button mat-icon-button color="warn" (click)="deleteItem(item.id)">
          <mat-icon>delete</mat-icon>
        </button>
      </mat-cell>
    </ng-container>

    <mat-header-row *matHeaderRowDef="displayedColumns"></mat-header-row>
    <mat-row *matRowDef="let row; columns: displayedColumns;"></mat-row>
  </mat-table>

  <mat-paginator
    [length]="totalItems"
    [pageSize]="pageSize"
    [pageSizeOptions]="[10, 20, 50]"
    (page)="onPageChange($event)">
  </mat-paginator>
</div>
```

**Timeline**: 3 ore

### 3.1.4 Create Dashboard Component

```typescript
// features/inventory/pages/dashboard/dashboard.component.ts
@Component({
  selector: 'app-dashboard',
  templateUrl: './dashboard.component.html'
})
export class DashboardComponent implements OnInit {
  stats$: Observable<DashboardStats>;
  recentOrders$: Observable<Order[]>;

  constructor(private apiService: CardTraderApiService) {}

  ngOnInit() {
    this.stats$ = forkJoin({
      totalItems: this.apiService.getInventoryItems(1, 1).pipe(map(r => r.totalCount)),
      totalOrders: this.apiService.getOrders(1, 1).pipe(map(r => r.totalCount)),
      games: this.apiService.getGames().pipe(map(games => games.length))
    }).pipe(
      map(data => ({
        totalProducts: data.totalItems,
        totalOrders: data.totalOrders,
        gameCount: data.games,
        lastSync: new Date()
      }))
    );

    this.recentOrders$ = this.apiService.getOrders(1, 5).pipe(map(r => r.items));
  }
}
```

**Timeline**: 2 ore

**Total Phase 3.1**: ~7.5 ore

---

## Phase 3.2: Card Trader Data Initial Sync

### 3.2.1 Create Sync Service

```typescript
// core/services/sync.service.ts
@Injectable({ providedIn: 'root' })
export class SyncService {
  syncProgress$ = new BehaviorSubject<SyncProgress>({
    status: 'idle',
    currentStep: '',
    progress: 0
  });

  constructor(
    private apiService: CardTraderApiService,
    private http: HttpClient,
    private notification: NotificationService
  ) {}

  syncCardTraderData(): Observable<SyncResult> {
    this.syncProgress$.next({
      status: 'running',
      currentStep: 'Sincronizzazione Giochi...',
      progress: 20
    });

    return this.http.post<SyncResult>('/api/cardtrader/sync', {}).pipe(
      tap(result => {
        this.syncProgress$.next({
          status: 'completed',
          currentStep: 'Sincronizzazione completata!',
          progress: 100
        });
        this.notification.success('Sincronizzazione completata con successo');
      }),
      catchError(error => {
        this.syncProgress$.next({
          status: 'error',
          currentStep: 'Errore durante la sincronizzazione',
          progress: 0
        });
        this.notification.error('Errore durante la sincronizzazione');
        return throwError(error);
      })
    );
  }
}
```

**Timeline**: 1 ora

### 3.2.2 Create Sync Page Component

```typescript
// features/sync/pages/initial-sync/initial-sync.component.ts
@Component({
  selector: 'app-initial-sync',
  templateUrl: './initial-sync.component.html'
})
export class InitialSyncComponent {
  syncProgress$ = this.syncService.syncProgress$;
  isRunning = false;

  constructor(private syncService: SyncService) {}

  startSync() {
    this.isRunning = true;
    this.syncService.syncCardTraderData().subscribe({
      complete: () => {
        this.isRunning = false;
      },
      error: () => {
        this.isRunning = false;
      }
    });
  }
}
```

**Html**:
```html
<mat-card class="sync-container">
  <mat-card-header>
    <mat-card-title>🔄 Sincronizzazione Card Trader</mat-card-title>
    <mat-card-subtitle>Carica i dati iniziali (Giochi, Espansioni, Inventario)</mat-card-subtitle>
  </mat-card-header>

  <mat-card-content>
    <div *ngIf="(syncProgress$ | async) as progress">
      <p>{{progress.currentStep}}</p>
      <mat-progress-bar
        mode="determinate"
        [value]="progress.progress"
        [color]="progress.status === 'error' ? 'warn' : 'primary'">
      </mat-progress-bar>
      <p class="progress-text">{{progress.progress}}%</p>
    </div>
  </mat-card-content>

  <mat-card-actions>
    <button mat-raised-button color="primary" (click)="startSync()" [disabled]="isRunning">
      <mat-icon *ngIf="!isRunning">sync</mat-icon>
      <mat-spinner *ngIf="isRunning" diameter="20"></mat-spinner>
      {{isRunning ? 'Sincronizzazione in corso...' : 'Avvia Sincronizzazione'}}
    </button>
  </mat-card-actions>
</mat-card>
```

**Timeline**: 2.5 ore

**Total Phase 3.2**: ~3.5 ore

---

## Phase 3.3: Product Listing Creation - Advanced Features

### 3.3.1 Listing Creation Workflow

Un flusso complesso che include:

#### 3.3.1.1 Basic Listing Form
- Selezionare Blueprint dal database
- Inserire quantità disponibile
- Impostare prezzo
- Selezionare condizione (NM, LP, MP, HP, DMG)
- Aggiungere commenti custom

```typescript
// features/products/pages/create-listing/create-listing.component.ts
@Component({
  selector: 'app-create-listing',
  templateUrl: './create-listing.component.html'
})
export class CreateListingComponent implements OnInit {
  listingForm: FormGroup;
  blueprints$: Observable<Blueprint[]>;
  conditions = ['NM', 'LP', 'MP', 'HP', 'DMG'];

  constructor(
    private fb: FormBuilder,
    private apiService: CardTraderApiService,
    private productService: ProductService
  ) {
    this.listingForm = this.createForm();
  }

  ngOnInit() {
    this.blueprints$ = this.apiService.getBlueprints();
  }

  createForm(): FormGroup {
    return this.fb.group({
      blueprintId: ['', Validators.required],
      quantity: [1, [Validators.required, Validators.min(1)]],
      price: ['', [Validators.required, Validators.min(0.01)]],
      condition: ['NM', Validators.required],
      comments: [''],
      languageCode: ['ENG'],
      isFoil: [false],
      isPlayset: [false]
    });
  }

  submit() {
    if (this.listingForm.valid) {
      this.productService.createListing(this.listingForm.value).subscribe(
        result => {
          // Success
        },
        error => {
          // Error handling
        }
      );
    }
  }
}
```

**Timeline**: 3 ore

#### 3.3.1.2 Price Optimization Helper
- Suggerimenti di prezzo basati su Card Trader market
- Storico prezzi della carta
- Analisi competitiva (prezzi di altre inserzioni)
- Margine di profitto calcolato

```typescript
// features/products/services/price-optimizer.service.ts
@Injectable({ providedIn: 'root' })
export class PriceOptimizerService {
  constructor(private apiService: CardTraderApiService) {}

  getPriceSuggestions(productId: number): Observable<PriceSuggestion> {
    return this.apiService.getPriceAnalytics(productId).pipe(
      map(analytics => ({
        recommendedPrice: analytics.avgPrice * 0.95,  // 95% of market
        minPrice: analytics.minPrice,
        maxPrice: analytics.maxPrice,
        avgPrice: analytics.avgPrice,
        competitorCount: analytics.competitorCount,
        profit: (analytics.avgPrice * 0.95) - this.getCost(productId)
      }))
    );
  }

  private getCost(productId: number): number {
    // Calculate base cost from purchase history
    return 0;
  }
}
```

**Timeline**: 2.5 ore

#### 3.3.1.3 Bulk Listing Creator
- Upload CSV con multiple carte
- Template di prezzo applicato a batch
- Validazione batch prima di pubblicare
- Progress tracking per bulk operations

```typescript
// features/products/components/bulk-listing-uploader/bulk-listing-uploader.component.ts
@Component({
  selector: 'app-bulk-listing-uploader',
  templateUrl: './bulk-listing-uploader.component.html'
})
export class BulkListingUploaderComponent {
  uploadProgress = 0;
  listings: BulkListing[] = [];

  constructor(private productService: ProductService) {}

  onFileSelected(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) {
      this.parseCsvFile(file);
    }
  }

  parseCsvFile(file: File) {
    // Parse CSV
    // Validate data
    // Show preview
  }

  submitBulkListings() {
    this.productService.createBulkListings(this.listings).subscribe(
      progress => {
        this.uploadProgress = progress.percent;
      },
      error => {
        // Error handling
      }
    );
  }
}
```

**Timeline**: 3 ore

#### 3.3.1.4 Listing Preview & Validation
- Anteprima di come apparirà su Card Trader
- Validazione dei dati obbligatori
- Avvisi per prezzi anomali
- Stima di tempo per la vendita

```typescript
// features/products/components/listing-preview/listing-preview.component.ts
@Component({
  selector: 'app-listing-preview',
  templateUrl: './listing-preview.component.html'
})
export class ListingPreviewComponent {
  @Input() listing: CreateListingDto;

  validationErrors: ValidationError[] = [];
  estimatedSellTime: string;

  ngOnInit() {
    this.validate();
    this.estimateSellTime();
  }

  validate() {
    // Validation logic
    // Price anomaly detection
    // Required fields check
  }

  estimateSellTime() {
    // Based on historical data and similar listings
  }
}
```

**Timeline**: 2 ore

**Total Phase 3.3**: ~10.5 ore

---

## Phase 3.4: Webhook Integration - Live Inventory Updates

### 3.4.1 WebSocket Connection Setup

```typescript
// core/services/webhook-listener.service.ts
@Injectable({ providedIn: 'root' })
export class WebhookListenerService {
  orderUpdates$ = new Subject<OrderUpdate>();
  inventoryUpdates$ = new Subject<InventoryUpdate>();

  constructor(private notification: NotificationService) {
    this.initializeWebSocketConnection();
  }

  private initializeWebSocketConnection() {
    // Connect to backend WebSocket endpoint
    // Listen for webhook events from Card Trader
    // Emit updates to components
  }

  private handleOrderUpdate(event: any) {
    const update: OrderUpdate = {
      orderId: event.data.id,
      status: event.data.status,
      timestamp: new Date()
    };
    this.orderUpdates$.next(update);
    this.notification.info(`Ordine #${event.data.id} - ${event.data.status}`);
  }

  private handleInventoryUpdate(event: any) {
    const update: InventoryUpdate = {
      productId: event.data.productId,
      quantitySold: event.data.quantity,
      newPrice: event.data.price,
      timestamp: new Date()
    };
    this.inventoryUpdates$.next(update);
  }
}
```

**Timeline**: 2 ore

### 3.4.2 Real-time Order Status Component

```typescript
// features/orders/components/order-status-monitor/order-status-monitor.component.ts
@Component({
  selector: 'app-order-status-monitor',
  templateUrl: './order-status-monitor.component.html'
})
export class OrderStatusMonitorComponent implements OnInit {
  recentOrders$ = this.webhookService.orderUpdates$.pipe(
    scan((acc, update) => [update, ...acc.slice(0, 9)], [] as OrderUpdate[])
  );

  orderTimeline$ = this.apiService.getOrders(1, 20).pipe(
    map(response => response.items),
    shareReplay(1)
  );

  constructor(
    private webhookService: WebhookListenerService,
    private apiService: CardTraderApiService
  ) {}

  ngOnInit() {
    // Listen for real-time updates
  }
}
```

**Html**:
```html
<mat-card class="monitor-card">
  <mat-card-header>
    <mat-card-title>📊 Monitoraggio Ordini</mat-card-title>
  </mat-card-header>

  <mat-list>
    <mat-list-item *ngFor="let order of recentOrders$ | async">
      <mat-icon matListAvatar [color]="getStatusColor(order.status)">
        {{getStatusIcon(order.status)}}
      </mat-icon>
      <div matLine>Ordine #{{order.orderId}}</div>
      <div matLine>{{order.status}} - {{order.timestamp | date:'short'}}</div>
    </mat-list-item>
  </mat-list>
</mat-card>
```

**Timeline**: 2 ore

### 3.4.3 Inventory Auto-update on Sales

```typescript
// features/inventory/services/inventory-sync.service.ts
@Injectable({ providedIn: 'root' })
export class InventorySyncService {
  constructor(
    private webhookService: WebhookListenerService,
    private apiService: CardTraderApiService
  ) {}

  startAutoSync() {
    this.webhookService.inventoryUpdates$.subscribe(update => {
      // Decrement inventory quantity
      // Update local cache
      // Refresh inventory list if visible
      this.updateLocalInventory(update);
    });
  }

  private updateLocalInventory(update: InventoryUpdate) {
    // Update component state
    // No need to refresh from server, webhook already provided the data
  }
}
```

**Timeline**: 1.5 ore

**Total Phase 3.4**: ~5.5 ore

---

## Phase 3.5: Reporting & Business Intelligence

### 3.5.1 Sales Dashboard

Visualizzare metriche chiave:
- Total revenue (giornaliero, settimanale, mensile)
- Best selling products
- Average sale price
- Sell-through rate
- Inventory turnover

```typescript
// features/reporting/pages/sales-dashboard/sales-dashboard.component.ts
@Component({
  selector: 'app-sales-dashboard',
  templateUrl: './sales-dashboard.component.html'
})
export class SalesDashboardComponent implements OnInit {
  salesChart$: Observable<ChartConfiguration>;
  topProducts$: Observable<ProductSales[]>;
  metrics$: Observable<SalesMetrics>;
  dateRange = {
    start: new Date(new Date().getFullYear(), new Date().getMonth(), 1),
    end: new Date()
  };

  constructor(private reportingService: ReportingService) {}

  ngOnInit() {
    this.loadData();
  }

  loadData() {
    this.metrics$ = this.reportingService.getSalesMetrics(this.dateRange);
    this.salesChart$ = this.reportingService.getSalesChart(this.dateRange);
    this.topProducts$ = this.reportingService.getTopProducts(this.dateRange);
  }

  onDateRangeChange(range: DateRange) {
    this.dateRange = range;
    this.loadData();
  }
}
```

**Timeline**: 3 ore

### 3.5.2 Inventory Analytics

- Inventory value over time
- Stock turnover by game/expansion
- Aging inventory report
- Price elasticity analysis
- Slow movers identification

```typescript
// features/reporting/pages/inventory-analytics/inventory-analytics.component.ts
@Component({
  selector: 'app-inventory-analytics',
  templateUrl: './inventory-analytics.component.html'
})
export class InventoryAnalyticsComponent implements OnInit {
  inventoryValueChart$: Observable<ChartConfiguration>;
  turnoverByGame$: Observable<TurnoverMetric[]>;
  slowMovers$: Observable<SlowMoverProduct[]>;
  totalInventoryValue$: Observable<number>;

  constructor(private reportingService: ReportingService) {}

  ngOnInit() {
    this.inventoryValueChart$ = this.reportingService.getInventoryValueChart();
    this.turnoverByGame$ = this.reportingService.getTurnoverByGame();
    this.slowMovers$ = this.reportingService.getSlowMovers(90); // 90 days
    this.totalInventoryValue$ = this.reportingService.getTotalInventoryValue();
  }
}
```

**Timeline**: 3 ore

### 3.5.3 Profitability Analysis

- Cost per product (from purchase history)
- Profit margin by product/game
- Cost-adjusted ranking
- ROI calculation

```typescript
// features/reporting/services/profitability.service.ts
@Injectable({ providedIn: 'root' })
export class ProfitabilityService {
  getProfitabilityReport(): Observable<ProfitabilityReport> {
    return forkJoin({
      sales: this.getSalesData(),
      costs: this.getCostsData(),
      inventory: this.getInventoryData()
    }).pipe(
      map(data => this.calculateProfitability(data))
    );
  }

  private calculateProfitability(data: any): ProfitabilityReport {
    return {
      totalRevenue: data.sales.reduce((sum, s) => sum + s.price, 0),
      totalCosts: data.costs.reduce((sum, c) => sum + c.cost, 0),
      grossProfit: /* calculated */,
      profitMargin: /* calculated */,
      byProduct: /* detailed breakdown */
    };
  }
}
```

**Timeline**: 2.5 ore

### 3.5.4 Export & Reporting

- Export to Excel/PDF
- Scheduled email reports
- Custom report builder
- Performance benchmarking

```typescript
// features/reporting/services/export.service.ts
@Injectable({ providedIn: 'root' })
export class ExportService {
  exportToExcel(report: SalesMetrics, filename: string) {
    // Generate Excel file with charts and data
    // Use xlsx library
  }

  exportToPdf(report: SalesMetrics, filename: string) {
    // Generate PDF report
    // Use pdfmake library
  }

  scheduleEmailReport(recipients: string[], frequency: 'daily' | 'weekly' | 'monthly') {
    // Schedule recurring report delivery
  }
}
```

**Timeline**: 2 ore

**Total Phase 3.5**: ~10.5 ore

---

## Phase 3.X: Future Enhancements - AI Integration (PRIORITY: LOW)

### 3.X.1 AI Card Grading Evaluation

**Idea**: Usare computer vision + ML per valutare automaticamente il grading delle carte:

```typescript
// features/ai/services/card-grading.service.ts
@Injectable({ providedIn: 'root' })
export class CardGradingService {
  // Future: Integrate with ML model (TensorFlow.js or cloud API)

  analyzeCardImage(image: File): Observable<CardGradingResult> {
    // 1. Upload image
    // 2. Send to ML service (Azure ML, GCP Vision, or local TensorFlow)
    // 3. Receive grading prediction (Gem MT 10, Near Mint 9, Mint 8, etc.)
    // 4. Return confidence score
    return this.http.post<CardGradingResult>('/api/ai/grade-card', formData);
  }
}
```

**Possibili implementazioni**:
- **Local TensorFlow.js**: Processamento nel browser, no server calls
- **Cloud Vision API**: Google Cloud Vision per analisi immagini
- **Custom ML Model**: Addestramento con dataset di carte graduate (PSA/BGS)
- **Hybrid approach**: Local detection + cloud verification

**Timeline**: Future phase, 8-10 ore

---

## Phase 3.6: Authentication & Security

### 3.6.1 JWT Authentication

```typescript
// core/services/auth.service.ts
@Injectable({ providedIn: 'root' })
export class AuthService {
  private token$ = new BehaviorSubject<string | null>(null);

  constructor(private http: HttpClient) {
    this.loadTokenFromStorage();
  }

  login(username: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/login', { username, password }).pipe(
      tap(response => {
        this.token$.next(response.token);
        localStorage.setItem('auth_token', response.token);
      })
    );
  }

  logout() {
    this.token$.next(null);
    localStorage.removeItem('auth_token');
  }

  isAuthenticated(): boolean {
    return !!this.token$.value;
  }
}
```

**Timeline**: 1.5 ore

### 3.6.2 HTTP Interceptor

```typescript
// core/interceptors/auth.interceptor.ts
@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  constructor(private authService: AuthService) {}

  intercept(request: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    const token = this.authService.getToken();
    if (token) {
      request = request.clone({
        setHeaders: {
          Authorization: `Bearer ${token}`
        }
      });
    }
    return next.handle(request);
  }
}
```

**Timeline**: 1 ora

**Total Phase 3.6**: ~2.5 ore

---

## Phase 3.7: Testing & QA

### 3.7.1 Unit Tests
- Service tests (API calls, data transformation)
- Component tests (UI logic, event handling)
- Pipe tests (data formatting)

**Timeline**: 3 ore

### 3.7.2 E2E Tests (Cypress)
- User workflows (login → sync → create listing → monitor)
- Form submission & validation
- Dashboard interaction

**Timeline**: 2.5 ore

**Total Phase 3.7**: ~5.5 ore

---

## Phase 3: Summary

| Sub-Phase | Tasks | Estimated Hours | Status |
|-----------|-------|-----------------|--------|
| 3.0 | Project Setup | 1.25 | ⏳ TODO |
| 3.1 | Database Consultation UI | 7.5 | ⏳ TODO |
| 3.2 | Card Trader Data Sync | 3.5 | ⏳ TODO |
| 3.3 | Product Listing Creation | 10.5 | ⏳ TODO |
| 3.4 | Webhook Integration | 5.5 | ⏳ TODO |
| 3.5 | Reporting & BI | 10.5 | ⏳ TODO |
| 3.6 | Authentication & Security | 2.5 | ⏳ TODO |
| 3.7 | Testing & QA | 5.5 | ⏳ TODO |
| 3.X | AI Card Grading (Future) | 8-10 | 📅 FUTURE |
| **TOTAL** | | **~47 ore** | |

**Timeline**: ~6-7 working days per step

---

## Phase 4: API Controller Enhancement (PRIORITY: MEDIUM)

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

| Phase | Tasks | Estimated Hours | Priority | Status |
|-------|-------|-----------------|----------|--------|
| 1 | Database & Migrations | 1.5 | HIGH | ✅ DONE |
| 2.1 | Card Trader Sync Integration | 7 | HIGH | ✅ DONE |
| 2.2 | Webhook Processing | 5 | HIGH | ✅ DONE |
| 2.3 | Backend Testing | 5.5 | HIGH | 🔨 IN PROGRESS |
| 3.0 | Angular Project Setup | 1.25 | HIGH | ✅ DONE |
| 3.1 | Database Consultation UI | 7.5 | HIGH | 🔨 NEXT |
| 3.2 | Card Trader Data Initial Sync | 3.5 | HIGH | ⏳ TODO |
| 3.3 | Product Listing Creation | 10.5 | HIGH | ⏳ TODO |
| 3.4 | Webhook Integration (Frontend) | 5.5 | HIGH | ⏳ TODO |
| 3.5 | Reporting & BI | 10.5 | HIGH | ⏳ TODO |
| 3.6 | Authentication & Security | 2.5 | MEDIUM | ⏳ TODO |
| 3.7 | Testing & QA | 5.5 | MEDIUM | ⏳ TODO |
| 4 | API Controller Enhancement | 3.5 | MEDIUM | ⏳ TODO |
| 5 | Advanced Features (Polly, Caching, Rate Limiting) | 7 | LOW | ⏳ TODO |
| 6 | Marketplace Expansion | 6-8 | LOW | ⏳ TODO |
| 7 | DevOps & Deployment | 4.5 | MEDIUM | ⏳ TODO |
| 8 | Monitoring & Analytics | 1.5 | LOW | ⏳ TODO |
| 3.X | AI Card Grading (Future) | 8-10 | LOW | 📅 FUTURE |
| **TOTAL** | | **~90-95 ore** | | |

---

## Recommended Development Order

1. **Week 1 - COMPLETED ✅**:
   - Phase 1: Database (1.5h) ✅
   - Phase 2.1: Card Trader Sync (7h) ✅
   - Phase 2.2: Webhooks (5h) ✅
   - **Total**: ~13.5h ✅

2. **Week 2 - CURRENT 🔨**:
   - Phase 2.3: Backend Testing (5.5h) - IN PROGRESS
   - **Total**: 5.5h

3. **Week 3 - CURRENT 🔨**:
   - Phase 3.0: Angular Project Setup (1.25h) ✅ DONE
   - Phase 3.1: Database Consultation UI (7.5h) - NEXT
   - Phase 3.2: Card Trader Data Initial Sync (3.5h)
   - **Total**: ~12.25h

4. **Week 4 - COMPLEX ⏳**:
   - Phase 3.3: Product Listing Creation (10.5h) - **Most complex step, includes:**
     - 3.3.1.1: Basic Listing Form (3h)
     - 3.3.1.2: Price Optimization Helper (2.5h)
     - 3.3.1.3: Bulk Listing Creator (3h)
     - 3.3.1.4: Listing Preview & Validation (2h)
   - **Total**: 10.5h

5. **Week 5 - REAL-TIME INTEGRATION ⏳**:
   - Phase 3.4: Webhook Integration (5.5h)
   - Phase 3.5: Reporting & BI (10.5h)
   - **Total**: ~16h

6. **Week 6 - FINALIZATION ⏳**:
   - Phase 3.6: Authentication & Security (2.5h)
   - Phase 3.7: Testing & QA (5.5h)
   - **Total**: ~8h

7. **Week 7+ - POST-MVP**:
   - Phase 4: API Controllers (3.5h)
   - Phase 5: Advanced Features (7h)
   - Phase 6: Marketplace Expansion (6-8h)
   - Phase 7: DevOps (4.5h)
   - Phase 8: Monitoring (1.5h)
   - **Total**: ~22.5-23.5h

8. **Future - AI Integration**:
   - Phase 3.X: AI Card Grading (8-10h)

---

## Version History

- **v0.1** ✅ COMPLETED: Architecture (Clean Architecture), Domain models, Infrastructure (DbContext, Repository pattern), API skeleton
- **v0.2** 🔨 IN PROGRESS: Database (Phase 1✅), API client + Sync (Phase 2.1✅), Webhooks (Phase 2.2✅), Backend Testing (Phase 2.3🔨)
- **v0.3** 🔨 NEXT: Angular Frontend (Phase 3.0✅, 3.1-3.7 incoming)
- **v1.0** (Target): Full Card Trader integration, Complete UI, Testing, Monitoring
- **v2.0** (Future): Multi-marketplace support, Advanced features, AI grading

---

## Success Criteria

✅ **Phase 1 Complete**: Database creato e migrazioni funzionanti

✅ **Phase 2.1 Complete**: Sync da Card Trader funzionante, data in DB

✅ **Phase 2.2 Complete**: Webhook processing con signature verification, handlers funzionanti

🔨 **Phase 2.3 In Progress**: Unit + Integration tests per webhook handlers e endpoints

✅ **Phase 3.0 Complete**: Angular 20 project setup, models, services, folder structure configured

🔨 **Phase 3.1 Next**: Dashboard & Inventory List components for database consultation

⏳ **Phase 3 Complete**: Angular UI funzionante, integrazione API completa, testabile in browser

⏳ **Phase 4 Complete**: API endpoints rispondono correttamente, pagination attiva

⏳ **Phase 5 Complete**: API resiliente con retry e caching

⏳ **Phase 6 Complete**: Almeno 1 marketplace aggiunto (eBay o Wallapop)

⏳ **Phase 7 Complete**: CI/CD pipeline attiva, deployable su cloud

⏳ **Phase 8 Complete**: Monitoring e health checks funzionanti

---

## Questions & Support

Consulta:
- `ARCHITECTURE.md` per design decisions
- `IMPLEMENTATION.md` per dettagli di codice
- `SPECIFICATIONS.md` per linee guida di development
