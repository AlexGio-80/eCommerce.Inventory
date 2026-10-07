import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import { ApiResponse } from '../../../core/models/api-response';

/** Un'uscita analizzabile: espansione principale più i suoi set figli (es. Commander). */
export interface SealedSetOption {
    code: string;
    name: string;
    releaseDate?: string;
    productCount: number;
    childSets: string[];
}

/** Prezzo di riferimento di un tipo di busta: il €/busta più basso fra i prodotti fatti solo di quella. */
export interface PackReference {
    packKey: string;
    label: string;
    pricePerPack: number;
    productName: string;
}

export interface PackCount {
    packKey: string;
    label: string;
    count: number;
}

export interface SealedProductAnalysis {
    id: number;
    name: string;
    category?: string;
    subtype?: string;
    setCode: string;
    contentsDescription: string;
    packs: PackCount[];
    totalPacks: number;
    hasFixedContent: boolean;
    hasExtras: boolean;
    unresolved: boolean;
    isCase: boolean;
    cardmarketId?: number;
    cardTraderBlueprintId?: number;
    cmTrend?: number;
    cmLow?: number;
    cmPriceDate?: string;
    ctMinPrice?: number;
    ctOfferCount?: number;
    ctPriceUpdatedAt?: string;
    pricePerPack?: number;
    packValue?: number;
    deltaPercent?: number;
}

export interface SealedSetAnalysis {
    code: string;
    name: string;
    releaseDate?: string;
    childSets: string[];
    cardmarketPriceDate?: string;
    catalogImportedAt?: string;
    references: PackReference[];
    products: SealedProductAnalysis[];
}

export interface CardTraderSealedRefreshResult {
    products: number;
    productsWithOffers: number;
    apiCalls: number;
}

export interface SealedCatalogImportResult {
    sets: number;
    products: number;
    newProducts: number;
}

@Injectable({ providedIn: 'root' })
export class PurchasingService {
    private readonly apiUrl = `${environment.apiUrl}/api/purchasing`;

    constructor(private http: HttpClient) { }

    getSets(): Observable<SealedSetOption[]> {
        return this.http.get<ApiResponse<SealedSetOption[]>>(`${this.apiUrl}/sets`)
            .pipe(map(response => response.data ?? []));
    }

    getAnalysis(code: string): Observable<SealedSetAnalysis> {
        return this.http.get<ApiResponse<SealedSetAnalysis>>(`${this.apiUrl}/sets/${code}/analysis`)
            .pipe(map(response => response.data!));
    }

    refreshCardTraderPrices(code: string): Observable<CardTraderSealedRefreshResult> {
        return this.http.post<ApiResponse<CardTraderSealedRefreshResult>>(`${this.apiUrl}/sets/${code}/cardtrader-prices`, {})
            .pipe(map(response => response.data!));
    }

    importCatalog(): Observable<SealedCatalogImportResult> {
        return this.http.post<ApiResponse<SealedCatalogImportResult>>(`${this.apiUrl}/catalog/import`, {})
            .pipe(map(response => response.data!));
    }
}
