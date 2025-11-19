import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Blueprint, PagedResponse } from '../models';
import { environment } from '../../../environments/environment';

/**
 * Service for managing Card Trader blueprints (cards)
 * Provides methods for querying, searching, and filtering blueprints
 */
@Injectable({
  providedIn: 'root',
})
export class BlueprintsService {
  private apiUrl = `${environment.api.baseUrl}/api/cardtrader/blueprints`;

  constructor(private http: HttpClient) {}

  /**
   * Get all blueprints with pagination
   * @param page Page number (default 1)
   * @param pageSize Items per page (default 20, max 100)
   * @returns Paged response containing blueprints
   */
  getAllBlueprints(page: number = 1, pageSize: number = 20): Observable<PagedResponse<Blueprint>> {
    const params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());
    return this.http.get<PagedResponse<Blueprint>>(this.apiUrl, { params });
  }

  /**
   * Get a specific blueprint by ID
   * @param id Blueprint ID
   * @returns Blueprint details
   */
  getBlueprintById(id: number): Observable<Blueprint> {
    return this.http.get<Blueprint>(`${this.apiUrl}/${id}`);
  }

  /**
   * Get blueprints for a specific game
   * @param gameId Game ID
   * @returns Array of blueprints for the game
   */
  getBlueprintsByGame(gameId: number): Observable<Blueprint[]> {
    return this.http.get<Blueprint[]>(`${this.apiUrl}/by-game/${gameId}`);
  }

  /**
   * Get blueprints for a specific expansion
   * @param expansionId Expansion ID
   * @returns Array of blueprints in the expansion
   */
  getBlueprintsByExpansion(expansionId: number): Observable<Blueprint[]> {
    return this.http.get<Blueprint[]>(`${this.apiUrl}/by-expansion/${expansionId}`);
  }

  /**
   * Get blueprint by its Card Trader ID
   * Useful for checking if a blueprint already exists
   * @param cardTraderId Card Trader ID
   * @returns Blueprint if found
   */
  getBlueprintByCardTraderId(cardTraderId: number): Observable<Blueprint> {
    return this.http.get<Blueprint>(`${this.apiUrl}/by-cardtrader-id/${cardTraderId}`);
  }

  /**
   * Search blueprints by name (partial match, case-insensitive)
   * @param name Name to search for
   * @returns Array of matching blueprints (max 50)
   */
  searchBlueprints(name: string): Observable<Blueprint[]> {
    const params = new HttpParams().set('name', name);
    return this.http.get<Blueprint[]>(`${this.apiUrl}/search`, { params });
  }

  /**
   * Get total count of blueprints in the database
   * @returns Total blueprint count
   */
  getBlueprintCount(): Observable<number> {
    return this.http.get<number>(`${this.apiUrl}/stats/count`);
  }
}
