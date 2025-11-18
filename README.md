# 🃏 eCommerce.Inventory - Trading Card Marketplace Management System

A comprehensive .NET 10 solution for managing trading card inventory across multiple marketplaces (Card Trader, eBay, Wallapop, etc.) with real-time synchronization, webhook processing, and order management.

## 📋 Project Overview

eCommerce.Inventory is a microservices-ready backend API designed to:
- **Sync inventory** from multiple trading card marketplaces
- **Manage orders** and track fulfillment
- **Handle webhooks** for real-time updates
- **Maintain product hierarchy** (Games → Expansions → Blueprints → Inventory Items)
- **Track financial data** with precise decimal handling

## 🏗️ Architecture

Built with **Clean Architecture** (4-layer approach):

```
eCommerce.Inventory.Api              (Presentation Layer)
├── Controllers/                      API endpoints per marketplace
└── Models/                          Request/Response DTOs

eCommerce.Inventory.Application      (Application Layer)
├── Interfaces/                      Service contracts
└── Services/                        Business logic orchestration

eCommerce.Inventory.Domain           (Domain Layer)
└── Entities/                        Core business entities

eCommerce.Inventory.Infrastructure   (Infrastructure Layer)
├── Persistence/                     EF Core DbContext & Repositories
├── ExternalServices/                Marketplace API clients
└── Migrations/                      Database schema versions
```

### 🔐 Key Principles

- ✅ **SOLID Principles**: Single Responsibility, Open/Closed, Liskov Substitution, Interface Segregation, Dependency Inversion
- ✅ **Repository Pattern**: Abstracted data access
- ✅ **Dependency Injection**: All services registered in DI container
- ✅ **Async/Await**: Non-blocking I/O operations
- ✅ **Structured Logging**: Serilog with context enrichment

## 🚀 Getting Started

### Prerequisites

- **.NET 10 SDK** or later
- **SQL Server 2019+** (configured in `appsettings.Development.json`)
- **Visual Studio 2022** or VS Code with C# extension

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/yourusername/eCommerceApp.git
   cd eCommerceApp
   ```

2. **Restore dependencies**
   ```bash
   dotnet restore
   ```

3. **Configure the database**
   - Update connection string in `eCommerce.Inventory.Api/appsettings.Development.json`:
   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Server=YOUR_SERVER;Database=ECommerceInventory;Trusted_Connection=True;"
   }
   ```

4. **Apply migrations** (auto-executed on first run in Development)
   ```bash
   cd eCommerce.Inventory.Api
   dotnet ef database update --project ../eCommerce.Inventory.Infrastructure
   ```

5. **Run the application**
   ```bash
   dotnet run
   ```
   API will be available at `https://localhost:5001`

## 📊 Database Schema

### Entity Relationships

```
Games (1) ──────────── (Many) Expansions
             ↓
         Expansions (1) ──────────── (Many) Blueprints
                            ↓
                      Blueprints (1) ──────────── (Many) InventoryItems
                                            ↓
                                      InventoryItems (Many) ──────────── (Many) OrderItems (Many)
                                                                              ↓
                                                                          Orders
```

### Tables

| Table | Purpose | Key Columns |
|-------|---------|------------|
| **Games** | Game titles (MTG, Yu-Gi-Oh, Pokémon, etc.) | Id, CardTraderId, Name, Code |
| **Expansions** | Sets/Expansions per game | Id, GameId, CardTraderId, Name |
| **Blueprints** | Individual cards | Id, ExpansionId, CardTraderId, Name, Rarity |
| **InventoryItems** | Your inventory stock | Id, BlueprintId, Quantity, Condition, ListingPrice |
| **Orders** | Customer orders from marketplaces | Id, CardTraderOrderId, Status, TotalAmount |
| **OrderItems** | Items per order | Id, OrderId, InventoryItemId, QuantitySold |

## 🔌 API Endpoints

