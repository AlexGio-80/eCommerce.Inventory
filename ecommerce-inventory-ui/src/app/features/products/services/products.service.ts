import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export interface CreateInventoryItemDto {
    blueprintId: number;
    quantity: number;
    price: number;
    condition: string;
    language: string;
    isFoil: boolean;
    isSigned: boolean;
    location?: string;
    purchasePrice: number;
}

export interface InventoryItem {
    id: number;
    blueprintId: number;
    quantity: number;
    listingPrice: number;
    condition: string;
    language: string;
    isFoil: boolean;
    isSigned: boolean;
    location: string;
    dateAdded: Date;
}

@Injectable({
    providedIn: 'root'
})
export class ProductsService {
    private apiUrl = `${environment.apiUrl}/api/inventory`;

    constructor(private http: HttpClient) { }

    createProduct(dto: CreateInventoryItemDto): Observable<InventoryItem> {
        return this.http.post<InventoryItem>(this.apiUrl, dto);
    }

    getProducts(page: number = 1, pageSize: number = 20, search?: string): Observable<any> {
        let params: any = { page, pageSize };
        if (search) params.search = search;
        return this.http.get<any>(this.apiUrl, { params });
    }
}
