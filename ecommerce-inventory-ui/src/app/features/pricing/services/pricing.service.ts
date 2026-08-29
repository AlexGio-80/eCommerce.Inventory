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

    /** Calcola cosa cambierebbe senza scrivere nulla, né su Card Trader né sullo storico. */
    preview(profileId: number, limit: number): Observable<PricingRunReport> {
        return this.http.post<ApiResponse<PricingRunReport>>(`${this.baseUrl}/preview`, { profileId, limit })
            .pipe(map(r => r.data!));
    }

    /** Esegue davvero: scrive su Card Trader solo se il profilo non è in dry-run. */
    run(profileId: number, highValueThreshold: number, bulkSliceSize: number): Observable<PricingRunReport> {
        return this.http.post<ApiResponse<PricingRunReport>>(`${this.baseUrl}/run`,
            { profileId, highValueThreshold, bulkSliceSize })
            .pipe(map(r => r.data!));
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