### Card Trader Inventory
```http
GET    /api/cardtrader/inventory              List all inventory items
GET    /api/cardtrader/inventory/{id}         Get item by ID
POST   /api/cardtrader/inventory              Create new item
PUT    /api/cardtrader/inventory/{id}         Update item
DELETE /api/cardtrader/inventory/{id}         Delete item

GET    /api/cardtrader/products               Sync products from Card Trader
POST   /api/cardtrader/webhooks/order         Receive order webhooks
POST   /api/cardtrader/sync/manual            Trigger manual sync
```

**Documentation**: See Swagger UI at `/swagger` in Development

## 🔄 Synchronization

### CardTraderSyncWorker

Runs as a background service:
- **Default interval**: 15 minutes (configurable)
- **Operations**:
  1. Sync Games & Expansions
  2. Sync Products (InventoryItems)
  3. Sync Orders from marketplace
  4. Handle merge logic (INSERT/UPDATE/DELETE)

### Webhook Processing

Real-time updates via CQRS pattern:
```csharp
[HttpPost("webhooks")]
public async Task<IActionResult> ProcessWebhook([FromBody] WebhookPayload payload)
{
    await _mediator.Send(new ProcessCardTraderWebhookCommand
    {
        Type = payload.Type,
        Data = payload.Data
    });
    return Ok();
}
```

## 📋 Development Status

### ✅ Phase 1: Database & Migrations (COMPLETED)
- Initial database schema with 6 tables
- Entity Framework Core migrations
- Seed data for test games, expansions, blueprints
- All relationships configured with cascade delete

### ✅ Phase 2.1: Card Trader Sync Integration (COMPLETED)
- DTOs → Domain Entities Mappers (CardTraderDtoMapper)
- Advanced database merge logic (INSERT/UPDATE/DELETE via InventorySyncService)
- Complete CardTraderSyncWorker with 3-step orchestration
- Asynchronous operations with CancellationToken support
- Scoped DI and service injection in background worker

### ✅ Phase 2.2: Webhook Processing (COMPLETED)
- MediatR CQRS command pattern implementation
- ProcessCardTraderWebhookCommand & ProcessCardTraderWebhookHandler
- Webhook signature verification (HMAC SHA256)
- REST endpoint: `POST /api/cardtraderwèbhooks/events`
- Support for order.create, order.update, order.destroy events
- Data retention: Orders preserved locally when deleted on Card Trader
- WebhookSignatureVerificationService with constant-time comparison

### ✅ Phase 2.3: Backend Testing (COMPLETED)
- 14 comprehensive unit & integration tests (100% passing)
- WebhookSignatureVerificationService tests (HMAC SHA256 validation)
- CardTraderWebhooksController integration tests
- ProcessCardTraderWebhookHandler MediatR tests
- Code coverage report generated with Coverlet XPlat
- Full signature verification, tampering detection, and payload validation coverage

### ✅ Phase 3.0: Angular Frontend - Project Setup (COMPLETED)
- Angular 20 standalone components setup
- Material Design integration
- Core folder structure (models, services, guards, interceptors)
- Features folder structure (inventory, orders, sync, reporting, products)
- HttpClient and RxJS reactive patterns configured
- API service skeleton with type-safe endpoints

### ✅ Phase 3.1: Database Consultation UI (COMPLETED)
- Dashboard Component with statistics cards (Total Products, Orders, Games, Last Sync)
- Inventory List Component with Material Data Table
- Material table with 6 columns (ID, Card Name, Quantity, Price, Status, Actions)
- Pagination support (10, 20, 50, 100 items per page)
- Filter controls (Game selector, Status dropdown, Search field)
- Edit and Delete item functionality with confirmation dialogs
- Responsive Material Design layout with hover effects
- Color-coded status indicators for inventory items and orders
- Loading spinners and empty state UI
- Build successful with 0 TypeScript errors
- Routes configured: `/dashboard` and `/inventory`

