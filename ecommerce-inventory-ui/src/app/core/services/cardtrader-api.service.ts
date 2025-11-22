import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  Game,
  Expansion,
  Blueprint,
  InventoryItem,
  Order,
  PagedResponse,
  ApiResponse,
} from '../models';
import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root',
})
export class CardTraderApiService {
  private apiUrl = `${environment.api.baseUrl}/api/cardtrader`;

  constructor(private http: HttpClient) { }

  // Games
  getGames(): Observable<Game[]> {
    return this.http.get<Game[]>(`${this.apiUrl}/games`);
  }

  getGameById(id: number): Observable<Game> {
    return this.http.get<Game>(`${this.apiUrl}/games/${id}`);
  }

  // Expansions
  getExpansions(gameId?: number): Observable<Expansion[]> {
    let params = new HttpParams();
    if (gameId) {
      params = params.set('gameId', gameId.toString());
    }
    return this.http.get<Expansion[]>(`${this.apiUrl}/expansions`, { params });
  }

  getExpansionById(id: number): Observable<Expansion> {
    return this.http.get<Expansion>(`${this.apiUrl}/expansions/${id}`);
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

  // Inventory Items
  getInventoryItems(
    page: number = 1,
    pageSize: number = 20
  ): Observable<PagedResponse<InventoryItem>> {
    const params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());
    return this.http.get<PagedResponse<InventoryItem>>(
      `${this.apiUrl}/inventory`,
      { params }
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
  // Orders
  getOrders(from?: string, to?: string): Observable<Order[]> {
    let params = new HttpParams();
    if (from) {
      params = params.set('from', from);
    }
    if (to) {
      params = params.set('to', to);
    }
    return this.http.get<Order[]>(`${this.apiUrl}/orders`, { params });
  }

  getOrderById(id: number): Observable<Order> {
    return this.http.get<Order>(`${this.apiUrl}/orders/${id}`);
  }

  syncOrders(from?: string, to?: string): Observable<ApiResponse<any>> {
    const body = {
      from: from || null,
      to: to || null
    };
    return this.http.post<ApiResponse<any>>(`${this.apiUrl}/orders/sync`, body);
  }

  toggleOrderCompletion(id: number, isCompleted: boolean): Observable<Order> {
    return this.http.put<Order>(`${this.apiUrl}/orders/${id}/complete`, isCompleted);
  }

  toggleItemPreparation(itemId: number, isPrepared: boolean): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/orders/items/${itemId}/prepare`, isPrepared);
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
}
