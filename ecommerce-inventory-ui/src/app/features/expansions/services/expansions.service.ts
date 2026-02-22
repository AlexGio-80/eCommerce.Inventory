import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import { ApiResponse } from '../../../core/models/api-response';

export interface Expansion {
    id: number;
    cardTraderId: number;
    name: string;
    code: string;
    gameId: number;
    gameName: string;
    gameCode: string;
    averageCardValue?: number;
    totalMinPrice?: number;
    lastValueAnalysisUpdate?: string;

    // Rarity Stats
    avgValueCommon?: number;
    avgValueUncommon?: number;
    avgValueRare?: number;
    avgValueMythic?: number;

    // Financials
    totalSales?: number;
    totalProfit?: number;
    totalAmountSpent?: number;
    roiPercentage?: number;
    releaseDate?: string;
    iconSvgUri?: string;
}

export interface SyncBlueprintsResponse {
    expansionId: number;
    expansionName: string;
    cardTraderId: number;
    blueprintsFetched: number;
    message: string;
}

@Injectable({
    providedIn: 'root'
})
export class ExpansionsService {
    private readonly apiUrl = `${environment.apiUrl}/api/expansions`;

    constructor(private http: HttpClient) { }

    getExpansions(gameId?: number, search?: string): Observable<Expansion[]> {
        let params: any = {};
        if (gameId) params.gameId = gameId;
        if (search) params.search = search;

        return this.http.get<ApiResponse<Expansion[]>>(this.apiUrl, { params }).pipe(
            map(response => response.data ?? [])
        );
    }

    getExpansion(id: number): Observable<Expansion> {
        return this.http.get<ApiResponse<Expansion>>(`${this.apiUrl}/${id}`).pipe(
            map(response => response.data!)
        );
    }

    syncBlueprints(id: number): Observable<SyncBlueprintsResponse> {
        return this.http.post<ApiResponse<SyncBlueprintsResponse>>(`${this.apiUrl}/${id}/sync-blueprints`, {}).pipe(
            map(response => response.data!)
        );
    }

    analyzeValue(id: number): Observable<any> {
        return this.http.post<ApiResponse<any>>(`${this.apiUrl}/${id}/analyze-value`, {});
    }

    analyzeAllValues(): Observable<any> {
        return this.http.post<ApiResponse<any>>(`${this.apiUrl}/analyze-all-values`, {});
    }
}