### ✅ Phase 3.2: Card Trader Data Initial Sync (COMPLETED)
- **Frontend Implementation**:
  - Sync Page Component with selective entity synchronization (Games, Categories, Expansions, Blueprints, Properties)
  - Material checkboxes for entity selection with Select All/Deselect All functionality
  - Real-time sync statistics display (Added, Updated, Failed counts)
  - Detailed synchronization log with timestamp, type-based color coding, and scrollable history
  - Auto-hide of success messages after 5 seconds for improved UX
  - LocalStorage persistence of last sync time
  - Responsive sidebar layout with navigation integration
  - Build successful with 0 TypeScript errors
  - Routes configured: `/sync` page with full Material Design integration

- **Backend Implementation**:
  - `CardTraderSyncController` with POST `/api/cardtrader/sync` endpoint
  - `SyncRequestDto` for selective entity synchronization
  - `SyncResponseDto` with detailed statistics per entity type
  - `CardTraderSyncOrchestrator` service orchestrating Games → Expansions → Blueprints sync flow
  - Upsert logic (INSERT new entities, UPDATE existing ones based on CardTraderId)
  - Dynamic type conversion using JSON serialization for flexible DTO handling
  - Proper HTTP BaseAddress configuration with trailing slash handling
  - CORS middleware ordering fix for proper preflight handling
  - HTTPS redirect disabled in Development environment
  - `CardTraderGameDto` with JsonPropertyName attributes for proper API response deserialization
  - `CardTraderGamesResponseDto` wrapper for handling wrapped API responses
  - Updated DTOs to match Card Trader API response structure
  - All compilation errors resolved (0 errors, 31 warnings)
  - Full end-to-end sync flow tested from UI to database

### ✅ Phase 3.3: Category and Property Synchronization (COMPLETED)
- **Backend Implementation**:
  - `ICardTraderApiService` interface updated with `SyncCategoriesAsync` method
  - `CardTraderApiClient` implementation with dual-format deserialization support (wrapper and direct array)
  - `CardTraderCategoryDto` and `CardTraderPropertyDto` DTOs for API response mapping
  - `CardTraderCategoriesResponseDto` wrapper for handling wrapped API responses
  - `InventorySyncService.SyncCategoriesAsync` with proper foreign key mapping
  - Database ID resolution: CardTraderId → Database Game ID mapping for referential integrity
  - Proper handling of nested Properties and PropertyValues during sync
  - Graceful skipping of orphaned categories (references to non-existent games)
  - INSERT/UPDATE logic for categories with full property cascade
  - Error logging and statistics tracking (Added, Updated, Skipped counts)
  - All 42 categories successfully synced from Card Trader API
  - Foreign key constraint resolved through proper ID mapping

### ✅ Phase 3.4: Expansions Synchronization (COMPLETED)
- **Backend Implementation**:
  - `CardTraderExpansionDto` with `JsonPropertyName` attributes for snake_case API mapping
    * Maps JSON fields: id, name, code, game_id
  - Fixed deserialization to handle API response format correctly
  - `CardTraderDtoMapper.MapExpansion` with intelligent defaults:
    * Missing Name: generates "Expansion {id}"
    * Missing Abbreviation: generates from first 3 letters of expansion name
  - `CardTraderSyncOrchestrator.UpsertExpansionsAsync` with proper GameId mapping
    * Loads all games and maps CardTraderId → Database Game.Id
    * Validates game references before inserting/updating expansions
    * Gracefully skips expansions with non-existent game references
  - Error logging with specific warnings for orphaned records
  - Statistics tracking (Added, Updated, Failed, Skipped counts)
  - All 42 expansions successfully synced with correct foreign key references
  - Test data examples stored in `Features/Expansions.Data.Examples.json`

### ⏳ Upcoming Phases
- Phase 3.5: Blueprints & Products Synchronization Testing
- Phase 4: API Controller Enhancement (Pagination, Response Envelopes, Error Handling)
- Phase 5: Advanced Features (Polly Resilience, Caching, Rate Limiting)
- Phase 6: Marketplace Expansion (eBay, Wallapop)
- Phase 7: DevOps & Deployment (Docker, CI/CD)
- Phase 8: Monitoring & Analytics

See [ROADMAP.md](./Documentation/ROADMAP.md) for detailed timeline and technical specifications.

