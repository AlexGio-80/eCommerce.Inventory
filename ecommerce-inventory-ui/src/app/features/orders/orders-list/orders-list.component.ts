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
import { MatDividerModule } from '@angular/material/divider';
import { MatSelectModule } from '@angular/material/select';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, GridApi, GridReadyEvent, ColumnState, SortModelItem } from 'ag-grid-community';
import { CardTraderApiService } from '../../../core/services/cardtrader-api.service';
import { GridStateService } from '../../../core/services/grid-state.service';
import { ExportService } from '../../../core/services/export.service';
import { Order, OrderItem } from '../../../core/models/order';
import { ConfirmDialogComponent } from '../../../shared/components/confirm-dialog.component';

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
        MatDividerModule,
        MatSelectModule,
        MatDialogModule,
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
    excludeNullDates: boolean = true;

    // Quick filter
    quickFilterText: string = '';

    // Filter presets
    filterPreset: string = 'all';
    filterPresets = [
        { value: 'all', label: 'All Orders' },
        { value: 'incomplete', label: 'Incomplete Orders' },
        { value: 'unprepared', label: 'Has Unprepared Items' },
        { value: 'today', label: "Today's Orders" }
    ];

    // Row selection
    selectedRows: Order[] = [];

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
        rowSelection: 'multiple' as const,  // Enable multiple row selection
        animateRows: true,
        enableRangeSelection: false,
        suppressMenuHide: false,
        columnMenu: 'new' as const,
        suppressDragLeaveHidesColumns: true
    };

    constructor(
        private apiService: CardTraderApiService,
        private gridStateService: GridStateService,
        private exportService: ExportService,
        private snackBar: MatSnackBar,
        private dialog: MatDialog
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
            // Restore filter model
            if (savedState.filterModel) {
                this.gridApi.setFilterModel(savedState.filterModel);
            }
            // Restore quick filter text
            if (savedState.quickFilterText) {
                this.quickFilterText = savedState.quickFilterText;
                this.gridApi.setGridOption('quickFilterText', this.quickFilterText);
            }
        }

        // Auto-size columns to fit
        this.gridApi.sizeColumnsToFit();
    }

    loadOrders(): void {
        this.isLoading = true;
        // Pass date filters to backend for SQL-level filtering
        this.apiService.getOrders(this.fromDate, this.toDate, this.excludeNullDates).subscribe({
            next: (orders) => {
                this.orders = orders;
                this.filteredOrders = orders; // No need for client-side filtering
                this.isLoading = false;
            },
            error: (err) => {
                console.error('Error loading orders', err);
                this.showSnackBar('Error loading orders');
                this.isLoading = false;
            }
        });
    }

    onDateFilterChange(): void {
        // Reload orders from backend with new date filters
        this.loadOrders();
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
        const filterModel = this.gridApi.getFilterModel();

        this.gridStateService.saveGridState(this.GRID_ID, {
            columnState,
            sortModel,
            filterModel,
            quickFilterText: this.quickFilterText
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
        // this.saveGridState(); // Auto-save disabled per user request
    }

    onColumnVisible(): void {
        // this.saveGridState(); // Auto-save disabled per user request
    }

    onSortChanged(): void {
        // this.saveGridState(); // Auto-save disabled per user request
    }

    // Column Visibility Helper
    toggleColumnVisibility(colId: string): void {
        const col = this.gridApi.getColumn(colId);
        if (col) {
            this.gridApi.setColumnsVisible([colId], !col.isVisible());
            this.saveGridState();
        }
    }

    isColumnVisible(colId: string): boolean {
        const col = this.gridApi?.getColumn(colId);
        return col ? col.isVisible() : true;
    }

    getAllColumns(): any[] {
        return this.columnDefs.filter(col => col.field !== 'actions');
    }

    // ========================================================================
    // Export Functionality
    // ========================================================================

    exportToCsv(): void {
        try {
            this.exportService.exportToCsv(this.gridApi, 'orders', false);
            this.showSnackBar('Orders exported to CSV successfully');
        } catch (error) {
            console.error('Export error:', error);
            this.showSnackBar('Error exporting to CSV');
        }
    }

    exportToExcel(): void {
        try {
            const allData = this.exportService.getAllRowData(this.gridApi);
            this.exportService.exportToExcel(allData, this.columnDefs, 'orders', 'Orders');
            this.showSnackBar('Orders exported to Excel successfully');
        } catch (error) {
            console.error('Export error:', error);
            this.showSnackBar('Error exporting to Excel');
        }
    }

    exportSelectedRows(): void {
        try {
            if (this.selectedRows.length === 0) {
                this.showSnackBar('No rows selected for export');
                return;
            }
            this.exportService.exportToExcel(
                this.selectedRows,
                this.columnDefs,
                'orders_selected',
                'Selected Orders'
            );
            this.showSnackBar(`${this.selectedRows.length} orders exported to Excel`);
        } catch (error) {
            console.error('Export error:', error);
            this.showSnackBar('Error exporting selected rows');
        }
    }

    // ========================================================================
    // Row Selection
    // ========================================================================

    onSelectionChanged(): void {
        this.selectedRows = this.gridApi.getSelectedRows();
    }

    deselectAll(): void {
        this.gridApi.deselectAll();
        this.selectedRows = [];
    }

    // ========================================================================
    // Quick Filter
    // ========================================================================

    onQuickFilterChanged(event: any): void {
        this.quickFilterText = event.target.value;
        this.gridApi.setGridOption('quickFilterText', this.quickFilterText);
    }

    clearQuickFilter(): void {
        this.quickFilterText = '';
        this.gridApi.setGridOption('quickFilterText', '');
    }

    // ========================================================================
    // Filter Presets
    // ========================================================================

    onFilterPresetChange(preset: string): void {
        this.filterPreset = preset;
        this.applyFilterPreset();
    }

    private applyFilterPreset(): void {
        if (!this.gridApi) return;

        // Clear existing filters first
        this.gridApi.setFilterModel(null);

        switch (this.filterPreset) {
            case 'all':
                // No filters
                break;

            case 'incomplete':
                // Filter for incomplete orders
                this.gridApi.setFilterModel({
                    isCompleted: {
                        filterType: 'text',
                        type: 'equals',
                        filter: 'false'
                    }
                });
                break;

            case 'unprepared':
                // This would require a custom filter or backend support
                // For now, just show a message
                this.showSnackBar('Unprepared items filter requires backend support');
                break;

            case 'today':
                // Filter for today's orders
                const today = new Date();
                today.setHours(0, 0, 0, 0);
                const tomorrow = new Date(today);
                tomorrow.setDate(tomorrow.getDate() + 1);

                this.gridApi.setFilterModel({
                    paidAt: {
                        filterType: 'date',
                        type: 'inRange',
                        dateFrom: today.toISOString().split('T')[0],
                        dateTo: tomorrow.toISOString().split('T')[0]
                    }
                });
                break;
        }
    }

    clearAllFilters(): void {
        if (!this.gridApi) return;

        // Clear AG-Grid filters
        this.gridApi.setFilterModel(null);

        // Clear quick filter
        this.quickFilterText = '';
        this.gridApi.setGridOption('quickFilterText', '');

        // Reset filter preset
        this.filterPreset = 'all';

        // Clear date filters (reset to defaults)
        const today = new Date();
        const tomorrow = new Date(today);
        tomorrow.setDate(tomorrow.getDate() + 1);
        this.fromDate = this.formatDate(today);
        this.toDate = this.formatDate(tomorrow);
        this.excludeNullDates = true;

        // Reload orders
        this.loadOrders();

        this.showSnackBar('All filters cleared');
    }

    // ========================================================================
    // Bulk Operations
    // ========================================================================

    bulkMarkComplete(): void {
        if (this.selectedRows.length === 0) {
            this.showSnackBar('No orders selected');
            return;
        }

        const dialogRef = this.dialog.open(ConfirmDialogComponent, {
            data: {
                title: 'Mark Orders as Complete',
                message: `Are you sure you want to mark ${this.selectedRows.length} order(s) as complete?`,
                confirmText: 'Mark Complete',
                cancelText: 'Cancel'
            }
        });

        dialogRef.afterClosed().subscribe(confirmed => {
            if (confirmed) {
                this.performBulkUpdate(true);
            }
        });
    }

    bulkMarkIncomplete(): void {
        if (this.selectedRows.length === 0) {
            this.showSnackBar('No orders selected');
            return;
        }

        const dialogRef = this.dialog.open(ConfirmDialogComponent, {
            data: {
                title: 'Mark Orders as Incomplete',
                message: `Are you sure you want to mark ${this.selectedRows.length} order(s) as incomplete?`,
                confirmText: 'Mark Incomplete',
                cancelText: 'Cancel'
            }
        });

        dialogRef.afterClosed().subscribe(confirmed => {
            if (confirmed) {
                this.performBulkUpdate(false);
            }
        });
    }

    private performBulkUpdate(isComplete: boolean): void {
        const orderIds = this.selectedRows.map(order => order.id);
        let completed = 0;
        let failed = 0;

        // Update orders one by one (could be optimized with a bulk API endpoint)
        orderIds.forEach((orderId, index) => {
            this.apiService.toggleOrderCompletion(orderId, isComplete).subscribe({
                next: (updatedOrder) => {
                    completed++;
                    // Update the order in the local array
                    const order = this.orders.find(o => o.id === orderId);
                    if (order) {
                        order.isCompleted = updatedOrder.isCompleted;
                    }

                    // If this is the last update, refresh grid and show summary
                    if (completed + failed === orderIds.length) {
                        this.gridApi.refreshCells({ force: true });
                        this.deselectAll();
                        this.showSnackBar(
                            `Bulk update complete: ${completed} succeeded, ${failed} failed`
                        );
                    }
                },
                error: (err) => {
                    failed++;
                    console.error(`Error updating order ${orderId}:`, err);

                    // If this is the last update, show summary
                    if (completed + failed === orderIds.length) {
                        this.gridApi.refreshCells({ force: true });
                        this.deselectAll();
                        this.showSnackBar(
                            `Bulk update complete: ${completed} succeeded, ${failed} failed`
                        );
                    }
                }
            });
        });
    }
}
