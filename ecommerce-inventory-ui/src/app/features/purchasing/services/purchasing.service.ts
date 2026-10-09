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
    /** Nome del prodotto su Cardmarket, per cercarlo sul sito (quello MTGJSON a volte è diverso). */
    cardmarketName?: string;
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
    decision?: 'Apri' | 'Tieni sigillato' | 'Dati incompleti' | 'Prezzo CM dubbio' | 'Non giocabili';
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
    /** Costo fisso in euro per ogni copia venduta sopra la soglia del bulk. */
    costPerCard: number;
    /** Quota venduta per fascia di prezzo sopra la soglia del bulk. */
    sellThroughBands: SellThroughBand[];
}

export interface SellThroughBand {
    from: number;
    to?: number;
    sold: number;
    inStock: number;
    sharePercent: number;
    measured: boolean;
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
    costPerCard?: number | null;
}

/** Un drop Secret Lair comprato intero, con spesa, incassato e resa. */
export interface SecretLairDropRow {
    sealedProductId: number;
    name: string;
    type: 'Normale' | 'Foil' | 'Bundle' | 'Commander';
    cardmarketId?: number;
    cardmarketName?: string;
    cardTraderBlueprintId?: number;
    /** Pagina del drop nel negozio Wizards, se il monitoraggio l'ha visto. */
    wizardsUrl?: string;
    distinctCards: number;
    cardsListed: number;
    copies: number;
    /** True se copie e prezzo vengono dal registro acquisti, false se stimati. */
    registeredPurchase: boolean;
    unitPrice: number;
    cost: number;
    soldCopies: number;
    grossRevenue: number;
    netRevenue: number;
    stockCopies: number;
    stockListingValue: number;
    profitSoFar: number;
    profitWithStock: number;
    returnPercent?: number;
    recoveredPercent?: number;
    cardmarketValuePerCopy: number;
    firstSeen: string;
}

export interface SecretLairTypeSummary {
    type: string;
    drops: number;
    copies: number;
    cost: number;
    netRevenue: number;
    profitWithStock: number;
    returnPercent?: number;
}

export interface SecretLairRetrospective {
    drops: SecretLairDropRow[];
    summary: SecretLairTypeSummary[];
    cardTraderFeePercent: number;
    looseSoldCopies: number;
    looseGrossRevenue: number;
    matchedRevenuePercent?: number;
    standardPrices: Record<string, number>;
}

export interface SecretLairShopCard {
    quantity: number;
    cardName: string;
    displayName?: string;
}

/** Prodotto visto nel negozio Secret Lair di Wizards. */
export interface SecretLairShopProduct {
    wizardsProductId: string;
    title: string;
    dropName?: string;
    isFoil: boolean;
    price: number;
    stock?: number;
    isPreorder: boolean;
    limitPerCustomer?: number;
    releaseDate?: string;
    saleStart?: string;
    saleEnd?: string;
    firstSeenAt: string;
    lastSeenAt: string;
    soldOutAt?: string;
    removedAt?: string;
    status: 'Disponibile' | 'Preordine' | 'In arrivo' | 'Esaurito' | 'Tolto dal negozio';
    contentsKnown: boolean;
    cards: SecretLairShopCard[];
    url: string;
}

export interface SecretLairShopRun {
    startedAt: string;
    completedAt?: string;
    outcome: 'Running' | 'Succeeded' | 'Failed';
    products: number;
    newProducts: number;
    contentsFetched: number;
    message?: string;
}

export interface SecretLairShopView {
    products: SecretLairShopProduct[];
    runs: SecretLairShopRun[];
    monitorEnabled: boolean;
}

/** Stima di una riga del prodotto: carta (dalla stampa base), carta senza stampe, drop di un bundle o riga non stimata. */
export interface SecretLairCardEstimate {
    quantity: number;
    line: string;
    foil: boolean;
    kind: 'Carta' | 'Senza stampe' | 'Prodotto' | 'Non stimata';
    basePrice?: number;
    baseSet?: string;
    estimatedPrice?: number;
    realPrice?: number;
    value?: number;
}

/** Valutazione di un prodotto del negozio Secret Lair (Fase 3). */
export interface SecretLairDropEstimate {
    wizardsProductId: string;
    price: number;
    verdict: 'Compra' | 'Al limite' | 'Lascia' | 'Non stimabile' | 'Carte da leggere';
    estimatedTrend: number;
    estimatedNetValue?: number;
    realTrend?: number;
    realNetValue?: number;
    marginPercent?: number;
    weakSharePercent: number;
    unknownLines: number;
    frozenNetValue?: number;
    frozenAt?: string;
    frozenErrorPercent?: number;
    cards: SecretLairCardEstimate[];
}

