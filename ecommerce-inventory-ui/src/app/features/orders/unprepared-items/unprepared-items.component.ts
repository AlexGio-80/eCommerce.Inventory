import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTableModule } from '@angular/material/table';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CardTraderApiService } from '../../../core/services/cardtrader-api.service';
import { Order, OrderItem } from '../../../core/models/order';

interface UnpreparedItem extends OrderItem {
    orderCode: string;
    buyerUsername: string;
}

@Component({
    selector: 'app-unprepared-items',
    standalone: true,
    imports: [
        CommonModule,
        MatTableModule,
        MatCheckboxModule,
        MatSnackBarModule,
        MatProgressSpinnerModule
    ],
    templateUrl: './unprepared-items.component.html',
    styleUrls: ['./unprepared-items.component.css']
})
export class UnpreparedItemsComponent implements OnInit {
    unpreparedItems: UnpreparedItem[] = [];
    displayedColumns: string[] = ['orderCode', 'buyer', 'name', 'expansion', 'condition', 'language', 'quantity', 'location', 'prepared'];
    isLoading = false;

    constructor(
        private apiService: CardTraderApiService,
        private snackBar: MatSnackBar
    ) { }

    ngOnInit(): void {
        this.loadUnpreparedItems();
    }

    loadUnpreparedItems(): void {
        this.isLoading = true;
        this.apiService.getOrders().subscribe({
            next: (orders) => {
                this.unpreparedItems = [];
                orders.forEach(order => {
                    // Show all unprepared items from all orders (not just paid/sending)
                    order.orderItems.forEach(item => {
                        if (!item.isPrepared) {
                            this.unpreparedItems.push({
                                ...item,
                                orderCode: order.code,
                                buyerUsername: order.buyerUsername
                            });
                        }
                    });
                });
                this.isLoading = false;
            },
            error: (err) => {
                console.error('Error loading orders', err);
                this.showSnackBar('Error loading items');
                this.isLoading = false;
            }
        });
    }

    toggleItemPreparation(item: UnpreparedItem, event: any): void {
        const newValue = !item.isPrepared;
        this.apiService.toggleItemPreparation(item.id, newValue).subscribe({
            next: () => {
                item.isPrepared = newValue;
                this.showSnackBar(`Item ${item.name} marked as ${newValue ? 'prepared' : 'unprepared'}`);
                // Remove from list if marked prepared?
                if (newValue) {
                    this.unpreparedItems = this.unpreparedItems.filter(i => i.id !== item.id);
                }
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
