import { Component, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatMenuModule } from '@angular/material/menu';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, GridApi, GridReadyEvent } from 'ag-grid-community';

import { CardTraderApiService } from '../../../core/services/cardtrader-api.service';
import { GridStateService } from '../../../core/services/grid-state.service';
import { UnpreparedItemDto } from '../../../core/models';

import { ImageCellRendererComponent } from '../../../shared/components/image-cell-renderer/image-cell-renderer.component';
import { ConditionCellRendererComponent } from '../../../shared/components/condition-cell-renderer/condition-cell-renderer.component';
import { LanguageCellRendererComponent } from '../../../shared/components/language-cell-renderer/language-cell-renderer.component';
import { FoilCellRendererComponent } from '../../../shared/components/foil-cell-renderer/foil-cell-renderer.component';

@Component({
    selector: 'app-unprepared-items',
    standalone: true,
    imports: [
        CommonModule,
        MatButtonModule,
        MatIconModule,
        MatProgressSpinnerModule,
        MatMenuModule,
        MatCheckboxModule,
        MatTooltipModule,
        MatSnackBarModule,
        AgGridAngular
    ],
    templateUrl: './unprepared-items.component.html',
    styleUrls: ['./unprepared-items.component.css']
})
export class UnpreparedItemsComponent implements OnInit {
    @ViewChild(AgGridAngular) agGrid!: AgGridAngular;

    private gridApi!: GridApi;
    private readonly GRID_ID = 'unprepared-items-grid';

    unpreparedItems: UnpreparedItemDto[] = [];
    isLoading = false;

    columnDefs: ColDef[] = [
        {
            headerName: 'Order',
            field: 'orderCode',
            sortable: true,
            filter: true,
            width: 120
        },
        {
            headerName: 'Buyer',
            field: 'buyerUsername',
            sortable: true,
            filter: true,
            width: 150
        },
        {
            headerName: 'Image',
            field: 'imageUrl',
            width: 80,
            sortable: false,
            filter: false,
            cellRenderer: ImageCellRendererComponent
        },
        {
            headerName: 'Card Name',
            field: 'name',
            sortable: true,
            filter: true,
            width: 250
        },
        {
            headerName: 'Expansion',
            field: 'expansionName',
            sortable: true,
            filter: true,
            width: 200
        },
        {
            headerName: 'Exp. Code',
            field: 'expansionCode',
            sortable: true,
            filter: true,
            width: 100
        },
        {
            headerName: 'Coll. #',
            field: 'collectorNumber', // Assuming this field will be populated in DTO
            sortable: true,
            filter: true,
            width: 80
        },
        {
            headerName: 'Condition',
            field: 'condition',
            sortable: true,
            filter: true,
            width: 80,
            cellRenderer: ConditionCellRendererComponent
        },
        {
            headerName: 'Language',
            field: 'language',
            sortable: true,
            filter: true,
            width: 80,
            cellRenderer: LanguageCellRendererComponent
        },
        {
            headerName: 'Foil',
            field: 'isFoil',
            sortable: true,
            filter: true,
            width: 60,
            cellRenderer: FoilCellRendererComponent
        },
        {
            headerName: 'Signed',
            field: 'isSigned',
            sortable: true,
            filter: true,
            width: 80,
            cellRenderer: (params: any) => params.value ? 'Yes' : 'No'
        },
        {
            headerName: 'Altered',
            field: 'isAltered',
            sortable: true,
            filter: true,
            width: 80,
            cellRenderer: (params: any) => params.value ? 'Yes' : 'No'
        },
        {
            headerName: 'Tag',
            field: 'tag',
            sortable: true,
            filter: true,
            width: 100
        },
        {
            headerName: 'Qty',
            field: 'quantity',
            sortable: true,
            filter: 'agNumberColumnFilter',
            width: 80
        },
        {
            headerName: 'Price',
            field: 'price',
            sortable: true,
            filter: 'agNumberColumnFilter',
            width: 100,
            valueFormatter: (params) => params.value ? `€${params.value.toFixed(2)}` : ''
        },
        {
            headerName: 'Prepared',
            field: 'isPrepared',
            sortable: true,
            filter: false,
            width: 100,
            cellRenderer: (params: any) => {
                return `<input type="checkbox" ${params.value ? 'checked' : ''} data-action-type="toggle-prepared">`;
            },
            onCellClicked: (params: any) => {
                if (params.event.target.getAttribute('data-action-type') === 'toggle-prepared') {
                    this.toggleItemPreparation(params.data, params.event.target);
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
        pagination: true,
        paginationPageSize: 20,
        enableCellTextSelection: true,
        suppressRowClickSelection: true,
        animateRows: true,
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
        this.loadUnpreparedItems();
    }

    onGridReady(params: GridReadyEvent): void {
        this.gridApi = params.api;

        // Restore saved grid state
        const savedState = this.gridStateService.loadGridState(this.GRID_ID);
        if (savedState?.columnState) {
            this.gridApi.applyColumnState({ state: savedState.columnState, applyOrder: true });
        } else {
            this.gridApi.sizeColumnsToFit();
        }
    }

    loadUnpreparedItems(): void {
        this.isLoading = true;
        this.apiService.getUnpreparedItems().subscribe({
            next: (items) => {
                this.unpreparedItems = items;
                this.isLoading = false;
            },
            error: (err) => {
                console.error('Error loading unprepared items', err);
                this.showSnackBar('Error loading items');
                this.isLoading = false;
            }
        });
    }

    toggleItemPreparation(item: UnpreparedItemDto, checkboxElement: any): void {
        const newValue = !item.isPrepared;

        // Optimistic update
        item.isPrepared = newValue;

        this.apiService.toggleItemPreparation(item.id, newValue).subscribe({
            next: () => {
                this.showSnackBar(`Item ${item.name} marked as ${newValue ? 'prepared' : 'unprepared'}`);

                // Open Card Trader URL if marked prepared and has Card Trader Blueprint ID
                if (newValue && item.cardTraderBlueprintId) {
                    const url = `https://www.cardtrader.com/cards/${item.cardTraderBlueprintId}`;
                    window.open(url, '_blank');
                }

                // Remove from list if marked prepared
                if (newValue) {
                    this.unpreparedItems = this.unpreparedItems.filter(i => i.id !== item.id);
                    // Refresh grid data
                    this.gridApi.setGridOption('rowData', this.unpreparedItems);
                }
            },
            error: (err) => {
                console.error('Error updating item preparation', err);
                this.showSnackBar('Error updating item status');
                // Revert
                item.isPrepared = !newValue;
                checkboxElement.checked = !newValue;
            }
        });
    }

    saveGridState(): void {
        if (!this.gridApi) return;

        const columnState = this.gridApi.getColumnState();
        this.gridStateService.saveGridState(this.GRID_ID, {
            columnState,
            sortModel: [] // Sort is included in columnState
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

    getAllColumns(): any[] {
        return this.columnDefs;
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

    // Image Preview Logic
    previewImage: string | null = null;
    previewPosition = { top: 0, left: 0 };

    onCellMouseOver(params: any): void {
        if (params.colDef.field === 'imageUrl' && params.value) {
            this.previewImage = params.value;
            // Position logic can be handled here or just fixed CSS
            // For fixed sidebar, we don't need mouse position
        }
    }

    onCellMouseOut(params: any): void {
        if (params.colDef.field === 'imageUrl') {
            this.previewImage = null;
        }
    }

    private showSnackBar(message: string): void {
        this.snackBar.open(message, 'Close', {
            duration: 3000,
        });
    }
}
