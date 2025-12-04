import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export interface PendingListing {
    id: number;
    blueprintId: number;
    blueprint?: any;
    quantity: number;
    sellingPrice: number;
    purchasePrice: number;
    condition: string;
    language: string;
    isFoil: boolean;
    isSigned: boolean;
    location: string;
    tag?: string;
    isSynced: boolean;
    createdAt: Date;
    syncedAt?: Date;
    syncError?: string;
    cardTraderProductId?: number;
    inventoryItemId?: number;
    // Grading data
    gradingScore?: number;
    gradingConditionCode?: string;
    gradingCentering?: number;
    gradingCorners?: number;
    gradingEdges?: number;
    gradingSurface?: number;
    gradingConfidence?: number;
    gradingImagesCount?: number;
}

export interface CreatePendingListingDto {
    blueprintId: number;
    quantity: number;
    price: number;
    purchasePrice: number;
    condition: string;
    language: string;
    isFoil: boolean;
    isSigned: boolean;
    location?: string;
    tag?: string;
    // Grading data
    gradingScore?: number;
    gradingConditionCode?: string;
    gradingCentering?: number;
    gradingCorners?: number;
    gradingEdges?: number;
    gradingSurface?: number;
    gradingConfidence?: number;
    gradingImagesCount?: number;
}

@Injectable({
    providedIn: 'root'
})
export class PendingListingsService {
    private apiUrl = `${environment.apiUrl}/api/pending-listings`;

    constructor(private http: HttpClient) { }

    getPendingListings(page: number = 1, pageSize: number = 20, isSynced?: boolean, hasError?: boolean): Observable<any> {
        let params: any = { page, pageSize };
        if (isSynced !== undefined) params.isSynced = isSynced;
        if (hasError) params.hasError = hasError;
        return this.http.get<any>(this.apiUrl, { params });
    }

    createPendingListing(dto: CreatePendingListingDto): Observable<PendingListing> {
        return this.http.post<PendingListing>(this.apiUrl, dto);
    }

    updatePendingListing(id: number, dto: CreatePendingListingDto): Observable<PendingListing> {
        return this.http.put<PendingListing>(`${this.apiUrl}/${id}`, dto);
    }

    deletePendingListing(id: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${id}`);
    }

    syncPendingListings(): Observable<any> {
        return this.http.post<any>(`${this.apiUrl}/sync`, {});
    }
}
