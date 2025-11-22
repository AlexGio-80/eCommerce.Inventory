import { Component, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatMenuModule } from '@angular/material/menu';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, GridApi, GridReadyEvent, ColumnState, SortModelItem } from 'ag-grid-community';
import { CardTraderApiService } from '../../../core/services/cardtrader-api.service';
import { GridStateService } from '../../../core/services/grid-state.service';
import { Order, OrderItem } from '../../../core/models/order';

@Component({
    selector: 'app-orders-list',
    standalone: true,
    imports: [
        CommonModule,
        FormsModule,
        MatButtonModule,
        MatIconModule,
        MatCheckboxModule,
        MatSnackBarModule,
        MatProgressSpinnerModule,
        MatInputModule,
        MatFormFieldModule,
        MatMenuModule,
        AgGridAngular
    ],
    templateUrl: './orders-list.component.html',
    styleUrls: ['./orders-list.component.css']
})
export class OrdersListComponent implements OnInit {
    @ViewChild(AgGridAngular) agGrid!: AgGridAngular;

    private gridApi!: GridApi;
    private readonly GRID_ID = 'orders-grid';

    orders: Order[] = [];
    filteredOrders: Order[] = [];
    isLoading = false;
    isSyncing = false;

    // Date filters
    fromDate: string;
    toDate: string;

    // AG-Grid Column Definitions
    columnDefs: ColDef[] = [
        {
            headerName: 'ID',
            field: 'cardTraderOrderId',
            sortable: true,
            filter: true,
            width: 100
        },
        {
            headerName: 'Code',
            field: 'code',
            sortable: true,
            filter: true,
            width: 150
        },
        {
            headerName: 'Buyer',
            field: 'buyerUsername',
            sortable: true,
            filter: true,
            width: 150
        },
        {
            headerName: 'Total',
            field: 'sellerTotal',
            sortable: true,
            filter: 'agNumberColumnFilter',
            width: 120,
            valueFormatter: (params) => params.value ? `€${params.value.toFixed(2)}` : ''
        },
        {
            headerName: 'State',
            field: 'state',
            sortable: true,
            filter: true,
            width: 120
        },
        {
            headerName: 'Paid At',
            field: 'paidAt',
            sortable: true,
            filter: 'agDateColumnFilter',
            width: 150,
            valueFormatter: (params) => params.value ? new Date(params.value).toLocaleDateString() : ''
        },
        {
            headerName: 'Completed',
            field: 'isCompleted',
            sortable: true,
            filter: true,
            width: 120,
            cellRenderer: (params: any) => {
                const checkbox = document.createElement('input');
                checkbox.type = 'checkbox';
                checkbox.checked = params.value;
                checkbox.addEventListener('change', () => {
                    this.toggleOrderCompletion(params.data, checkbox.checked);
                });
                return checkbox;
            }
        },
        {
            headerName: 'Actions',
            field: 'actions',
            sortable: false,
            filter: false,
            width: 100,
            cellRenderer: (params: any) => {
                return `<button class="action-btn">View</button>`;
            },
            onCellClicked: (params) => {
                this.viewOrderDetails(params.data);
            }
        }
    ];

    // AG-Grid Options
    defaultColDef: ColDef = {
        resizable: true,
        sortable: true,
        filter: true,
        enableRowGroup: false,
        enablePivot: false,
        enableValue: false
    };

    gridOptions = {
        pagination: false,
        domLayout: 'autoHeight' as const,
        enableCellTextSelection: true,
        suppressRowClickSelection: true,
        rowSelection: 'single' as const,
        animateRows: true,
        enableRangeSelection: false,
        suppressMenuHide: false,
        columnMenu: 'new' as const,
        suppressDragLeaveHidesColumns: true
    };

    constructor(
        private apiService: CardTraderApiService,
        private gridStateService: GridStateService,
        private snackBar: MatSnackBar
    ) {
        // Set default dates
        const today = new Date();
        const tomorrow = new Date(today);
        tomorrow.setDate(tomorrow.getDate() + 1);

        this.fromDate = this.formatDate(today);
        this.toDate = this.formatDate(tomorrow);
    }