export interface SecretLairCurvePoint {
    basePrice: number;
    secretLairPrice: number;
    samples: number;
}

export interface SecretLairValuation {
    model: {
        normalCurve: SecretLairCurvePoint[];
        foilCurve: SecretLairCurvePoint[];
        noBaseNormalPrice: number;
        noBaseFoilPrice: number;
        normalSamples: number;
        foilSamples: number;
        noBaseSamples: number;
        backtest: { drops: number; medianAbsoluteErrorPercent?: number; medianBiasPercent?: number; within25Percent?: number };
        buyMarginPercent: number;
        settings: OpeningValueSettings;
    };
    products: SecretLairDropEstimate[];
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

export interface PlanOffer {
    productId: number;
    productName: string;
    setCode: string;
    sellerId: number;
    sellerName: string;
    sellerCountry?: string;
    ctZero: boolean;
    price: number;
    available: number;
    openValueNet: number;
    roiPercent: number;
}

export interface PlanProduct {
    productId: number;
    name: string;
    setCode?: string;
    openValueNet?: number;
    cmTrend?: number;
    offerCount: number;
    cheapestPrice?: number;
    cheapestSeller?: string;
    cardTraderBlueprintId?: number;
    /** Nome del prodotto su Cardmarket, per cercarlo sul sito e confrontarne il prezzo. */
    cardmarketName?: string;
    note?: string;
}

export interface PlanSeller {
    sellerId: number;
    sellerName: string;
    country?: string;
    ctZero: boolean;
    productCount: number;
    total: number;
    openValueNet: number;
    margin: number;
    items: PlanOffer[];
}

/** Piano d'acquisto su Card Trader: venditori con più prodotti convenienti e carrello CT Zero. */
export interface PurchasePlan {
    computedAt: string;
    products: PlanProduct[];
    sellers: PlanSeller[];
    ctZero: { productCount: number; total: number; openValueNet: number; margin: number; items: PlanOffer[] };
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
    /** Costo per carta scritto a mano; null = calcolato. */
    costPerCard?: number | null;
}

export interface ProductPurchase extends ProductPurchaseInput {
    /** Prezzo unitario diviso le carte contenute in un'unità. */
    calculatedCostPerCard?: number;
    cardsPerUnit?: number;
    cardsEstimated: boolean;
    /** Da usare nelle inserzioni: scritto a mano, altrimenti calcolato. */
    effectiveCostPerCard?: number;
    cardmarketName?: string;
    id: number;
    productName: string;
    setCode: string;
    totalPrice: number;
    predictedOpenValueNet?: number;
    predictionCoverage?: number;
    predictedAt?: string;
    predictedTotalNet?: number;
}

/** Prodotto del catalogo sigillati trovato dalla ricerca del registro acquisti. */
export interface CatalogProduct {
    id: number;
    /** Nome MTGJSON. */
    name: string;
    cardmarketName?: string;
    setCode: string;
    setName?: string;
    category?: string;
    subtype?: string;
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

    getSecretLairDrops(): Observable<SecretLairRetrospective> {
        return this.http.get<ApiResponse<SecretLairRetrospective>>(`${this.apiUrl}/secret-lair/drops`)
            .pipe(map(response => response.data!));
    }

    getSecretLairShop(): Observable<SecretLairShopView> {
        return this.http.get<ApiResponse<SecretLairShopView>>(`${this.apiUrl}/secret-lair/shop`)
            .pipe(map(response => response.data!));
    }

    getSecretLairValuation(): Observable<SecretLairValuation> {
        return this.http.get<ApiResponse<SecretLairValuation>>(`${this.apiUrl}/secret-lair/valuation`)
            .pipe(map(response => response.data!));
    }

    refreshSecretLairShop(): Observable<{ products: number; newProducts: number; contentsFetched: number; message: string }> {
        return this.http.post<ApiResponse<{ products: number; newProducts: number; contentsFetched: number; message: string }>>(
            `${this.apiUrl}/secret-lair/shop/refresh`, {})
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

    buildPurchasePlan(sealedProductIds: number[]): Observable<PurchasePlan> {
        return this.http.post<ApiResponse<PurchasePlan>>(`${this.apiUrl}/purchase-plan`, sealedProductIds).pipe(map(r => r.data!));
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

    searchCatalog(query: string): Observable<CatalogProduct[]> {
        return this.http.get<ApiResponse<CatalogProduct[]>>(`${this.apiUrl}/catalog/search`, { params: { q: query } })
            .pipe(map(response => response.data!));
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
