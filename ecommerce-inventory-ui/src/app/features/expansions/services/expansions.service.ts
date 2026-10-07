import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import { ApiResponse } from '../../../core/models/api-response';

/** Esito di un import del listino prezzi pubblico di Cardmarket (storico per l'analisi acquisti). */
export interface CardmarketImportLog {
    id: number;
    trigger: string;
    outcome: 'Running' | 'Succeeded' | 'Skipped' | 'Failed';
    startedAt: string;
    completedAt?: string;
    sourceCreatedAt?: string;
    sealedProducts: number;
    sealedSnapshotsWritten: number;
    singlesTracked: number;
    singlesSnapshotsWritten: number;
    newProducts: number;
    message?: string;
}

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

    // Box calculator config
    packsPerBox?: number;
    cardsPerPack?: number;
    boxPrice?: number;
    boxRoiPercentage?: number;
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

    saveBoxConfig(id: number, packsPerBox: number | null, cardsPerPack: number | null, boxPrice: number | null): Observable<any> {
        return this.http.patch<ApiResponse<any>>(`${this.apiUrl}/${id}/box-config`, { packsPerBox, cardsPerPack, boxPrice });
    }

    syncSealedPrices(): Observable<any> {
        return this.http.post<ApiResponse<any>>(`${this.apiUrl}/sync-sealed-prices`, {});
    }

    getLastCardmarketImport(): Observable<CardmarketImportLog | null> {
        return this.http.get<ApiResponse<CardmarketImportLog[]>>(`${environment.apiUrl}/api/cardmarket/import/logs?take=1`)
            .pipe(map(response => response.data?.[0] ?? null));
    }

    runCardmarketImport(): Observable<CardmarketImportLog> {
        return this.http.post<ApiResponse<CardmarketImportLog>>(`${environment.apiUrl}/api/cardmarket/import`, {})
            .pipe(map(response => response.data!));
    }
}
