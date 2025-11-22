import { Component, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatMenuModule } from '@angular/material/menu';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, GridApi, GridReadyEvent } from 'ag-grid-community';

import { CardTraderApiService } from '../../../../core/services';
import { GridStateService } from '../../../../core/services/grid-state.service';
import { InventoryItem } from '../../../../core/models';

@Component({
  selector: 'app-inventory-list',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatMenuModule,
    MatSnackBarModule,
    AgGridAngular
  ],
  templateUrl: './inventory-list.component.html',
  styleUrls: ['./inventory-list.component.scss'],
})
export class InventoryListComponent implements OnInit {
  @ViewChild(AgGridAngular) agGrid!: AgGridAngular;

  private gridApi!: GridApi;
  private readonly GRID_ID = 'inventory-grid';

  items: InventoryItem[] = [];
  isLoading = false;

  columnDefs: ColDef[] = [
    {
      headerName: 'ID',
      field: 'id',
      sortable: true,
      filter: 'agNumberColumnFilter',
      width: 80
    },
    {
      headerName: 'Card Name',
      field: 'blueprint.name',
      sortable: true,
      filter: true,
      width: 250
    },
    {
      headerName: 'Expansion',
      field: 'blueprint.expansion.name',
      sortable: true,
      filter: true,
      width: 200
    },
    {
      headerName: 'Quantity',
      field: 'quantity',
      sortable: true,
      filter: 'agNumberColumnFilter',
      width: 100
    },
    {
      headerName: 'Purchase Price',
      field: 'purchasePrice',
      sortable: true,
      filter: 'agNumberColumnFilter',
      width: 130,
      valueFormatter: (params) => params.value ? `€${params.value.toFixed(2)}` : ''
    },
    {
      headerName: 'Listing Price',
      field: 'listingPrice',
      sortable: true,
      filter: 'agNumberColumnFilter',
      width: 130,
      valueFormatter: (params) => params.value ? `€${params.value.toFixed(2)}` : ''
    },
    {
      headerName: 'Condition',
      field: 'condition',
      sortable: true,
      filter: true,
      width: 100
    },
    {
      headerName: 'Language',
      field: 'language',
      sortable: true,
      filter: true,
      width: 100
    },
    {
      headerName: 'Status',
      field: 'status',
      sortable: true,
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
    sortable: true,
    filter: true
  };

  gridOptions = {
    pagination: false,
    domLayout: 'autoHeight' as const,
    enableCellTextSelection: true,
    suppressRowClickSelection: true,
    animateRows: true,
    sideBar: {
      toolPanels: [
        {
          id: 'columns',
          labelDefault: 'Columns',
          labelKey: 'columns',
          iconKey: 'columns',
          toolPanel: 'agColumnsToolPanel',
          toolPanelParams: {
            suppressRowGroups: true,
            suppressValues: true,
            suppressPivots: true,
            suppressPivotMode: true
          }
        }
      ],
      defaultToolPanel: ''
    }
  };

  constructor(
    private apiService: CardTraderApiService,
    private gridStateService: GridStateService,
    private snackBar: MatSnackBar
  ) { }

  ngOnInit(): void {
    this.loadInventory();
  }

  onGridReady(params: GridReadyEvent): void {
    this.gridApi = params.api;

    const savedState = this.gridStateService.loadGridState(this.GRID_ID);
    if (savedState?.columnState) {
      this.gridApi.applyColumnState({ state: savedState.columnState, applyOrder: true });
    }

    this.gridApi.sizeColumnsToFit();
  }

  loadInventory(): void {
    this.isLoading = true;
    // Load all items (no pagination for AG-Grid)
    this.apiService.getInventoryItems(1, 10000).subscribe({
      next: (response) => {
        this.items = response.items || [];
        this.isLoading = false;
      },
      error: (err) => {
        console.error('Error loading inventory:', err);
        this.showSnackBar('Error loading inventory');
        this.isLoading = false;
      }
    });
  }

  saveGridState(): void {
    if (!this.gridApi) return;

    const columnState = this.gridApi.getColumnState();
    this.gridStateService.saveGridState(this.GRID_ID, {
      columnState,
      sortModel: columnState.filter(col => col.sort != null)
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
          this.loadInventory();
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

  onColumnMoved(): void {
    this.saveGridState();
  }

  onColumnVisible(): void {
    this.saveGridState();
  }

  onSortChanged(): void {
    this.saveGridState();
  }
}