    ngOnInit(): void {
        this.loadOrders();
    }

    onGridReady(params: GridReadyEvent): void {
        this.gridApi = params.api;

        // Restore saved grid state
        const savedState = this.gridStateService.loadGridState(this.GRID_ID);
        if (savedState) {
            if (savedState.columnState) {
                this.gridApi.applyColumnState({ state: savedState.columnState, applyOrder: true });
            }
            if (savedState.sortModel) {
                this.gridApi.applyColumnState({ state: savedState.sortModel });
            }
        }

        // Auto-size columns to fit
        this.gridApi.sizeColumnsToFit();
    }

    loadOrders(): void {
        this.isLoading = true;
        this.apiService.getOrders().subscribe({
            next: (orders) => {
                this.orders = orders;
                this.applyDateFilter();
                this.isLoading = false;
            },
            error: (err) => {
                console.error('Error loading orders', err);
                this.showSnackBar('Error loading orders');
                this.isLoading = false;
            }
        });
    }

    applyDateFilter(): void {
        if (!this.fromDate && !this.toDate) {
            this.filteredOrders = this.orders;
            return;
        }

        const from = this.fromDate ? new Date(this.fromDate) : null;
        const to = this.toDate ? new Date(this.toDate) : null;

        this.filteredOrders = this.orders.filter(order => {
            if (!order.paidAt) return false;

            const orderDate = new Date(order.paidAt);

            if (from && orderDate < from) return false;
            if (to) {
                const toEndOfDay = new Date(to);
                toEndOfDay.setHours(23, 59, 59, 999);
                if (orderDate > toEndOfDay) return false;
            }

            return true;
        });
    }

    onDateFilterChange(): void {
        this.applyDateFilter();
    }

    syncOrders(): void {
        this.isSyncing = true;
        this.apiService.syncOrders(this.fromDate, this.toDate).subscribe({
            next: (response) => {
                this.showSnackBar(response.message || 'Orders synced successfully');
                this.isSyncing = false;
                this.loadOrders();
            },
            error: (err) => {
                console.error('Error syncing orders', err);
                this.showSnackBar('Error syncing orders');
                this.isSyncing = false;
            }
        });
    }

    saveGridState(): void {
        if (!this.gridApi) return;

        const columnState = this.gridApi.getColumnState();
        const sortModel = this.gridApi.getColumnState().filter(col => col.sort != null);

        this.gridStateService.saveGridState(this.GRID_ID, {
            columnState,
            sortModel
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

    toggleOrderCompletion(order: Order, newValue: boolean): void {
        this.apiService.toggleOrderCompletion(order.id, newValue).subscribe({
            next: (updatedOrder) => {
                order.isCompleted = updatedOrder.isCompleted;
                this.showSnackBar(`Order ${order.code} marked as ${newValue ? 'completed' : 'incomplete'}`);
                this.gridApi.refreshCells({ force: true });
            },
            error: (err) => {
                console.error('Error updating order completion', err);
                this.showSnackBar('Error updating order status');
                // Refresh to revert checkbox
                this.gridApi.refreshCells({ force: true });
            }
        });
    }

    toggleItemPreparation(item: OrderItem, event: any): void {
        const newValue = !item.isPrepared;
        this.apiService.toggleItemPreparation(item.id, newValue).subscribe({
            next: () => {
                item.isPrepared = newValue;
                this.showSnackBar(`Item ${item.name} marked as ${newValue ? 'prepared' : 'unprepared'}`);
            },
            error: (err) => {
                console.error('Error updating item preparation', err);
                this.showSnackBar('Error updating item status');
                // Revert checkbox state
                event.source.checked = !newValue;
            }
        });
    }

    viewOrderDetails(order: Order): void {
        // TODO: Implement order details view
        console.log('View order details:', order);
    }

    private formatDate(date: Date): string {
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        return `${year}-${month}-${day}`;
    }

    private showSnackBar(message: string): void {
        this.snackBar.open(message, 'Close', {
            duration: 3000,
        });
    }

    // Event handlers for grid state changes
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
