import { Component, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, GridApi, GridReadyEvent, IDatasource, IGetRowsParams } from 'ag-grid-community';
import { CardTraderApiService } from '../../../../core/services/cardtrader-api.service';
import { GridStateService } from '../../../../core/services/grid-state.service';
import { InventoryItem } from '../../../../core/models/inventory-item';

@Component({
  selector: 'app-inventory-list',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatCheckboxModule,
    MatTooltipModule,
    AgGridAngular
  ],
  templateUrl: './inventory-list.component.html',
  styleUrls: ['./inventory-list.component.scss']
})
export class InventoryListComponent implements OnInit {
  @ViewChild(AgGridAngular) agGrid!: AgGridAngular;

  private gridApi!: GridApi;
  private readonly GRID_ID = 'inventory-grid';

  // Infinite Scroll Settings
  cacheBlockSize = 100;
  maxBlocksInCache = 10;

  columnDefs: ColDef[] = [
    {
      field: 'id',
      sortable: false, // Sorting not supported by backend yet
      filter: 'agNumberColumnFilter',
      width: 80
    },
    {
      headerName: 'Card Name',
      field: 'blueprint.name',
      sortable: false,
      filter: true,
      width: 250
    },
    {
      headerName: 'Expansion',
      field: 'blueprint.expansion.name',
      sortable: false,
      filter: true,
      width: 200
    },
    {
      headerName: 'Quantity',
      field: 'quantity',
      sortable: false,
      filter: 'agNumberColumnFilter',
      width: 100
    },
    {
      headerName: 'Purchase Price',
      field: 'purchasePrice',
      sortable: false,
      filter: 'agNumberColumnFilter',
      width: 130,
      valueFormatter: (params) => params.value ? `€${params.value.toFixed(2)}` : ''
    },
    {
      headerName: 'Listing Price',
      field: 'listingPrice',
      sortable: false,
      filter: 'agNumberColumnFilter',
      width: 130,
      valueFormatter: (params) => params.value ? `€${params.value.toFixed(2)}` : ''
    },
    {
      headerName: 'Condition',
      field: 'condition',
      sortable: false,
      filter: true,
      width: 100
    },
    {
      headerName: 'Language',
      field: 'language',
      sortable: false,
      filter: true,
      width: 100
    },
    {
      headerName: 'Status',
      field: 'status',
      sortable: false,
      filter: true,
      width: 100
    },
    {
      headerName: 'Actions',
      field: 'actions',
      sortable: false,
      filter: false,
      width: 150,
      cellRenderer: (params: any) => {
        if (!params.data) return ''; // Loading row
        return `<button class="action-btn edit-btn">Edit</button>
                <button class="action-btn delete-btn">Delete</button>`;
      },
      onCellClicked: (params) => {
        const target = params.event?.target as HTMLElement;
        if (target.classList.contains('edit-btn')) {
          this.editItem(params.data);
        } else if (target.classList.contains('delete-btn')) {
          this.deleteItem(params.data);
        }
      }
    }
  ];

  defaultColDef: ColDef = {
    resizable: true,
    sortable: false, // Disable sorting globally for now
    filter: true
  };

  gridOptions = {
    // Pagination settings
    pagination: false, // Infinite scroll handles this
    rowModelType: 'infinite' as const,
    cacheBlockSize: this.cacheBlockSize,
    maxBlocksInCache: this.maxBlocksInCache,
    infiniteInitialRowCount: 100,

    // domLayout: 'autoHeight' as const, // REMOVED: Causes performance issues with infinite scroll
    enableCellTextSelection: true,
    suppressRowClickSelection: true,
    animateRows: true,

    // Menu settings
    suppressMenuHide: false,
    columnMenu: 'new' as const,
    suppressDragLeaveHidesColumns: true
  };

  constructor(
    private apiService: CardTraderApiService,
    private gridStateService: GridStateService,
    private snackBar: MatSnackBar
  ) { }

  ngOnInit(): void {
    // No initial load needed, grid calls getRows
  }

  onGridReady(params: GridReadyEvent): void {
    this.gridApi = params.api;

    // Restore saved grid state
    const savedState = this.gridStateService.loadGridState(this.GRID_ID);
    if (savedState?.columnState) {
      this.gridApi.applyColumnState({ state: savedState.columnState, applyOrder: true });
    }

    // Setup Infinite Scroll Datasource
    const dataSource: IDatasource = {
      getRows: (params: IGetRowsParams) => {
        const page = Math.floor(params.startRow / this.cacheBlockSize) + 1;

        console.log(`Fetching page ${page} (rows ${params.startRow} to ${params.endRow})`);

        this.apiService.getInventoryItems(page, this.cacheBlockSize).subscribe({
          next: (response) => {
            // If we reached the end, totalCount will tell grid to stop scrolling
            const lastRow = response.totalCount <= params.endRow ? response.totalCount : -1;
            params.successCallback(response.items || [], response.totalCount);
          },
          error: (err) => {
            console.error('Error fetching inventory:', err);
            params.failCallback();
            this.showSnackBar('Error loading inventory data');
          }
        });
      }
    };

    this.gridApi.setGridOption('datasource', dataSource);
    this.gridApi.sizeColumnsToFit();
  }

  saveGridState(): void {
    if (!this.gridApi) return;

    const columnState = this.gridApi.getColumnState();
    this.gridStateService.saveGridState(this.GRID_ID, {
      columnState,
      sortModel: [] // Sorting not supported yet
    });

    this.showSnackBar('Grid configuration saved');
  }

  resetGridState(): void {
    if (!this.gridApi) return;

    this.gridStateService.clearGridState(this.GRID_ID);
    this.gridApi.resetColumnState();
    this.gridApi.sizeColumnsToFit();

    this.showSnackBar('Grid configuration reset to defaults');
  }

  editItem(item: InventoryItem): void {
    console.log('Edit item:', item);
    // TODO: Open dialog to edit item
  }

  deleteItem(item: InventoryItem): void {
    if (confirm(`Sei sicuro di voler eliminare ${item.blueprint?.name}?`)) {
      this.apiService.deleteInventoryItem(item.id).subscribe({
        next: () => {
          this.showSnackBar('Item deleted successfully');
          this.gridApi.refreshInfiniteCache(); // Reload data
        },
        error: (err) => {
          console.error('Error deleting item:', err);
          this.showSnackBar('Error deleting item');
        }
      });
    }
  }

  private showSnackBar(message: string): void {
    this.snackBar.open(message, 'Close', {
      duration: 3000
    });
  }

  // Column Visibility Helper
  toggleColumnVisibility(colId: string): void {
    const col = this.gridApi.getColumn(colId);
    if (col) {
      this.gridApi.setColumnsVisible([colId], !col.isVisible());
      // this.saveGridState(); // Auto-save disabled
    }
  }

  isColumnVisible(colId: string): boolean {
    const col = this.gridApi?.getColumn(colId);
    return col ? col.isVisible() : true;
  }

  getAllColumns(): ColDef[] {
    return this.columnDefs.filter(col => col.field !== 'actions');
  }

  onColumnMoved(): void {
    // this.saveGridState(); // Auto-save disabled
  }

  onColumnVisible(): void {
    // this.saveGridState(); // Auto-save disabled
  }
}
