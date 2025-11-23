# Changelog - 2025-11-23

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
