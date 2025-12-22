import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { RouterModule, Router } from '@angular/router';
import { Observable, forkJoin, of, BehaviorSubject } from 'rxjs';
import { map, debounceTime, distinctUntilChanged, switchMap } from 'rxjs/operators';

import { CardTraderApiService, ReportingService } from '../../../../core/services';
import { PagedResponse, SalesByExpansion, ExpansionProfitability, TopExpansionValue } from '../../../../core/models';
import { TabManagerService } from '../../../../core/services/tab-manager.service';

interface DashboardStats {
  totalProducts: number;
  totalOrders: number;
  unpreparedItemsCount: number;
  lastSync: Date;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatIconModule,
    MatToolbarModule,
    MatButtonModule,
    MatListModule,
    MatProgressBarModule,
    MatChipsModule,
    MatFormFieldModule,
    MatInputModule,
    RouterModule
  ],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.scss'],
})
export class DashboardComponent implements OnInit {
  stats$!: Observable<DashboardStats>;
  salesByExpansion$!: Observable<SalesByExpansion[]>;
  expansionProfitability$!: Observable<ExpansionProfitability[]>;
  topExpansionsByValue$!: Observable<TopExpansionValue[]>;
  isLoading = true;

  // Filter subjects
  private salesFilterSubject = new BehaviorSubject<string>('');
  private roiFilterSubject = new BehaviorSubject<string>('');

  // Filter properties for binding
  salesFilter = '';
  roiFilter = '';

  constructor(
    private apiService: CardTraderApiService,
    private reportingService: ReportingService,
    private tabManager: TabManagerService,
    private router: Router
  ) { }

  ngOnInit(): void {
    this.loadDashboard();
  }

  loadDashboard(): void {
    this.stats$ = forkJoin({
      totalItems: this.apiService.getInventoryItems(1, 1).pipe(
        map((response: PagedResponse<any>) => response.totalCount || 0)
      ),
      totalOrders: this.apiService.getOrders().pipe(
        map((orders: any[]) => orders.length)
      ),
      unpreparedItems: this.apiService.getUnpreparedItems().pipe(
        map((items: any[]) => items.length)
      ),
      lastSync: of(new Date()) // TODO: Get real last sync date
    }).pipe(
      map((data) => ({
        totalProducts: data.totalItems,
        totalOrders: data.totalOrders,
        unpreparedItemsCount: data.unpreparedItems,
        lastSync: data.lastSync
      }))
    );

    this.salesByExpansion$ = this.salesFilterSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      switchMap(filter => this.apiService.getSalesByExpansion(undefined, undefined, 15, filter))
    );

    this.expansionProfitability$ = this.roiFilterSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      switchMap(filter => this.apiService.getExpansionProfitability(undefined, undefined, 12, filter))
    );

    this.topExpansionsByValue$ = this.reportingService.getTopExpansionsByValue(10);

    this.isLoading = false;
  }

  openTab(route: string, title: string, icon: string): void {
    this.tabManager.openTab(route, title, icon);
    this.router.navigate([route]);
  }

  onSalesFilterChange(filter: string): void {
    this.salesFilterSubject.next(filter);
  }

  onRoiFilterChange(filter: string): void {
    this.roiFilterSubject.next(filter);
  }
}
