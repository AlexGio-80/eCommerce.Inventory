import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export interface Expansion {
    id: number;
    cardTraderId: number;
    name: string;
    code: string;
    gameId: number;
    gameName: string;
    gameCode: string;
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

        return this.http.get<Expansion[]>(this.apiUrl, { params });
    }

    getExpansion(id: number): Observable<Expansion> {
        return this.http.get<Expansion>(`${this.apiUrl}/${id}`);
    }

    syncBlueprints(id: number): Observable<SyncBlueprintsResponse> {
        return this.http.post<SyncBlueprintsResponse>(`${this.apiUrl}/${id}/sync-blueprints`, {});
    }
}
