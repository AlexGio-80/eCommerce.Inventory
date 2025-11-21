import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSortModule } from '@angular/material/sort';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Observable, BehaviorSubject } from 'rxjs';
import { map, tap, switchMap, startWith } from 'rxjs/operators';

import { CardTraderApiService } from '../../../../core/services';
import { InventoryItem, Game, PagedResponse } from '../../../../core/models';

@Component({
  selector: 'app-inventory-list',
  standalone: true,
  imports: [
    CommonModule,
    MatTableModule,
    MatPaginatorModule,
    MatSortModule,
    MatFormFieldModule,
    MatSelectModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatToolbarModule,
    MatCardModule,
    MatChipsModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './inventory-list.component.html',
  styleUrls: ['./inventory-list.component.scss'],
})
export class InventoryListComponent implements OnInit {
  displayedColumns: string[] = [
    'id',
    'cardName',
    'quantity',
    'price',
    'status',
    'actions',
  ];

  items$!: Observable<InventoryItem[]>;
  games$!: Observable<Game[]>;
  totalItems = 0;
  isLoading = true;

  pageSize = 20;
  pageSizeOptions = [10, 20, 50, 100];
  currentPage = 0;

  private pageSubject = new BehaviorSubject<number>(1);

  constructor(private apiService: CardTraderApiService) { }

  ngOnInit(): void {
    this.games$ = this.apiService.getGames();
    this.loadInventory();
  }

  private loadInventory(): void {
    this.items$ = this.pageSubject.pipe(
      tap(() => {
        this.isLoading = true;
      }),
      switchMap((page) =>
        this.apiService.getInventoryItems(page, this.pageSize)
      ),
      tap((response: PagedResponse<InventoryItem>) => {
        this.totalItems = response.totalCount || 0;
        this.isLoading = false;
      }),
      map((response: PagedResponse<InventoryItem>) => response.items || []),
      startWith([])
    );
  }

  onPageChange(event: PageEvent): void {
    this.pageSize = event.pageSize;
    this.currentPage = event.pageIndex;
    this.pageSubject.next(event.pageIndex + 1);
  }

  editItem(item: InventoryItem): void {
    console.log('Edit item:', item);
    // TODO: Open dialog to edit item
  }

  deleteItem(item: InventoryItem): void {
    if (confirm(`Sei sicuro di voler eliminare ${item.blueprint?.name}?`)) {
      this.apiService.deleteInventoryItem(item.id).subscribe({
        next: () => {
          console.log('Item deleted');
          this.loadInventory();
        },
        error: (err) => {
          console.error('Error deleting item:', err);
        },
      });
    }
  }

  getStatusColor(status: string): string {
    const colorMap: { [key: string]: string } = {
      active: 'primary',
      inactive: 'warn',
      sold: 'accent',
    };
    return colorMap[status] || 'primary';
  }
}
