import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatChipsModule } from '@angular/material/chips';
import { Observable, forkJoin } from 'rxjs';
import { map } from 'rxjs/operators';

import { CardTraderApiService } from '../../../../core/services';
import { PagedResponse, Order } from '../../../../core/models';

interface DashboardStats {
  totalProducts: number;
  totalOrders: number;
  gameCount: number;
  lastSync: Date;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatIconModule,
    MatToolbarModule,
    MatButtonModule,
    MatListModule,
    MatProgressBarModule,
    MatChipsModule,
  ],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.scss'],
})
export class DashboardComponent implements OnInit {
  stats$!: Observable<DashboardStats>;
  recentOrders$!: Observable<Order[]>;
  isLoading = true;

  constructor(private apiService: CardTraderApiService) { }

  ngOnInit(): void {
    this.loadDashboard();
  }

  private loadDashboard(): void {
    this.stats$ = forkJoin({
      totalItems: this.apiService.getInventoryItems(1, 1).pipe(
        map((response: PagedResponse<any>) => response.totalCount || 0)
      ),
      totalOrders: this.apiService.getOrders().pipe(
        map((orders: Order[]) => orders.length)
      ),
      games: this.apiService.getGames().pipe(
        map((games: any[]) => games.length)
      ),
    }).pipe(
      map((data) => ({
        totalProducts: data.totalItems,
        totalOrders: data.totalOrders,
        gameCount: data.games,
        lastSync: new Date(),
      }))
    );

    this.recentOrders$ = this.apiService.getOrders().pipe(
      map((orders: Order[]) => orders.slice(0, 5))
    );

    this.isLoading = false;
  }

  getOrderStatusColor(state: string): string {
    const statusColorMap: { [key: string]: string } = {
      pending: 'warn',
      paid: 'accent',
      sending: 'primary',
      sent: 'primary',
      delivered: 'success',
      cancelled: 'warn',
    };
    return statusColorMap[state] || 'primary';
  }
}