## 🛠️ Technology Stack

| Layer | Technologies |
|-------|--------------|
| **Framework** | .NET 10, ASP.NET Core |
| **Database** | SQL Server, Entity Framework Core 10 |
| **Logging** | Serilog 4.2.0 with structured logging |
| **API** | RESTful with OpenAPI/Swagger |
| **CQRS** | MediatR 12.3.0 for command handling |
| **Webhooks** | HMAC SHA256 signature verification |
| **DI** | Microsoft.Extensions.DependencyInjection |
| **Background Tasks** | BackgroundService (HostedService) |
| **Async** | Task-based async/await |
| **JSON** | System.Text.Json for deserialization |

## 📝 Configuration

### appsettings.Development.json (⚠️ NOT in git)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=DEV-ALEX\\MSSQLSERVER01;Database=ECommerceInventory;Trusted_Connection=True;"
  },
  "CardTraderApi": {
    "BaseUrl": "https://api.cardtrader.com/api/v2",
    "BearerToken": "YOUR_TOKEN_HERE",
    "SharedSecret": "YOUR_WEBHOOK_SHARED_SECRET_HERE"
  },
  "Serilog": {
    "MinimumLevel": "Debug",
    "WriteTo": [
      { "Name": "Console" },
      { "Name": "File", "Args": { "path": "logs/ecommerce-inventory-.txt", "rollingInterval": "Day" } }
    ]
  }
}
```

## 🔐 Security Considerations

- ✅ Connection strings in appsettings (not git-tracked)
- ✅ API tokens in configuration (not hardcoded)
- ✅ Structured logging (no sensitive data logged)
- ⏳ HTTPS enforced in Production
- ⏳ Rate limiting (Phase 5)
- ⏳ Input validation (Phase 3)

## 🧪 Testing Strategy

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test eCommerce.Inventory.Tests

# Coverage report
dotnet test /p:CollectCoverage=true
```

**Target coverage**: 80%+ of domain and infrastructure layers

## 📚 Documentation

- [ROADMAP.md](./Documentation/ROADMAP.md) - Detailed development timeline and phases
- [SPECIFICATIONS.md](./Documentation/SPECIFICATIONS.md) - Technical specifications and guidelines
- [ARCHITECTURE.md](./Documentation/ARCHITECTURE.md) - Architecture decision records
- [IMPLEMENTATION.md](./Documentation/IMPLEMENTATION.md) - Implementation details and Phase completion notes

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/your-feature`
3. Commit changes following conventions in [SPECIFICATIONS.md](./Documentation/SPECIFICATIONS.md)
4. Push to branch: `git push origin feature/your-feature`
5. Submit a Pull Request

### Commit Message Format
```
<type>(<scope>): <subject>

<body>

<footer>
```

**Types**: feat, fix, docs, style, refactor, test, chore

## 📄 License

This project is licensed under the MIT License - see LICENSE file for details.

## 👨‍💻 Author

**Alessandro** - Project Lead
Trading Card Inventory Management System
Started: November 2024

## 🙏 Acknowledgments

- **Clean Architecture** principles by Robert C. Martin
- **Entity Framework Core** documentation
- **Card Trader API** for marketplace integration
- **Serilog** for structured logging
- **GitHub GitIgnore** templates

## 📞 Support

For issues, questions, or suggestions:
1. Check [SPECIFICATIONS.md](./Documentation/SPECIFICATIONS.md) for guidelines
2. Open an issue on GitHub
3. Review existing documentation in `/Documentation` folder

---

**Last Updated**: November 18, 2024 (Phase 3.4 Complete)
**Version**: 0.5 (Expansions Synchronization Complete)
**Status**: Active Development - Phase 3.4 Complete | Phase 3.5 Next
**Completed Phases**: Phase 1 ✅ | Phase 2.1 ✅ | Phase 2.2 ✅ | Phase 2.3 ✅ | Phase 3.0 ✅ | Phase 3.1 ✅ | Phase 3.2 ✅ | Phase 3.3 ✅ | Phase 3.4 ✅
