import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

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
        return this.http.get<Game[]>(this.apiUrl);
    }

    getGame(id: number): Observable<Game> {
        return this.http.get<Game>(`${this.apiUrl}/${id}`);
    }

    updateGame(id: number, isEnabled: boolean): Observable<Game> {
        return this.http.put<Game>(`${this.apiUrl}/${id}`, { isEnabled });
    }

    syncExpansions(id: number): Observable<SyncResponse> {
        return this.http.post<SyncResponse>(`${this.apiUrl}/${id}/sync-expansions`, {});
    }

    syncAll(id: number): Observable<SyncResponse> {
        return this.http.post<SyncResponse>(`${this.apiUrl}/${id}/sync-all`, {});
    }
}
