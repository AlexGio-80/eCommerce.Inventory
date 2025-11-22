import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { animate, state, style, transition, trigger } from '@angular/animations';
import { CardTraderApiService } from '../../../core/services/cardtrader-api.service';
import { Order, OrderItem } from '../../../core/models/order';

@Component({
    selector: 'app-orders-list',
    standalone: true,
    imports: [
        CommonModule,
        FormsModule,
        MatTableModule,
        MatButtonModule,
        MatIconModule,
        MatCheckboxModule,
        MatExpansionModule,
        MatSnackBarModule,
        MatProgressSpinnerModule,
        MatInputModule,
        MatFormFieldModule
    ],
    templateUrl: './orders-list.component.html',
    styleUrls: ['./orders-list.component.css'],
    animations: [
        trigger('detailExpand', [
            state('collapsed', style({ height: '0px', minHeight: '0' })),
            state('expanded', style({ height: '*' })),
            transition('expanded <=> collapsed', animate('225ms cubic-bezier(0.4, 0.0, 0.2, 1)')),
        ]),
    ],
})
export class OrdersListComponent implements OnInit {
    orders: Order[] = [];
    filteredOrders: Order[] = [];
    columnsToDisplay = ['id', 'code', 'buyer', 'total', 'state', 'paidAt', 'isCompleted', 'actions'];
    expandedElement: Order | null = null;
    isLoading = false;
    isSyncing = false;

    // Date filters - default to today and tomorrow
    fromDate: string;
    toDate: string;

    constructor(
        private apiService: CardTraderApiService,
        private snackBar: MatSnackBar
    ) {
        // Set default dates: from today to tomorrow
        const today = new Date();
        const tomorrow = new Date(today);
        tomorrow.setDate(tomorrow.getDate() + 1);

        this.fromDate = this.formatDate(today);
        this.toDate = this.formatDate(tomorrow);
    }

    ngOnInit(): void {
        this.loadOrders();
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
                // Include the entire "to" day
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

    private formatDate(date: Date): string {
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        return `${year}-${month}-${day}`;
    }

    toggleOrderCompletion(order: Order, event: any): void {
        event.stopPropagation();
        const newValue = !order.isCompleted;
        this.apiService.toggleOrderCompletion(order.id, newValue).subscribe({
            next: (updatedOrder) => {
                order.isCompleted = updatedOrder.isCompleted;
                this.showSnackBar(`Order ${order.code} marked as ${newValue ? 'completed' : 'incomplete'}`);
            },
            error: (err) => {
                console.error('Error updating order completion', err);
                this.showSnackBar('Error updating order status');
                // Revert checkbox state
                event.source.checked = !newValue;
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

    private showSnackBar(message: string): void {
        this.snackBar.open(message, 'Close', {
            duration: 3000,
        });
    }
}
