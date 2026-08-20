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
    description?: string;
    tag?: string;
    isSynced: boolean;
    isUpdate: boolean;
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
    description?: string;
    tag?: string;
    // Update-mode fields
    cardTraderProductId?: number;
    isUpdate?: boolean;
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

export interface BlueprintListingInfo {
    inventoryItemId?: number;
    pendingListingId?: number;
    cardTraderProductId?: number;
    quantity: number;
    sellingPrice: number;
    purchasePrice: number;
    condition: string;
    language: string;
    isFoil: boolean;
    isSigned: boolean;
    description?: string;
    tag?: string;
    /** synced | pending-edit | ct-native | pending-new */
    status: string;
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

    getListingsByBlueprint(blueprintId: number): Observable<any> {
        return this.http.get<any>(`${this.apiUrl}/by-blueprint/${blueprintId}`);
    }

    createPendingListing(dto: CreatePendingListingDto): Observable<any> {
        return this.http.post<any>(this.apiUrl, dto);
    }

    updatePendingListing(id: number, dto: CreatePendingListingDto): Observable<any> {
        return this.http.put<any>(`${this.apiUrl}/${id}`, dto);
    }

    deletePendingListing(id: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${id}`);
    }

    syncPendingListings(): Observable<any> {
        return this.http.post<any>(`${this.apiUrl}/sync`, {});
    }
}
