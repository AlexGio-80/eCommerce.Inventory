# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Scheduled Full Sync Worker**: Enhanced nightly sync to include all entities (Games, Expansions, Blueprints, Inventory, Orders) with comprehensive logging
- **Roadmap Note**: Added periodic review reminder for card grading recognition technology

### Changed
- **Sync Worker Logging**: Improved log format with clear separators and detailed entity-level statistics

## [1.0.0] - 2025-11-28

### Added
- **Phase 6: Rate Limiting & Backup System**
  - Outbound rate limiting for Card Trader API (20 requests/minute)
  - Comprehensive daily backup system (database + application files)
  - Configurable backup schedule and retention policy
- **Phase 5: Deployment**
  - Windows Service deployment for backend API
  - IIS hosting for Angular frontend
  - Automated deployment scripts (`publish.ps1`, `setup-iis.ps1`)

## [0.9.0] - 2025-11-27

### Added
- **Phase 3.12: Authentication & Security**
  - JWT Bearer Token authentication
  - User entity with BCrypt password hashing
  - Login/logout functionality
  - AuthGuard for route protection
  - AuthInterceptor for automatic token attachment

### Fixed
- Admin user seeding logic (independent check for admin username)
- CORS configuration for authentication support
- API URL port correction (5155 → 5152)

## [0.8.0] - 2025-11-25

### Added
- **Phase 3.11: Reporting & Analytics System**
  - Sales Dashboard (revenue, orders, AOV, growth metrics)
  - Inventory Analytics (value distribution, slow-movers)
  - Profitability Analysis (profit margins, ROI, top performers)
  - 10 reporting endpoints with Chart.js visualizations
  - AG-Grid integration for data tables

### Fixed
- Property name mismatches in reporting DTOs
- API URL correction (localhost:5000 → localhost:5152)
- Safe navigation operators in templates
- Blueprint search field overlapping text
- Pending listings display issue (response.data.items unwrapping)

## [0.7.0] - 2025-11-24

### Added
- **Phase 3.8-3.10: Orders Grid Enhancements**
  - Multi-column sorting (Shift+Click)
  - Grid state persistence (column dimensions, order, visibility, sort)
  - Visual improvements (colored badges, flag icons, foil star icon)
  - Card Trader integration (opens product page when marking items as prepared)
  - Auto-sync for unprepared items (every 5 minutes when tab is active)

### Fixed
- Missing form fields in product listing UI
- Price suggestions (Local Blueprint ID → Card Trader Blueprint ID)
- JSON deserialization for marketplace products
- Tag sync payload structure
- Dashboard N+1 query issue (added AsNoTracking)
- Grid loading issue on first visit

## [0.6.0] - 2025-11-23

### Added
- **Phase 4: API Controller Enhancement**
  - `ApiResponse<T>` generic response envelope
  - `PagedResponse<T>` for pagination
  - GlobalExceptionMiddleware for centralized error handling
  - Server-side filtering for inventory (card name, expansion, condition, language)

### Changed
- Migrated CardTraderInventoryController and CardTraderOrdersController to new response format
- Frontend service updated to unwrap ApiResponse envelope

### Fixed
- Massive performance issue (removed virtual keyword from InventoryItem.OrderItems)
- Empty inventory grid (response unwrapping)

## [0.5.0] - 2025-11-22

### Added
- **Phase 3.5: Advanced Grid Features**
  - AG-Grid standardization across all data grids
  - Column visibility menu with Material Design
  - Grid state persistence (manual save)
  - Server-side pagination for inventory
  - Infinite scroll support

### Changed
- Converted all mat-table grids to AG-Grid
- Optimized "To Prepare" list with UnpreparedItemDto
- Added OrderItem-Blueprint relationship

### Fixed
- TypeScript compilation error (rowModelType)
- Database migration constraint FK violation
- Performance issue (removed domLayout: 'autoHeight')

## [0.4.0] - 2025-11-21

### Added
- **Phase 3.3-3.4: Product Listing & Webhook Integration**
  - Pending listings system (queue-based workflow)
  - Price suggestions from Card Trader marketplace
  - SignalR real-time notifications
  - Webhook integration for live inventory updates

## [0.3.0] - 2025-11-20

### Added
- **Phase 3.1-3.2: Frontend Foundation**
  - Angular 20 project setup
  - Dashboard with KPI cards
  - Inventory list with Material table
  - Card Trader data initial sync
  - Games and Expansions management pages

## [0.2.0] - 2025-11-19

### Added
- **Phase 2: Backend Integration**
  - Card Trader API client with DTOs
  - Inventory sync service
  - Webhook processing (order.create, order.update, order.destroy)
  - WebhookSignatureVerificationService (HMAC SHA256)
  - MediatR command handling
  - Backend unit and integration tests (14 passing)

## [0.1.0] - 2025-11-18

### Added
- **Phase 1: Database & Architecture**
  - Clean Architecture (4 layers: Domain, Application, Infrastructure, API)
  - Entity Framework Core with SQL Server
  - Initial migration (Games, Expansions, Blueprints, InventoryItems, Orders, OrderItems)
  - Repository pattern
  - Serilog logging configuration
  - Seed data for testing

---

## Legend

- **Added**: New features
- **Changed**: Changes in existing functionality
- **Deprecated**: Soon-to-be removed features
- **Removed**: Removed features
- **Fixed**: Bug fixes
- **Security**: Security improvements
