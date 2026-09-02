import { Injectable, computed, signal } from '@angular/core';
import { Subject } from 'rxjs';
import { PricingRunStatus, PricingService } from './pricing.service';

/**
 * Segue l'esecuzione dell'autopricer da qualunque punto dell'applicazione.
 *
 * È il pezzo che permette di lanciare un ricalcolo e poi andarsene sulla maschera di
 * inserimento o sulla coda degli ordini da preparare: lo stato non vive nel componente
 * che ha premuto il pulsante, ma qui — e a monte sul server, che scrive l'avanzamento a
 * ogni carta valutata. Ricaricando il browser l'esecuzione si ritrova comunque.
 *
 * Il ritmo è volutamente diverso fra i due casi: mentre qualcosa gira serve vedere il
 * progresso, a riposo un controllo ogni tanto basta e avanza.
 */
@Injectable({ providedIn: 'root' })
export class PricingRunMonitorService {
    private static readonly POLL_ACTIVE_MS = 4000;
    private static readonly POLL_IDLE_MS = 20000;

    private readonly _status = signal<PricingRunStatus | null>(null);
    private timer?: ReturnType<typeof setTimeout>;
    private started = false;

    /** Esecuzione in corso, `null` se l'autopricer è fermo. */
    readonly status = this._status.asReadonly();
    readonly isRunning = computed(() => this._status() !== null);

    /**
     * Percentuale sul pianificato, o `null` durante la preparazione: prima che le carte
     * siano state selezionate non esiste un denominatore, e mostrare 0% farebbe pensare
     * a uno stallo invece che a un lavoro non ancora cominciato.
     */
    readonly progressPercent = computed(() => {
        const s = this._status();
        if (!s?.plannedCount) return null;
        return Math.round(((s.evaluatedCount ?? 0) / s.plannedCount) * 100);
    });

    /** Si alza quando un'esecuzione seguita arriva a termine: le pagine ricaricano lo storico. */
    readonly runCompleted$ = new Subject<number | null>();

    constructor(private pricingService: PricingService) { }

    /**
     * Avvia il controllo periodico. Va chiamato dal guscio dell'applicazione, che esiste
     * solo dopo l'autenticazione: partire prima significherebbe una raffica di 401.
     */
    start(): void {
        if (this.started) return;
        this.started = true;
        this.poll();
    }

    stop(): void {
        this.started = false;
        if (this.timer) clearTimeout(this.timer);
        this.timer = undefined;
        this._status.set(null);
    }

    /** Controllo immediato, da usare subito dopo aver lanciato o interrotto un'esecuzione. */
    refreshNow(): void {
        if (this.timer) clearTimeout(this.timer);
        this.poll();
    }

    private poll(): void {
        if (!this.started) return;

        this.pricingService.getCurrentRun().subscribe({
            next: status => this.apply(status),
            // Un errore di rete non deve spegnere il monitoraggio: si riprova al giro dopo.
            error: () => this.schedule(PricingRunMonitorService.POLL_IDLE_MS)
        });
    }

    private apply(status: PricingRunStatus | null): void {
        const previous = this._status();
        this._status.set(status);

        if (previous && !status) {
            this.runCompleted$.next(previous.runId ?? null);
        }

        this.schedule(status
            ? PricingRunMonitorService.POLL_ACTIVE_MS
            : PricingRunMonitorService.POLL_IDLE_MS);
    }

    private schedule(delayMs: number): void {
        if (!this.started) return;
        if (this.timer) clearTimeout(this.timer);
        this.timer = setTimeout(() => this.poll(), delayMs);
    }
}
