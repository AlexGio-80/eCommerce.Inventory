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
    /** Valore atteso dell'apertura al netto dei costi, su prezzi Cardmarket. */
    openValueCm?: number;
    coverageCm?: number;
    openValueCt?: number;
    coverageCt?: number;
    /** Ricavato netto rivendendo il sigillato al trend CM. */
    sealedNetCm?: number;
    openingRoiPercent?: number;
    decision?: 'Apri' | 'Tieni sigillato' | 'Dati incompleti' | 'Prezzo CM dubbio';
    priceMismatch: boolean;
    componentsTrend?: number;
    missingPacks: string[];
    missingDecks: string[];
}

export interface BulkSellThroughExpansion {
    name: string;
    releaseDate: string;
    sold: number;
    inStock: number;
    sharePercent: number;
}

export interface OpeningValueSettings {
    bulkThreshold: number;
    bulkPrice: number;
    bulkSellThroughPercent: number;
    sellingCostPercent: number;
    measuredBulkSellThroughPercent: number;
    bulkSellThroughMeasured: boolean;
    bulkSellThroughExpansions: BulkSellThroughExpansion[];
    measuredCardTraderFeePercent?: number;
}

export interface SheetValue {
    name: string;
    slotsPerPack: number;
    valuePerSlot: number;
    coveragePercent: number;
}

export interface TopCard {
    name: string;
    setCode?: string;
    number?: string;
    foil: boolean;
    value: number;
    probabilityPercent: number;
    expectedValue: number;
}

/** Valore atteso di un tipo di busta, con il dettaglio di da dove viene. */
export interface PackValue {
    packKey: string;
    label: string;
    valueCm: number;
    netCm: number;
    coverageCm: number;
    valueCt?: number;
    netCt?: number;
    coverageCt: number;
    sheets: SheetValue[];
    topCards: TopCard[];
}

export interface OpeningValueParams {
    bulkThreshold?: number | null;
    bulkPrice?: number | null;
    bulkSellThroughPercent?: number | null;
    sellingCostPercent?: number | null;
}

export interface SetDetailImportResult {
    sets: number;
    cards: number;
    boosterTypes: number;
    decks: number;
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
    detailImportedAt?: string;
    hasBoosterData: boolean;
    settings: OpeningValueSettings;
    packValues: PackValue[];
}

export interface CardTraderSealedRefreshResult {
    products: number;
    productsWithOffers: number;
    apiCalls: number;
    cardPrices: number;
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

    getAnalysis(code: string, params: OpeningValueParams = {}): Observable<SealedSetAnalysis> {
        const query: Record<string, string> = {};
        for (const [key, value] of Object.entries(params)) {
            if (value != null) query[key] = String(value);
        }
        return this.http.get<ApiResponse<SealedSetAnalysis>>(`${this.apiUrl}/sets/${code}/analysis`, { params: query })
            .pipe(map(response => response.data!));
    }

    importDetails(code: string): Observable<SetDetailImportResult> {
        return this.http.post<ApiResponse<SetDetailImportResult>>(`${this.apiUrl}/sets/${code}/details/import`, {})
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
