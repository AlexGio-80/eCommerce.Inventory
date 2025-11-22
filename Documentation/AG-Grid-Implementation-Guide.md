# AG-Grid Implementation Guide

## Overview
This document describes the AG-Grid implementation for the Orders List component, which can be reused for other grids in the application.

## Features Implemented
- ✅ **Column Sorting**: Click column headers to sort
- ✅ **Column Reordering**: Drag and drop columns to reorder
- ✅ **Column Visibility**: Show/hide columns via column menu
- ✅ **Grid State Persistence**: Configuration saved to localStorage
- ✅ **Auto-save**: Grid state automatically saved on changes
- ✅ **Manual Controls**: Save and Reset buttons in grid menu

## Files Created/Modified

### New Files
1. **`grid-state.service.ts`** - Service for managing grid state persistence
   - `saveGridState(gridId, state)` - Save grid configuration
   - `loadGridState(gridId)` - Load saved configuration
   - `clearGridState(gridId)` - Reset to defaults

### Modified Files
1. **`orders-list.component.ts`** - Replaced Material Table with AG-Grid
2. **`orders-list.component.html`** - New AG-Grid template
3. **`orders-list.component.css`** - AG-Grid Material theme styling

## Usage Pattern

### 1. Component Setup
```typescript
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, GridApi, GridReadyEvent } from 'ag-grid-community';
import { GridStateService } from '../../../core/services/grid-state.service';

@Component({
  // ... imports including AgGridAngular
})
export class YourComponent {
  @ViewChild(AgGridAngular) agGrid!: AgGridAngular;
  private gridApi!: GridApi;
  private readonly GRID_ID = 'your-grid-id';

  columnDefs: ColDef[] = [
    {
      headerName: 'Column Name',
      field: 'fieldName',
      sortable: true,
      filter: true,
      width: 150
    }
    // ... more columns
  ];

  defaultColDef: ColDef = {
    resizable: true,
    sortable: true,
    filter: true
  };

  constructor(private gridStateService: GridStateService) {}
}
```

### 2. Grid Ready Handler
```typescript
onGridReady(params: GridReadyEvent): void {
  this.gridApi = params.api;
  
  // Restore saved state
  const savedState = this.gridStateService.loadGridState(this.GRID_ID);
  if (savedState?.columnState) {
    this.gridApi.applyColumnState({ 
      state: savedState.columnState, 
      applyOrder: true 
    });
  }
  
  this.gridApi.sizeColumnsToFit();
}
```

### 3. Auto-save Events
```typescript
onColumnMoved(): void {
  this.saveGridState();
}

onColumnVisible(): void {
  this.saveGridState();
}

onSortChanged(): void {
  this.saveGridState();
}

saveGridState(): void {
  if (!this.gridApi) return;
  
  const columnState = this.gridApi.getColumnState();
  this.gridStateService.saveGridState(this.GRID_ID, {
    columnState,
    sortModel: columnState.filter(col => col.sort != null)
  });
}
```

### 4. Template
```html
<ag-grid-angular
  style="width: 100%; height: 600px;"
  class="ag-theme-material"
  [rowData]="data"
  [columnDefs]="columnDefs"
  [defaultColDef]="defaultColDef"
  (gridReady)="onGridReady($event)"
  (columnMoved)="onColumnMoved()"
  (columnVisible)="onColumnVisible()"
  (sortChanged)="onSortChanged()"
>
</ag-grid-angular>
```

### 5. Styles
```css
@import 'ag-grid-community/styles/ag-grid.css';
@import 'ag-grid-community/styles/ag-theme-material.css';

.ag-theme-material {
  --ag-header-background-color: #3f51b5;
  --ag-header-foreground-color: white;
  --ag-odd-row-background-color: #f5f5f5;
}
```

## Column Definition Options

### Basic Column
```typescript
{
  headerName: 'Display Name',
  field: 'dataField',
  sortable: true,
  filter: true,
  width: 150
}
```

### Number Column with Formatting
```typescript
{
  headerName: 'Price',
  field: 'price',
  sortable: true,
  filter: 'agNumberColumnFilter',
  valueFormatter: (params) => params.value ? `€${params.value.toFixed(2)}` : ''
}
```

### Date Column
```typescript
{
  headerName: 'Date',
  field: 'date',
  sortable: true,
  filter: 'agDateColumnFilter',
  valueFormatter: (params) => params.value ? new Date(params.value).toLocaleDateString() : ''
}
```

### Custom Cell Renderer (Checkbox)
```typescript
{
  headerName: 'Active',
  field: 'isActive',
  cellRenderer: (params: any) => {
    const checkbox = document.createElement('input');
    checkbox.type = 'checkbox';
    checkbox.checked = params.value;
    checkbox.addEventListener('change', () => {
      this.handleCheckboxChange(params.data, checkbox.checked);
    });
    return checkbox;
  }
}
```

### Action Column
```typescript
{
  headerName: 'Actions',
  field: 'actions',
  sortable: false,
  filter: false,
  cellRenderer: (params: any) => '<button class="action-btn">View</button>',
  onCellClicked: (params) => this.handleAction(params.data)
}
```

## Grid Options
```typescript
gridOptions = {
  pagination: false,
  domLayout: 'autoHeight' as const,
  enableCellTextSelection: true,
  suppressRowClickSelection: true,
  rowSelection: 'single' as const,
  animateRows: true,
  columnMenu: 'new' as const,
  suppressDragLeaveHidesColumns: true
};
```

## Next Steps: Extending to Other Grids

### 1. Inventory Grid
- Copy pattern from Orders List
- Update column definitions for inventory fields
- Use unique GRID_ID: `'inventory-grid'`

### 2. Products Grid
- Similar implementation
- Add product-specific columns
- Use GRID_ID: `'products-grid'`

### 3. Blueprints Grid
- Implement with blueprint fields
- Use GRID_ID: `'blueprints-grid'`

## Benefits
- **User Customization**: Users can arrange columns as they prefer
- **Persistence**: Configuration survives page refreshes
- **Performance**: AG-Grid handles large datasets efficiently
- **Consistency**: Same UX across all grids
- **Reusability**: GridStateService works for all grids

## Testing Checklist
- [ ] Column sorting works (ascending/descending)
- [ ] Columns can be dragged to reorder
- [ ] Column visibility can be toggled
- [ ] Grid state persists after page refresh
- [ ] Save button saves current configuration
- [ ] Reset button restores defaults
- [ ] Filters work correctly
- [ ] Responsive on mobile devices
