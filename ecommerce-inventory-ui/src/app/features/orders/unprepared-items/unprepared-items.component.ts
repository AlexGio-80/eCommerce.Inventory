import { Component, OnInit, OnDestroy, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatMenuModule } from '@angular/material/menu';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatNativeDateModule } from '@angular/material/core';
import { FormsModule } from '@angular/forms';
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
        MatDatepickerModule,
        MatInputModule,
        MatFormFieldModule,
        MatNativeDateModule,
        FormsModule,
        AgGridAngular
    ],
    templateUrl: './unprepared-items.component.html',
    styleUrls: ['./unprepared-items.component.css']
})
export class UnpreparedItemsComponent implements OnInit, OnDestroy {
    @ViewChild(AgGridAngular) agGrid!: AgGridAngular;

    private gridApi!: GridApi;
    private readonly GRID_ID = 'unprepared-items-grid';

    unpreparedItems: UnpreparedItemDto[] = [];
    isLoading = false;
    isSyncing = false;
    private syncInterval: any;

    // Sync date filters
    fromDate: Date;
    toDate: Date;

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
            headerName: 'Icon',
            field: 'iconSvgUri',
            width: 70,
            cellRenderer: (params: any) => {
                if (!params.value) {
                    return `<span style="color: #ccc; font-size: 10px;">No Icon</span>`;
                }
                return `<img src="${params.value}" title="${params.value}" style="width: 32px; height: 32px; vertical-align: middle;" />`;
            }
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
            width: 100,
            valueFormatter: (params) => params.value ? params.value.toUpperCase() : ''
        },
        {
            headerName: 'Coll. #',
            field: 'collectorNumber',
            sortable: true,
            filter: true,
            width: 80,
            valueFormatter: (params) => params.value ? params.value.toString().padStart(3, '0') : ''
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
                return `<button class="prepare-btn" data-action-type="toggle-prepared">Prepare</button>`;
            },
            onCellClicked: (params: any) => {
                if (params.event.target.getAttribute('data-action-type') === 'toggle-prepared') {
                    this.toggleItemPreparation(params.data, params.event.target);
                }
            }
        },
        {
            headerName: 'Rel. Date',
            field: 'expansionReleaseDate',
            sortable: true,
            filter: 'agDateColumnFilter',
            width: 120,
            hide: false, // Visible as requested
            valueFormatter: (params) => params.value ? new Date(params.value).toLocaleDateString() : ''
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
        domLayout: 'autoHeight' as const,
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
    ) {
        // Initialize default dates: from = today - 1 day, to = today + 1 day
        const today = new Date();
        this.fromDate = new Date(today);
        this.fromDate.setDate(today.getDate() - 1);

        this.toDate = new Date(today);
        this.toDate.setDate(today.getDate() + 1);
    }

    ngOnInit(): void {
        this.loadUnpreparedItems();
        // Start auto-sync every 5 minutes (300000 ms)
        this.syncInterval = setInterval(() => {
            this.autoSyncOrders();
        }, 300000);
    }

    ngOnDestroy(): void {
        if (this.syncInterval) {
            clearInterval(this.syncInterval);
        }
    }

    onGridReady(params: GridReadyEvent): void {
        this.gridApi = params.api;

        // Restore saved grid state
        const savedState = this.gridStateService.loadGridState(this.GRID_ID);
        if (savedState?.columnState) {
            this.gridApi.applyColumnState({ state: savedState.columnState, applyOrder: true });
        } else {
            // Default sort: Release Date (asc) and Collector Number (asc) as requested
            this.gridApi.applyColumnState({
                state: [
                    { colId: 'expansionReleaseDate', sort: 'asc' },
                    { colId: 'collectorNumber', sort: 'asc' }
                ],
                defaultState: { sort: null }
            });
            this.gridApi.sizeColumnsToFit();
        }
    }

    loadUnpreparedItems(): void {
        this.isLoading = true;
        this.apiService.getUnpreparedItems().subscribe({
            next: (items) => {
                this.unpreparedItems = items;
                this.isLoading = false;

                // Force grid to re-render after data is loaded
                setTimeout(() => {
                    if (this.gridApi) {
                        this.gridApi.setGridOption('rowData', this.unpreparedItems);
                    }
                }, 0);
            },
            error: (err) => {
                console.error('Error loading unprepared items', err);
                this.showSnackBar('Error loading items');
                this.isLoading = false;
            }
        });
    }

    syncOrders(): void {
        if (!this.fromDate || !this.toDate) {
            this.showSnackBar('Please select both From and To dates');
            return;
        }

        this.isSyncing = true;
        // Convert dates to ISO strings for API
        const fromDateStr = this.fromDate.toISOString().split('T')[0];
        const toDateStr = this.toDate.toISOString().split('T')[0];

        this.apiService.syncOrders(fromDateStr, toDateStr).subscribe({
            next: (result) => {
                const count = (result as any).syncedCount || (result as any).data?.syncedCount || 0;
                this.showSnackBar(`Synced ${count} orders successfully`);
                this.isSyncing = false;
                // Reload unprepared items after sync
                this.loadUnpreparedItems();
            },
            error: (err) => {
                console.error('Error syncing orders', err);
                this.showSnackBar('Error syncing orders');
                this.isSyncing = false;
            }
        });
    }

    autoSyncOrders(): void {
        // Fixed dates: Today - 1 day to Today + 1 day
        const today = new Date();

        const fromDate = new Date(today);
        fromDate.setDate(today.getDate() - 1);

        const toDate = new Date(today);
        toDate.setDate(today.getDate() + 1);

        const fromDateStr = fromDate.toISOString().split('T')[0];
        const toDateStr = toDate.toISOString().split('T')[0];

        // We don't set isSyncing to true here to avoid blocking the UI or showing the spinner constantly
        // or maybe we do want to show it? The user said "lancia la sincronizzazione".
        // Let's keep it subtle.

        this.apiService.syncOrders(fromDateStr, toDateStr).subscribe({
            next: (result) => {
                const count = (result as any).syncedCount || (result as any).data?.syncedCount || 0;
                if (count > 0) {
                    this.showSnackBar(`Auto-sync: ${count} orders synced`);
                    this.loadUnpreparedItems();
                }
            },
            error: (err) => {
                console.error('Error in auto-sync orders', err);
                // Silently fail or log to console, don't disturb user with snackbar for background task unless critical
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
        const imageUrl = params.data.imageUrl;
        if (imageUrl) {
            this.previewImage = imageUrl;
        }
    }

    onCellMouseOut(params: any): void {
        this.previewImage = null;
    }

    private showSnackBar(message: string): void {
        this.snackBar.open(message, 'Close', {
            duration: 3000,
        });
    }
}
