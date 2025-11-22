# Changelog - 2025-11-22

## Phase 3.5: Advanced Grid Features - COMPLETATO ✅

### Panoramica
Implementazione completa di AG-Grid per tutte le griglie dati dell'applicazione, con ottimizzazioni di performance e standardizzazione dell'interfaccia utente.

### Modifiche Backend

#### 1. Ottimizzazione "To Prepare" List
- **Nuovo DTO**: `UnpreparedItemDto` per trasferimento dati ottimizzato
  - Include tutti i campi necessari (OrderCode, BuyerUsername, ImageUrl)
  - Evita problemi di serializzazione con grafi di entità complessi
- **Repository**: `OrderRepository.GetUnpreparedItemsAsync`
  - Usa LINQ projection per query ottimizzate
  - Restituisce direttamente `UnpreparedItemDto` dal database
- **Controller**: Nuovo endpoint `GET /api/cardtrader/orders/unprepared-items`
- **Relazione**: Aggiunta navigazione `Blueprint` a `OrderItem`
  - Permette accesso alle immagini delle carte
  - Migration: `AddOrderItemBlueprintRelationNullable`
  - Gestione dati esistenti (set BlueprintId=NULL per valori invalidi)

#### 2. Paginazione Server-Side Inventory
- **Repository**: `InventoryItemRepository.GetPagedAsync`
  - Supporto per paginazione lato server
  - Parametri: page, pageSize
- **Controller**: `CardTraderInventoryController.GetAllInventoryItems`
  - Restituisce `PagedResponse<InventoryItem>`
  - Ottimizzato per grandi dataset

#### 3. Filtri Backend Orders
- **Repository**: `OrderRepository.GetOrdersWithItemsAsync`
  - Parametri: from, to, excludeNullDates
  - Filtri applicati direttamente nella query SQL
- **Controller**: `CardTraderOrdersController.GetOrders`
  - Accetta parametri di filtro via query string

### Modifiche Frontend

#### 1. AG-Grid Standardization
Applicato AG-Grid a tutte le liste:
- **OrdersListComponent**: Master-detail con filtri data
- **InventoryListComponent**: Infinite scroll (server-side pagination)
- **UnpreparedItemsComponent**: Convertito da mat-table a AG-Grid
- **GamesPageComponent**: Standardizzato con menu colonne
- **ExpansionsPageComponent**: Standardizzato con menu colonne

#### 2. Features Implementate
- **Column Visibility Menu**: Menu custom con Material Design
  - Accessibile tramite pulsante "more_vert"
  - Checkbox per mostrare/nascondere colonne
- **State Persistence**: Salvataggio configurazione in localStorage
  - Ordine colonne
  - Visibilità colonne
  - Stato ordinamento
  - **Manuale**: Salvataggio solo tramite pulsante "Save Configuration"
- **Performance**: 
  - Rimosso `domLayout: 'autoHeight'` da Inventory (causava rendering completo)
  - Infinite Row Model per caricamento dati on-demand
  - Query ottimizzate lato backend

#### 3. Servizi
- **GridStateService**: Gestione centralizzata dello stato delle griglie
  - `saveGridState(gridId, state)`
  - `loadGridState(gridId)`
  - `clearGridState(gridId)`

### Bug Fixes

1. **TypeScript Compilation**
   - Fixed: `rowModelType: 'infinite' as const` in InventoryListComponent
   - Risolto errore di tipo incompatibile con GridOptions

2. **Database Migration**
   - Fixed: Constraint FK violation durante migrazione OrderItem-Blueprint
   - Soluzione: UPDATE per impostare BlueprintId=NULL per valori invalidi

3. **Auto-Save Disabilitato**
   - Rimossi listener automatici per `columnMoved`, `columnVisible`, `sortChanged`
   - Salvataggio solo manuale tramite pulsante menu

### Files Modificati

#### Backend
- `eCommerce.Inventory.Application/DTOs/UnpreparedItemDto.cs` (NEW)
- `eCommerce.Inventory.Application/Interfaces/IOrderRepository.cs`
- `eCommerce.Inventory.Infrastructure/Persistence/Repositories/OrderRepository.cs`
- `eCommerce.Inventory.Infrastructure/Persistence/Repositories/InventoryItemRepository.cs`
- `eCommerce.Inventory.Api/Controllers/CardTrader/CardTraderOrdersController.cs`
- `eCommerce.Inventory.Api/Controllers/CardTrader/CardTraderInventoryController.cs`
- `eCommerce.Inventory.Domain/Entities/OrderItem.cs`
- `eCommerce.Inventory.Infrastructure/Migrations/20251122175538_AddOrderItemBlueprintRelationNullable.cs` (NEW)

#### Frontend
- `src/app/core/models/unprepared-item-dto.ts` (NEW)
- `src/app/core/services/cardtrader-api.service.ts`
- `src/app/core/services/grid-state.service.ts` (EXISTING)
- `src/app/features/orders/orders-list/orders-list.component.ts`
- `src/app/features/orders/orders-list/orders-list.component.html`
- `src/app/features/orders/orders-list/orders-list.component.css`
- `src/app/features/inventory/pages/inventory-list/inventory-list.component.ts`
- `src/app/features/inventory/pages/inventory-list/inventory-list.component.html`
- `src/app/features/inventory/pages/inventory-list/inventory-list.component.scss`
- `src/app/features/orders/unprepared-items/unprepared-items.component.ts`
- `src/app/features/orders/unprepared-items/unprepared-items.component.html`
- `src/app/features/orders/unprepared-items/unprepared-items.component.css`
- `src/app/features/games/pages/games-page.component.ts` (VERIFIED)
- `src/app/features/expansions/pages/expansions-page.component.ts` (VERIFIED)

### Testing
- ✅ Backend compila senza errori
- ✅ Frontend compila senza errori
- ✅ Migration applicata con successo
- ✅ Tutti i servizi in esecuzione (Backend: http://localhost:5152, Frontend: http://127.0.0.1:4200)

### Prossimi Passi (Phase 3.6-3.7)
- Export dati (CSV, Excel)
- Filtri avanzati
- Bulk operations
- Reporting e Analytics
