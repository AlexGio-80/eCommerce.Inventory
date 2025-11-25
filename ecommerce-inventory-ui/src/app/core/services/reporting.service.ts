import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import {
    SalesMetrics,
    SalesChartData,
    TopProduct,
    SalesByGame,
    InventoryValue,
    InventoryDistribution,
    SlowMover,
    ProfitabilityOverview,
    TopPerformer,
    DateRange
} from '../models/reporting.models';
import { ApiResponse } from '../models/api-response';

@Injectable({
    providedIn: 'root'
})
export class ReportingService {
    private apiUrl = 'http://localhost:5152/api/reporting';

    constructor(private http: HttpClient) { }

    // Sales Analytics
    getSalesMetrics(dateRange?: DateRange): Observable<SalesMetrics> {
        let params = new HttpParams();
        if (dateRange) {
            params = params.set('from', dateRange.from.toISOString());
            params = params.set('to', dateRange.to.toISOString());
        }

        return this.http.get<ApiResponse<SalesMetrics>>(`${this.apiUrl}/sales/metrics`, { params })
            .pipe(map(response => response.data!));
    }

    getSalesChart(dateRange?: DateRange, groupBy: 'day' | 'week' | 'month' = 'day'): Observable<SalesChartData> {
        let params = new HttpParams().set('groupBy', groupBy);
        if (dateRange) {
            params = params.set('from', dateRange.from.toISOString());
            params = params.set('to', dateRange.to.toISOString());
        }

        return this.http.get<ApiResponse<SalesChartData>>(`${this.apiUrl}/sales/chart`, { params })
            .pipe(map(response => response.data!));
    }

    getTopProducts(dateRange?: DateRange, limit: number = 10): Observable<TopProduct[]> {
        let params = new HttpParams().set('limit', limit.toString());
        if (dateRange) {
            params = params.set('from', dateRange.from.toISOString());
            params = params.set('to', dateRange.to.toISOString());
        }

        return this.http.get<ApiResponse<TopProduct[]>>(`${this.apiUrl}/sales/top-products`, { params })
            .pipe(map(response => response.data!));
    }

    getSalesByGame(dateRange?: DateRange): Observable<SalesByGame[]> {
        let params = new HttpParams();
        if (dateRange) {
            params = params.set('from', dateRange.from.toISOString());
            params = params.set('to', dateRange.to.toISOString());
        }

        return this.http.get<ApiResponse<SalesByGame[]>>(`${this.apiUrl}/sales/by-game`, { params })
            .pipe(map(response => response.data!));
    }

    // Inventory Analytics
    getInventoryValue(): Observable<InventoryValue> {
        return this.http.get<ApiResponse<InventoryValue>>(`${this.apiUrl}/inventory/value`)
            .pipe(map(response => response.data!));
    }

    getInventoryDistribution(): Observable<InventoryDistribution[]> {
        return this.http.get<ApiResponse<InventoryDistribution[]>>(`${this.apiUrl}/inventory/distribution`)
            .pipe(map(response => response.data!));
    }

    getSlowMovers(days: number = 90): Observable<SlowMover[]> {
        const params = new HttpParams().set('days', days.toString());
        return this.http.get<ApiResponse<SlowMover[]>>(`${this.apiUrl}/inventory/slow-movers`, { params })
            .pipe(map(response => response.data!));
    }

    // Profitability Analytics
    getProfitabilityOverview(dateRange?: DateRange): Observable<ProfitabilityOverview> {
        let params = new HttpParams();
        if (dateRange) {
            params = params.set('from', dateRange.from.toISOString());
            params = params.set('to', dateRange.to.toISOString());
        }

        return this.http.get<ApiResponse<ProfitabilityOverview>>(`${this.apiUrl}/profitability/overview`, { params })
            .pipe(map(response => response.data!));
    }

    getTopPerformers(dateRange?: DateRange, limit: number = 10): Observable<TopPerformer[]> {
        let params = new HttpParams().set('limit', limit.toString());
        if (dateRange) {
            params = params.set('from', dateRange.from.toISOString());
            params = params.set('to', dateRange.to.toISOString());
        }

        return this.http.get<ApiResponse<TopPerformer[]>>(`${this.apiUrl}/profitability/top-performers`, { params })
            .pipe(map(response => response.data!));
    }
}
