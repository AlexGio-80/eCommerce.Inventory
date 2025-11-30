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
import { TabManagerService } from '../../../../core/services/tab-manager.service';
import { InventoryItem } from '../../../../core/models/inventory-item';

import { ImageCellRendererComponent } from '../../../../shared/components/image-cell-renderer/image-cell-renderer.component';
import { ConditionCellRendererComponent } from '../../../../shared/components/condition-cell-renderer/condition-cell-renderer.component';
import { LanguageCellRendererComponent } from '../../../../shared/components/language-cell-renderer/language-cell-renderer.component';

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
      headerName: 'Image',
      field: 'blueprint.imageUrl',
      sortable: false,
      filter: false,
      width: 80,
      cellRenderer: ImageCellRendererComponent
    },
    {
      headerName: 'Coll. #',
      field: 'blueprint.fixedProperties',
      sortable: false,
      filter: true,
      width: 80,
      valueGetter: (params) => {
        if (!params.data?.blueprint?.fixedProperties) return '';
        try {
          const props = JSON.parse(params.data.blueprint.fixedProperties);
          return props.collector_number || '';
        } catch {
          return '';
        }
      }
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
      width: 100,
      cellRenderer: ConditionCellRendererComponent
    },
    {
      headerName: 'Language',
      field: 'language',
      sortable: false,
      filter: true,
      width: 100,
      cellRenderer: LanguageCellRendererComponent
    },
    {
      headerName: 'Foil',
      field: 'isFoil',
      sortable: false,
      filter: true,
      width: 80,
      cellRenderer: (params: any) => params.value ? 'Yes' : 'No'
    },
    {
      headerName: 'Signed',
      field: 'isSigned',
      sortable: false,
      filter: true,
      width: 80,
      cellRenderer: (params: any) => params.value ? 'Yes' : 'No'
    },
    {
      headerName: 'Altered',
      field: 'isAltered',
      sortable: false,
      filter: true,
      width: 80,
      cellRenderer: (params: any) => params.value ? 'Yes' : 'No'
    },
    {
      headerName: 'Tag',
      field: 'tag',
      sortable: false,
      filter: true,
      width: 120
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
      width: 100,
      cellRenderer: (params: any) => {
        if (!params.data) return ''; // Loading row
        return `<button class="action-btn link-btn" title="Apri su CardTrader">
                  <mat-icon>open_in_new</mat-icon>
                </button>`;
      },
      onCellClicked: (params) => {
        const target = params.event?.target as HTMLElement;
        if (target.classList.contains('link-btn') || target.closest('.link-btn')) {
          this.openOnCardTrader(params.data);
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
    private tabManager: TabManagerService,
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

        // Extract filters from AG-Grid filter model
        const filters = this.buildFiltersFromModel(params.filterModel);

        console.log(`Fetching page ${page} (rows ${params.startRow} to ${params.endRow}) with filters:`, filters);

        this.apiService.getInventoryItems(page, this.cacheBlockSize, filters).subscribe({
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

  private buildFiltersFromModel(filterModel: any): any {
    const filters: any = {};

    // AG-Grid text filters have structure: { filter: "search text", filterType: "text", type: "contains" }
    if (filterModel['blueprint.name']) {
      filters.cardName = filterModel['blueprint.name'].filter;
    }
    if (filterModel['blueprint.expansion.name']) {
      filters.expansionName = filterModel['blueprint.expansion.name'].filter;
    }
    if (filterModel['condition']) {
      filters.condition = filterModel['condition'].filter;
    }
    if (filterModel['language']) {
      filters.language = filterModel['language'].filter;
    }

    return filters;
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

  openOnCardTrader(item: InventoryItem): void {
    if (item.blueprint?.cardTraderId) {
      const url = `https://www.cardtrader.com/cards/${item.blueprint.cardTraderId}`;
      window.open(url, '_blank');
    } else {
      this.showSnackBar('Questa carta non ha un ID CardTrader');
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

  // Image Preview Logic
  previewImage: string | null = null;

  onCellMouseOver(params: any): void {
    if (params.colDef.field === 'blueprint.imageUrl' && params.value) {
      this.previewImage = params.value;
    }
  }

  onCellMouseOut(params: any): void {
    if (params.colDef.field === 'blueprint.imageUrl') {
      this.previewImage = null;
    }
  }

  openCreateListing(): void {
    const tabId = this.tabManager.openTab('/layout/products/create', 'Nuovo Articolo', 'add_circle');
    this.tabManager.setActiveTab(tabId);
  }
}
