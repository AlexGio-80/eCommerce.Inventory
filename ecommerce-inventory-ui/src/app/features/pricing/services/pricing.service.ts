import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import { ApiResponse } from '../../../core/models/api-response';

export type PriceReferenceMode =
    | 'NthLowestOffer'
    | 'LowestOffer'
    | 'MedianOffer'
    | 'AverageOffer'
    | 'AverageOfLowestN'
    | 'PercentileOffer';

export interface PricingRule {
    id?: number;
    fromPrice: number;
    toPrice: number;
    referenceMode: PriceReferenceMode;
    position: number;
    /** Collocazione sulla scaletta in percentuale (0 = la più economica). Usata da PercentileOffer. */
    percentile: number;
    adjustmentAmount: number;
    adjustmentPercent: number;
    canIncrease: boolean;
    canDecrease: boolean;
    priority: number;
    isActive: boolean;
}

export interface PricingProfile {
    id: number;
    name: string;
    isActive: boolean;
    dryRun: boolean;
    minPrice: number;
    maxIncreasePercentPerRun: number;
    maxDecreasePercentPerRun: number;
    maxMedianRatio: number;
    includeProSellers: boolean;
    includeNormalSellers: boolean;
    excludeVacationSellers: boolean;
    minSellerDailyCapacity?: number | null;
    countryCodesCsv?: string | null;
    enableOutlierRejection: boolean;
    outlierMadThreshold: number;
    minOffersForOutlierRejection: number;
    minComparableOffers: number;
    matchCondition: boolean;
    matchLanguage: boolean;
    matchFoil: boolean;
    rules: PricingRule[];
}

export interface PriceChange {
    id?: number;
    blueprintId: number;
    cardName?: string | null;
    /** Id del blueprint su Card Trader: serve ad aprire la pagina della carta sul sito. */
    cardTraderId?: number | null;
    /** Null quando la carta è uscita dal magazzino, di norma perché venduta: la riga di registro resta. */
    inventoryItemId: number | null;
    oldPrice: number;
    proposedPrice: number;
    delta: number;
    referencePrice?: number | null;
    comparableOffersCount: number;
    outliersRejectedCount: number;
    outcome: string;
    reason: string;
    createdAt: string;
}

/** Esiti possibili di una valutazione, allineati all'enum PricingOutcome del backend. */
export const PRICING_OUTCOMES: { value: string; label: string }[] = [
    { value: 'Applied', label: 'Applicate' },
    { value: 'SimulatedDryRun', label: 'Simulate (dry-run)' },
    { value: 'NoChangeNeeded', label: 'Invariate' },
    { value: 'NoMatchingRule', label: 'Nessuna regola' },
    { value: 'InsufficientOffers', label: 'Offerte insufficienti' },
    { value: 'BlockedByGuardrail', label: 'Bloccate dal guardrail' },
    { value: 'BlockedByDirection', label: 'Bloccate dalla direzione' },
    { value: 'Failed', label: 'Fallite' }
];

/** Pagina di dettaglio restituita dall'endpoint delle variazioni di una esecuzione. */
export interface PriceChangePage {
    totalCount: number;
    returnedCount: number;
    items: PriceChange[];
}

export interface PricingRunReport {
    trigger: string;
    dryRun: boolean;
    startedAt: string;
    completedAt?: string;
    plannedCount: number;
    evaluatedCount: number;
    appliedCount: number;
    simulatedCount: number;
    noChangeCount: number;
    skippedCount: number;
    failedCount: number;
    totalPriceDelta: number;
    coveragePercent: number;
    changes: PriceChange[];
}

export interface PricingRunSummary {
    id: number;
    trigger: string;
    dryRun: boolean;
    startedAt: string;
    completedAt?: string;
    plannedCount: number;
    evaluatedCount: number;
    appliedCount: number;
    simulatedCount: number;
    noChangeCount: number;
    skippedCount: number;
    failedCount: number;
    totalPriceDelta: number;
    coveragePercent: number;
    errorMessage?: string;
}

/** Come scegliere le carte su cui calcolare l'anteprima. Campi vuoti = nessun limite. */
export interface PreviewFilters {
    limit: number;
    minPrice?: number | null;
    maxPrice?: number | null;
    expansionId?: number | null;
}

/**
 * Esecuzione in corso. I contatori sono opzionali di proposito: finché `runId` è assente
 * l'esecuzione sta ancora preparando (selezione delle carte, allineamento dei prezzi da
 * Card Trader) e l'unica cosa da mostrare è `phase`.
 */
