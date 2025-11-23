# Changelog - 2025-11-23

## Phase 3.7: Testing & QA

### Backend Testing
- **New Unit Tests:**
  - Created `InventorySyncServiceTests.cs` with 3 comprehensive tests for product synchronization logic
  - Tests cover INSERT, UPDATE, and empty list handling scenarios
  - All tests use EF Core InMemory database for isolation

- **Test Fixes:**
  - Fixed `ProcessCardTraderWebhookHandlerTests.cs` - added missing `INotificationService` mock parameter
  - All 3 webhook handler tests now passing

- **Test Results:**
  - Total: 21 tests
  - Passed: ✅ 21/21
  - Failed: ❌ 0

### Frontend/E2E Testing
- **Cypress E2E Framework:**
  - Installed Cypress `^15.7.0` via npm
  - Created `cypress.config.ts` with base configuration
  - BaseURL: `http://localhost:4200`
  - Spec pattern: `cypress/e2e/**/*.cy.ts`

- **Sample E2E Test:**
  - Created `inventory.cy.ts` for Inventory page testing
  - Tests: page title visibility, AG-Grid rendering, column headers
  - Configured with proper route: `/layout/inventory`
  - Timeout: 10 seconds for async grid loading

### Technical Details
- **Testing Strategy:**
  - Backend: xUnit + Moq + FluentAssertions + InMemory EF Core
  - Frontend: Jasmine/Karma (existing) + Cypress (new E2E)
  - Integration tests use existing framework with mock dependencies

### Documentation
- Created comprehensive walkthrough in `walkthrough.md`
- Documented all test execution commands
- Noted pre-existing frontend test failure (unrelated to Phase 3.7)

---

## Phase 3.6: Export, Advanced Filtering & Bulk Operations (Orders List Prototype)

### New Features
- **Export Functionality**:
  - Implemented `ExportService` for centralized export logic.
  - Added CSV and Excel export options to `OrdersListComponent`.
  - Added "Export Selected" capability to export only user-selected rows.
  - Integrated `xlsx` library for robust Excel generation with proper data formatting (dates, currency).

- **Advanced Filtering**:
  - Added "Quick Filter" global search bar to `OrdersListComponent`.
  - Implemented Filter Presets dropdown:
    - "All Orders"
    - "Incomplete Orders"
    - "Has Unprepared Items" (UI placeholder)
    - "Today's Orders"
  - Added "Clear Filters" button to reset all grid filters, search text, and presets.
  - Enhanced `GridStateService` to persist filter state and quick filter text in `localStorage`.

- **Bulk Operations**:
  - Enabled multi-row selection in `OrdersListComponent`.
  - Added "Bulk Actions Toolbar" that appears when rows are selected.
  - Implemented "Mark as Complete" and "Mark as Incomplete" bulk actions.
  - Created reusable `ConfirmDialogComponent` for safe bulk operations.
  - Added automatic grid refresh and success/error notifications after bulk actions.

- **UI/UX Improvements**:
  - Standardized AG-Grid styling with a shared theme (`ag-grid-theme.scss`).
  - Migrated all 5 grid components to use the shared theme.
  - Improved responsive layout for grid toolbars.

### Technical Improvements
- **Build Optimization**:
  - Moved AG-Grid base CSS imports to global `styles.css` to resolve Angular build budget errors.
  - Increased build budgets in `angular.json` to accommodate `xlsx` and AG-Grid dependencies.

### Known Issues / Future Work
- **Replication**: Full replication of these features to `InventoryList`, `UnpreparedItems`, `GamesPage`, and `ExpansionsPage` is deferred to a future phase due to component-specific complexities (e.g., Infinite Scroll in Inventory).
- **Backend Optimization**: Bulk operations currently make individual API calls per item. A dedicated backend bulk endpoint is recommended for performance.

### Testing
- **Manual Testing**:
  - Verified CSV and Excel export (all rows and selected rows).
  - Verified Quick Filter and Filter Presets.
  - Verified Grid State persistence (refreshing page restores filters).
  - Verified Bulk Mark Complete/Incomplete with confirmation dialogs.
  - Verified Build success (development and production configurations).

---

## Phase 3.4: Webhook Integration & Real-time Updates

### New Features
- **Real-time Notifications (SignalR)**:
  - Implemented `NotificationHub` in the backend to manage WebSocket connections.
  - Created `SignalRNotificationService` to broadcast `OrderCreated` and `OrderUpdated` events.
  - Integrated `@microsoft/signalr` in the Angular frontend.
  - Created `SignalRService` to handle connection lifecycle and event subscriptions.
  - Added `MatSnackBar` notifications for real-time alerts on new/updated orders.

- **Real-time Order Monitoring**:
  - Developed `OrderStatusMonitorComponent` to display a live feed of recent order events.
  - Integrated the monitor into the `OrdersListComponent` dashboard.

- **Inventory Auto-update**:
  - Backend `ProcessCardTraderWebhookHandler` now triggers order synchronization upon receiving webhooks.
  - Frontend listens for events and allows for grid refresh (manual trigger implemented for stability, auto-refresh capability ready).

### Bug Fixes
- **Order Sync FK Violation**:
  - Fixed a critical bug in `InventorySyncService.SyncOrdersAsync` where `OrderItems` were being saved with external `CardTraderId` as `BlueprintId`, causing Foreign Key constraint violations.
  - Implemented a pre-fetch and mapping logic to translate external Blueprint IDs to internal Database IDs before saving.
  - Added handling for missing blueprints (setting `BlueprintId` to null and logging a warning) to prevent sync failures.

- **Frontend Build Fixes**:
  - Resolved multiple TypeScript errors in `inventory-list.component.ts` (corrupted file restoration).
  - Fixed HTML template errors in `orders-list.component.html`.
  - Corrected module imports and exports for `CardTraderApiService` and `SignalRService`.

### Technical Details
- **Architecture**:
  - Decoupled notification logic using `INotificationService` interface in Application layer.
  - Maintained Clean Architecture by implementing SignalR logic in API layer and injecting it into Infrastructure.
- **Configuration**:
  - Added CORS policy "AllowAll" for development ease.
  - Configured SignalR endpoint at `/notificationHub`.
