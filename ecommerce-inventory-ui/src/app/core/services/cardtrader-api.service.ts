import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import {
  Game,
  Expansion,
  Blueprint,
  InventoryItem,
  Order,
  OrderItem,
  PagedResponse,
  ApiResponse,
  UnpreparedItemDto,
  MarketplaceStats,
} from '../models';
import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root',
})
export class CardTraderApiService {
  private apiUrl = `${environment.api.baseUrl}/api/cardtrader`;

  constructor(private http: HttpClient) { }

  // Games (use dedicated GamesController)
  getGames(): Observable<Game[]> {
    return this.http.get<Game[]>(`${environment.api.baseUrl}/api/games`);
  }

  getGameById(id: number): Observable<Game> {
    return this.http.get<Game>(`${environment.api.baseUrl}/api/games/${id}`);
  }

  // Expansions (use dedicated ExpansionsController)
  getExpansions(gameId?: number): Observable<Expansion[]> {
    let params = new HttpParams();
    if (gameId) {
      params = params.set('gameId', gameId.toString());
    }
    return this.http.get<Expansion[]>(`${environment.api.baseUrl}/api/expansions`, { params });
  }

  getExpansionById(id: number): Observable<Expansion> {
    return this.http.get<Expansion>(`${environment.api.baseUrl}/api/expansions/${id}`);
  }

  // Blueprints
  getBlueprints(expansionId?: number): Observable<Blueprint[]> {
    let params = new HttpParams();
    if (expansionId) {
      params = params.set('expansionId', expansionId.toString());
    }
    return this.http.get<Blueprint[]>(`${this.apiUrl}/blueprints`, { params });
  }

  getBlueprintById(id: number): Observable<Blueprint> {
    return this.http.get<Blueprint>(`${this.apiUrl}/blueprints/${id}`);
  }

  getBlueprintByCardTraderId(cardTraderId: number): Observable<Blueprint> {
    return this.http.get<Blueprint>(`${this.apiUrl}/blueprints/by-cardtrader-id/${cardTraderId}`);
  }

  getAdjacentBlueprint(expansionId: number, currentCollectorNumber: string, direction: 'next' | 'prev'): Observable<Blueprint> {
    const params = new HttpParams()
      .set('expansionId', expansionId)
      .set('currentCollectorNumber', currentCollectorNumber)
      .set('direction', direction);
    return this.http.get<Blueprint>(`${this.apiUrl}/blueprints/adjacent`, { params });
  }

  // Inventory Items
  getInventoryItems(
    page: number = 1,
    pageSize: number = 20,
    filters?: {
      searchTerm?: string;
      cardName?: string;
      expansionName?: string;
      condition?: string;
      language?: string;
    }
  ): Observable<PagedResponse<InventoryItem>> {
    let params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());

    if (filters) {
      if (filters.searchTerm) params = params.set('searchTerm', filters.searchTerm);
      if (filters.cardName) params = params.set('cardName', filters.cardName);
      if (filters.expansionName) params = params.set('expansionName', filters.expansionName);
      if (filters.condition) params = params.set('condition', filters.condition);
      if (filters.language) params = params.set('language', filters.language);
    }

