import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import { ApiResponse } from '../../../core/models/api-response';

export interface Game {
    id: number;
    cardTraderId: number;
    name: string;
    code: string;
    isEnabled: boolean;
}

export interface SyncResponse {
    message: string;
    details?: any;
    expansionsSynced?: boolean;
    blueprintsStats?: {
        added: number;
        updated: number;
        failed: number;
    };
}

@Injectable({
    providedIn: 'root'
})
export class GamesService {
    private readonly apiUrl = `${environment.apiUrl}/api/games`;

    constructor(private http: HttpClient) { }

    getGames(): Observable<Game[]> {
        return this.http.get<ApiResponse<Game[]>>(this.apiUrl).pipe(
            map(response => response.data ?? [])
        );
    }

    getGame(id: number): Observable<Game> {
        return this.http.get<ApiResponse<Game>>(`${this.apiUrl}/${id}`).pipe(
            map(response => response.data!)
        );
    }

    updateGame(id: number, isEnabled: boolean): Observable<Game> {
        return this.http.put<ApiResponse<Game>>(`${this.apiUrl}/${id}`, { isEnabled }).pipe(
            map(response => response.data!)
        );
    }

    syncExpansions(id: number): Observable<SyncResponse> {
        return this.http.post<ApiResponse<SyncResponse>>(`${this.apiUrl}/${id}/sync-expansions`, {}).pipe(
            map(response => response.data!)
        );
    }

    syncAll(id: number): Observable<SyncResponse> {
        return this.http.post<ApiResponse<SyncResponse>>(`${this.apiUrl}/${id}/sync-all`, {}).pipe(
            map(response => response.data!)
        );
    }
}
