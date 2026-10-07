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
    /** Fattore "prezzo realizzato" applicato (incassato / trend CM), in percentuale. */
    priceRealizationPercent: number;
    measuredPriceRealizationPercent: number;
    priceRealizationMeasured: boolean;
    priceRealizationSampleCopies: number;
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
    priceRealizationPercent?: number | null;
}

/** Bilancio reale di un'apertura, ricostruito dal tag delle inserzioni. */
export interface OpeningBalance {
    tag: string;
    expansion: string;
    expansionReleaseDate?: string;
    openedAtRelease: boolean;
    firstUpload: string;
    ageDays: number;
    copies: number;
    cost: number;
    soldCopies: number;
    grossRevenue: number;
    netRevenue: number;
    stockCopies: number;
    stockListingValue: number;
    stockBulkCopies: number;
    profitSoFar: number;
    profitSoFarPercent?: number;
    revenueCurve: { days: number; netRevenue: number }[];
    registeredCost?: number;
    predictedNet?: number;
}

/** Prodotto nella classifica delle opportunità (Fase 3). */
export interface Opportunity {
    sealedProductId: number;
    name: string;
    category?: string;
    subtype?: string;
    mainSetCode: string;
    setName: string;
    releaseDate?: string;
    cmTrend?: number;
    cmLow?: number;
    openValueCm?: number;
    coverageCm?: number;
    sealedNetCm?: number;
    openingRoiPercent?: number;
    decision?: string;
    trendChange7?: number;
    trendChange30?: number;
    valueChange7?: number;
    valueChange30?: number;
    cardTraderBlueprintId?: number;
}

export interface OpportunityList {
    date?: string;
    computedAt?: string;
    items: Opportunity[];
}

export type AlertRuleType = 'PriceBelow' | 'PriceDrop' | 'OpeningOpportunity' | 'PriceAtLow';

export interface AlertRuleInput {
    name: string;
    type: AlertRuleType;
    sealedProductId?: number | null;
    setCode?: string | null;
    category?: string | null;
    /** Sottotipo MTGJSON (es. collector, play). */
    subtype?: string | null;
    /** Solo uscite di non più di tanti giorni fa, comprese quelle in arrivo. */
    recentReleaseDays?: number | null;
    threshold: number;
    useLowPrice: boolean;
    isActive: boolean;
    sendEmail: boolean;
}

export interface AlertRule extends AlertRuleInput {
    id: number;
    productName?: string;
    lastEvaluatedAt?: string;
    matchingCount: number;
}

export interface AlertNotification {
    id: number;
    alertRuleId?: number;
    sealedProductId?: number;
    setCode?: string;
    title: string;
    message: string;
    createdAt: string;
    readAt?: string;
    emailRequested: boolean;
    emailSentAt?: string;
    emailError?: string;
}

export interface AlertNotificationList {
    unread: number;
    emailConfigured: boolean;
    items: AlertNotification[];
}

export interface AlertEvaluationResult {
    rulesEvaluated: number;
    newNotifications: number;
    email: string;
}

export interface ProductPurchaseInput {
    sealedProductId: number;
    quantity: number;
    unitPrice: number;
    store?: string | null;
    seller?: string | null;
    purchasedAt?: string | null;
    openedAt?: string | null;
    tag?: string | null;
    notes?: string | null;
}

export interface ProductPurchase extends ProductPurchaseInput {
    id: number;
    productName: string;
    setCode: string;
    totalPrice: number;
    predictedOpenValueNet?: number;
    predictionCoverage?: number;
    predictedAt?: string;
    predictedTotalNet?: number;
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

    getOpenings(): Observable<OpeningBalance[]> {
        return this.http.get<ApiResponse<OpeningBalance[]>>(`${this.apiUrl}/openings`)
            .pipe(map(response => response.data ?? []));
    }

    getOpportunities(): Observable<OpportunityList> {
        return this.http.get<ApiResponse<OpportunityList>>(`${this.apiUrl}/opportunities`)
            .pipe(map(response => response.data ?? { items: [] }));
    }

    computeOpportunities(): Observable<unknown> {
        return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/opportunities/compute`, {});
    }

    getAlertRules(): Observable<AlertRule[]> {
        return this.http.get<ApiResponse<AlertRule[]>>(`${this.apiUrl}/alerts`).pipe(map(r => r.data ?? []));
    }

    saveAlertRule(input: AlertRuleInput, id?: number): Observable<AlertRule> {
        const request = id
            ? this.http.put<ApiResponse<AlertRule>>(`${this.apiUrl}/alerts/${id}`, input)
            : this.http.post<ApiResponse<AlertRule>>(`${this.apiUrl}/alerts`, input);
        return request.pipe(map(r => r.data!));
    }

    deleteAlertRule(id: number): Observable<unknown> {
        return this.http.delete<ApiResponse<unknown>>(`${this.apiUrl}/alerts/${id}`);
    }

    evaluateAlerts(): Observable<AlertEvaluationResult> {
        return this.http.post<ApiResponse<AlertEvaluationResult>>(`${this.apiUrl}/alerts/evaluate`, {}).pipe(map(r => r.data!));
    }

    sendTestEmail(): Observable<unknown> {
        return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/alerts/test-email`, {});
    }

    getNotifications(take = 50): Observable<AlertNotificationList> {
        return this.http.get<ApiResponse<AlertNotificationList>>(`${this.apiUrl}/notifications`, { params: { take } })
            .pipe(map(r => r.data ?? { unread: 0, emailConfigured: false, items: [] }));
    }

    /** Segna come letto un avviso; senza id, tutti. */
    markNotificationsRead(id?: number): Observable<unknown> {
        const params: Record<string, string> = id ? { id: String(id) } : {};
        return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/notifications/read`, {}, { params });
    }

    getPurchases(): Observable<ProductPurchase[]> {
        return this.http.get<ApiResponse<ProductPurchase[]>>(`${this.apiUrl}/purchases`)
            .pipe(map(response => response.data ?? []));
    }

    savePurchase(input: ProductPurchaseInput, id?: number): Observable<ProductPurchase> {
        const request = id
            ? this.http.put<ApiResponse<ProductPurchase>>(`${this.apiUrl}/purchases/${id}`, input)
            : this.http.post<ApiResponse<ProductPurchase>>(`${this.apiUrl}/purchases`, input);
        return request.pipe(map(response => response.data!));
    }

    deletePurchase(id: number): Observable<unknown> {
        return this.http.delete<ApiResponse<unknown>>(`${this.apiUrl}/purchases/${id}`);
    }

    importCatalog(): Observable<SealedCatalogImportResult> {
        return this.http.post<ApiResponse<SealedCatalogImportResult>>(`${this.apiUrl}/catalog/import`, {})
            .pipe(map(response => response.data!));
    }
}
