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

### 🔄 Phase 2: Card Trader API Integration (IN PROGRESS)
- Parse DTOs → Domain Entities (Mappers)
- Database merge logic (INSERT/UPDATE/DELETE)
- Complete CardTraderSyncWorker implementation
- Webhook processing with MediatR

### ⏳ Upcoming Phases
- Phase 3: API Controller Enhancement (Pagination, Response Envelopes, Error Handling)
- Phase 4: Testing (Unit, Integration, E2E)
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
| **Logging** | Serilog with structured logging |
| **API** | RESTful with OpenAPI/Swagger |
| **DI** | Microsoft.Extensions.DependencyInjection |
| **Background Tasks** | BackgroundService (HostedService) |
| **Async** | Task-based async/await |

## 📝 Configuration

### appsettings.Development.json (⚠️ NOT in git)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=DEV-ALEX\\MSSQLSERVER01;Database=ECommerceInventory;Trusted_Connection=True;"
  },
  "CardTraderApi": {
    "BaseUrl": "https://api.cardtrader.com/api/v2",
    "BearerToken": "YOUR_TOKEN_HERE"
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
- [IMPLEMENTATION.md](./Documentation/IMPLEMENTATION.md) - Implementation details (coming soon)

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

**Last Updated**: November 18, 2024
**Version**: 0.1 (Architecture & Database Schema)
**Status**: Active Development