export interface PricingRunStatus {
    runId?: number | null;
    trigger: string;
    description: string;
    startedAt: string;
    phase: string;
    cancellationRequested: boolean;
    plannedCount?: number | null;
    evaluatedCount?: number | null;
    appliedCount?: number | null;
    simulatedCount?: number | null;
    noChangeCount?: number | null;
    skippedCount?: number | null;
    failedCount?: number | null;
    totalPriceDelta?: number | null;
    coveragePercent?: number | null;
    dryRun?: boolean | null;
}

export interface CoverageBand {
    fascia: string;
    blueprint: number;
    maiValutati: number;
    valutatiUltime24h: number;
    valutatiUltimi7Giorni: number;
}

export interface CoverageReport {
    blueprintTotali: number;
    maiValutati: number;
    fasce: CoverageBand[];
}

@Injectable({ providedIn: 'root' })
export class PricingService {
    private readonly baseUrl = `${environment.apiUrl}/api/pricing`;

    constructor(private http: HttpClient) { }

    getProfiles(): Observable<PricingProfile[]> {
        return this.http.get<ApiResponse<PricingProfile[]>>(`${this.baseUrl}/profiles`)
            .pipe(map(r => r.data ?? []));
    }

    updateProfile(id: number, changes: Partial<PricingProfile>): Observable<PricingProfile> {
        return this.http.put<ApiResponse<PricingProfile>>(`${this.baseUrl}/profiles/${id}`, changes)
            .pipe(map(r => r.data!));
    }

    /**
     * Calcola cosa cambierebbe senza scrivere nulla, né su Card Trader né sullo storico.
     * I filtri servono a provare una regola sulla fascia che riguarda: senza, il campione
     * è sempre quello delle carte di maggior valore.
     */
    preview(profileId: number, filters: PreviewFilters): Observable<PricingRunReport> {
        return this.http.post<ApiResponse<PricingRunReport>>(`${this.baseUrl}/preview`,
            { profileId, ...filters })
            .pipe(map(r => r.data!));
    }

    /**
     * Applica davvero alle carte scelte nell'anteprima. Le rivaluta su dati freschi prima di
     * scrivere, e scrive anche se il profilo è in dry-run. Come `run`, ritorna subito.
     */
    apply(profileId: number, blueprintIds: number[]): Observable<PricingRunStatus> {
        return this.http.post<ApiResponse<PricingRunStatus>>(`${this.baseUrl}/apply`,
            { profileId, blueprintIds })
            .pipe(map(r => r.data!));
    }

    /**
     * Avvia l'esecuzione e ritorna subito: il lavoro prosegue sul server, quindi si può
     * lasciare la pagina. Scrive su Card Trader solo se il profilo non è in dry-run.
     * Risponde `409` se un'altra esecuzione è già in corso.
     */
    run(profileId: number, highValueThreshold: number, bulkSliceSize: number): Observable<PricingRunStatus> {
        return this.http.post<ApiResponse<PricingRunStatus>>(`${this.baseUrl}/run`,
            { profileId, highValueThreshold, bulkSliceSize })
            .pipe(map(r => r.data!));
    }

    /** Esecuzione attualmente in corso, `null` se l'autopricer è fermo. */
    getCurrentRun(): Observable<PricingRunStatus | null> {
        return this.http.get<ApiResponse<PricingRunStatus | null>>(`${this.baseUrl}/run/current`)
            .pipe(map(r => r.data ?? null));
    }

    /** Chiede l'interruzione: si ferma fra una carta e la successiva, non all'istante. */
    cancelRun(): Observable<void> {
        return this.http.post<ApiResponse<unknown>>(`${this.baseUrl}/run/cancel`, {})
            .pipe(map(() => void 0));
    }

    getRuns(limit = 20): Observable<PricingRunSummary[]> {
        return this.http.get<ApiResponse<PricingRunSummary[]>>(`${this.baseUrl}/runs?limit=${limit}`)
            .pipe(map(r => r.data ?? []));
    }

    /**
     * Dettaglio carta per carta di una esecuzione. `outcome` filtra per esito lato server:
     * su una notturna le righe sono migliaia e senza filtro il tetto restituirebbe
     * solo le variazioni di importo maggiore.
     */
    getRunChanges(runId: number, outcome?: string, limit = 500): Observable<PriceChangePage> {
        let url = `${this.baseUrl}/runs/${runId}/changes?limit=${limit}`;
        if (outcome) {
            url += `&outcome=${encodeURIComponent(outcome)}`;
        }
        return this.http.get<ApiResponse<PriceChangePage>>(url)
            .pipe(map(r => r.data ?? { totalCount: 0, returnedCount: 0, items: [] }));
    }

    getCoverage(): Observable<CoverageReport> {
        return this.http.get<ApiResponse<CoverageReport>>(`${this.baseUrl}/coverage`)
            .pipe(map(r => r.data!));
    }
}