    return this.http.get<ApiResponse<PagedResponse<InventoryItem>>>(
      `${this.apiUrl}/inventory`,
      { params }
    ).pipe(
      map(response => {
        if (!response.success || !response.data) {
          throw new Error(response.message || 'Failed to fetch inventory items');
        }
        return response.data;
      })
    );
  }

  getInventoryItem(id: number): Observable<InventoryItem> {
    return this.http.get<InventoryItem>(`${this.apiUrl}/inventory/${id}`);
  }

  createInventoryItem(item: Partial<InventoryItem>): Observable<InventoryItem> {
    return this.http.post<InventoryItem>(`${this.apiUrl}/inventory`, item);
  }

  updateInventoryItem(
    id: number,
    item: Partial<InventoryItem>
  ): Observable<InventoryItem> {
    return this.http.put<InventoryItem>(
      `${this.apiUrl}/inventory/${id}`,
      item
    );
  }

  deleteInventoryItem(id: number): Observable<ApiResponse<void>> {
    return this.http.delete<ApiResponse<void>>(`${this.apiUrl}/inventory/${id}`);
  }

  // Orders
  getOrders(from?: string, to?: string, excludeNullDates: boolean = true): Observable<Order[]> {
    let params = new HttpParams();
    if (from) {
      params = params.set('from', from);
    }
    if (to) {
      params = params.set('to', to);
    }
    params = params.set('excludeNullDates', excludeNullDates.toString());

    return this.http.get<ApiResponse<Order[]>>(`${this.apiUrl}/orders`, { params }).pipe(
      map(response => {
        if (!response.success || !response.data) {
          throw new Error(response.message || 'Failed to fetch orders');
        }
        return response.data;
      })
    );
  }

  getOrderById(id: number): Observable<Order> {
    return this.http.get<Order>(`${this.apiUrl}/orders/${id}`);
  }

  getUnpreparedItems(): Observable<UnpreparedItemDto[]> {
    return this.http.get<ApiResponse<UnpreparedItemDto[]>>(`${this.apiUrl}/orders/unprepared-items`).pipe(
      map(response => {
        if (!response.success || !response.data) {
          throw new Error(response.message || 'Failed to fetch unprepared items');
        }
        return response.data;
      })
    );
  }

  syncOrders(from?: string, to?: string): Observable<ApiResponse<any>> {
    const body = {
      from: from || null,
      to: to || null
    };
    return this.http.post<ApiResponse<any>>(`${this.apiUrl}/orders/sync`, body);
  }

  toggleOrderCompletion(id: number, isCompleted: boolean): Observable<Order> {
    return this.http.put<ApiResponse<Order>>(`${this.apiUrl}/orders/${id}/complete`, isCompleted).pipe(
      map(response => {
        if (!response.success || !response.data) {
          throw new Error(response.message || 'Failed to toggle order completion');
        }
        return response.data;
      })
    );
  }

  toggleItemPreparation(itemId: number, isPrepared: boolean): Observable<any> {
    return this.http.put<ApiResponse<any>>(`${this.apiUrl}/orders/items/${itemId}/prepare`, isPrepared).pipe(
      map(response => {
        if (!response.success || !response.data) {
          throw new Error(response.message || 'Failed to toggle item preparation');
        }
        return response.data;
      })
    );
  }

  // Sync
  syncCardTraderData(request: {
    syncGames?: boolean;
    syncCategories?: boolean;
    syncExpansions?: boolean;
    syncBlueprints?: boolean;
    syncProperties?: boolean;
    syncInventory?: boolean;
  }): Observable<ApiResponse<any>> {
    return this.http.post<ApiResponse<any>>(`${this.apiUrl}/sync`, request);
  }

  getMarketplaceStats(blueprintId: number, filters?: {
    condition?: string;
    language?: string;
    isFoil?: boolean;
    isSigned?: boolean;
  }): Observable<MarketplaceStats> {
    let params = new HttpParams();
    if (filters) {
      if (filters.condition) params = params.set('condition', filters.condition);
      if (filters.language) params = params.set('language', filters.language);
      if (filters.isFoil !== undefined) params = params.set('isFoil', filters.isFoil.toString());
      if (filters.isSigned !== undefined) params = params.set('isSigned', filters.isSigned.toString());
    }
    return this.http.get<MarketplaceStats>(`${this.apiUrl}/inventory/marketplace-stats/${blueprintId}`, { params });
  }

  // Reporting
  getSalesByExpansion(from?: string, to?: string, limit: number = 10, filter?: string): Observable<any[]> {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    params = params.set('limit', limit.toString());
    if (filter) params = params.set('filter', filter);

    return this.http.get<ApiResponse<any[]>>(`${environment.api.baseUrl}/api/reporting/sales/by-expansion`, { params }).pipe(
      map(response => {
        if (!response.success || !response.data) {
          throw new Error(response.message || 'Failed to fetch sales by expansion');
        }
        return response.data;
      })
    );
  }

  getExpansionProfitability(from?: string, to?: string, limit: number = 10, filter?: string): Observable<any[]> {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    params = params.set('limit', limit.toString());
    if (filter) params = params.set('filter', filter);

    return this.http.get<ApiResponse<any[]>>(`${environment.api.baseUrl}/api/reporting/profitability/by-expansion`, { params }).pipe(
      map(response => {
        if (!response.success || !response.data) {
          throw new Error(response.message || 'Failed to fetch expansion profitability');
        }
        return response.data;
      })
    );
  }
}
