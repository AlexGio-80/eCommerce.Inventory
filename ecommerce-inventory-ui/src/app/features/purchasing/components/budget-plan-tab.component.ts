import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import {
  BudgetPlanBasket, BudgetPlanLine, BudgetPlanResult, BudgetPlanStatus, BudgetShipping, PlanCartItem, PurchasingService
} from '../services/purchasing.service';

const LAST_PLAN_KEY = 'budget_plan_last';
const LAST_REQUEST_KEY = 'budget_plan_request';

/**
 * Pianificatore del budget (feature 007, Fase 0): dato quanto si può spendere, quali sigillati comprare, da chi
 * e quanti, fra Card Trader, Cardmarket (al prezzo più basso, venditore da cercare) e il negozio Wizards.
 * Il calcolo legge le offerte Card Trader in background (1-2 minuti); togliere una riga o cambiare le
 * spedizioni ricalcola sulle stesse offerte, subito.
 */
@Component({
  selector: 'app-budget-plan-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatCheckboxModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatProgressBarModule, MatTooltipModule, MatSnackBarModule],
  template: `
    <div class="container">
      <mat-card>
        <mat-card-content>
          <div class="toolbar">
            <mat-form-field appearance="outline" class="budget">
              <mat-label>Budget €</mat-label>
              <input matInput type="number" min="1" step="10" [(ngModel)]="budget">
            </mat-form-field>
            <div class="share">
              <div class="share-label">Aprire <b>{{ openShare }}%</b> · Tenere <b>{{ 100 - openShare }}%</b></div>
              <input type="range" min="0" max="100" step="10" [(ngModel)]="openShare">
            </div>
            <mat-checkbox [(ngModel)]="includeBorderline">Secret Lair anche "Al limite"</mat-checkbox>
            <button mat-stroked-button (click)="showShipping.set(!showShipping())">
              <mat-icon>local_shipping</mat-icon> Spedizioni
            </button>
            <span class="spacer"></span>
            <button mat-raised-button color="primary" [disabled]="running() || !(budget > 0)" (click)="start()">
              <mat-icon>calculate</mat-icon> Calcola il piano
            </button>
          </div>

          @if (showShipping() && shipping) {
            <div class="shipping">
              <mat-form-field appearance="outline"><mat-label>Card Trader, per venditore €</mat-label>
                <input matInput type="number" min="0" step="0.5" [(ngModel)]="shipping.cardTrader"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Card Trader Zero, una volta €</mat-label>
                <input matInput type="number" min="0" step="0.5" [(ngModel)]="shipping.cardTraderZero"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Cardmarket, per venditore €</mat-label>
                <input matInput type="number" min="0" step="0.5" [(ngModel)]="shipping.cardmarket"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Wizards €</mat-label>
                <input matInput type="number" min="0" step="0.5" [(ngModel)]="shipping.wizards"></mat-form-field>
              @if (planId()) {
                <button mat-stroked-button (click)="recompute()">Ricalcola con queste spedizioni</button>
              }
            </div>
          }

          @if (running()) {
            <div class="progress">
              <div>Leggo le offerte Card Trader: {{ status()?.done ?? 0 }} di {{ status()?.total || '…' }} prodotti
                (20 richieste al minuto, ci vuole un minuto o due)</div>
              <mat-progress-bar [mode]="status()?.total ? 'determinate' : 'indeterminate'"
                [value]="status()?.total ? (status()!.done / status()!.total) * 100 : 0"></mat-progress-bar>
            </div>
          }
          @if (error()) { <div class="error">{{ error() }}</div> }
        </mat-card-content>
      </mat-card>

      @if (result(); as r) {
        <mat-card class="summary">
          <mat-card-content>
            <div class="numbers">
              <div><span class="label">Speso</span><span class="big">{{ eur(r.spent) }}</span><span class="label">di {{ eur(r.budget) }}</span></div>
              <div><span class="label">Guadagno atteso aperture</span><span class="big gain">{{ eur(r.openGain) }}</span></div>
              <div><span class="label">Da tenere</span><span class="big">{{ eur(r.holdSpent) }}</span>
                <span class="label">carte per {{ eur(r.holdFloor) }}</span></div>
              <div><span class="label">Spedizioni</span><span class="big">{{ eur(r.shipping) }}</span></div>
              <div><span class="label">Rimasto</span><span class="big">{{ eur(r.remaining) }}</span></div>
            </div>
            @if (r.remainingReason) { <div class="muted">{{ r.remainingReason }}</div> }
            <div class="muted">
              Candidati di oggi: {{ r.openCandidates }} da aprire, {{ r.holdCandidates }} da tenere; offerte lette per
              {{ r.openRead }} e {{ r.holdRead }} (i migliori). Offerte lette alle {{ r.collectedAt | date:'HH:mm' }}.
              @if (r.excluded.length) {
                {{ r.excluded.length }} righe tolte · <a href="" (click)="$event.preventDefault(); restoreAll()">rimettile</a>
              }
            </div>
          </mat-card-content>
        </mat-card>

        @for (b of r.baskets; track b.key) {
          <mat-card class="basket">
            <mat-card-content>
              <div class="basket-head">
                <div>
                  <span class="store">{{ b.store }}</span>
                  @if (b.seller) { <span class="seller">· {{ b.seller }}</span> }
                  @if (b.country) { <span class="muted"> ({{ b.country }})</span> }
                  @if (b.store === 'Cardmarket') { <span class="muted"> · venditore da cercare, al trend (o al prezzo più basso, se più alto) come nella classifica</span> }
                </div>
                <div class="basket-totals">
                  {{ b.lines.length }} {{ b.lines.length === 1 ? 'prodotto' : 'prodotti' }} · spedizione {{ eur(b.shipping) }} ·
                  totale <b>{{ eur(b.total) }}</b>
                  @if (hasGain(b)) { · guadagno <b class="gain">{{ eur(b.gain) }}</b> }
                </div>
              </div>
              <table>
                <thead>
                  <tr><th>Tipo</th><th>Prodotto</th><th class="num">Q.tà</th><th class="num">Prezzo</th>
                    <th class="num">Valore / punteggio</th><th class="num">Guadagno</th><th>Avvisi</th><th></th></tr>
                </thead>
                <tbody>
                  @for (l of b.lines; track l.offerKey) {
                    <tr>
                      <td><span [class]="'kind ' + l.info.kind">{{ kindLabel(l) }}</span></td>
                      <td>
                        <a [href]="productUrl(b, l)" target="_blank" rel="noopener">{{ l.info.name }}</a>
                        @if (l.info.setCode) { <span class="muted"> · {{ l.info.setCode }}</span> }
                      </td>
                      <td class="num">{{ l.quantity }}</td>
                      <td class="num">{{ eur(l.unitPrice) }}</td>
                      <td class="num" [matTooltip]="valueTooltip(l)">{{ valueLabel(l) }}</td>
                      <td class="num gain">{{ l.gain != null ? eur(l.gain) : '—' }}</td>
                      <td class="flags">@for (f of l.info.flags; track f) { <div>{{ f }}</div> }</td>
                      <td><button mat-icon-button matTooltip="Togli dal piano e ricalcola" (click)="exclude(l)"><mat-icon>close</mat-icon></button></td>
                    </tr>
                  }
                </tbody>
              </table>
              <div class="actions">
                @if (b.store === 'Card Trader' || b.store === 'Card Trader Zero') {
                  <button mat-stroked-button color="primary" [disabled]="addingCart() === b.key" (click)="addToCart(b)">
                    <mat-icon>add_shopping_cart</mat-icon> {{ addingCart() === b.key ? 'Aggiungo…' : 'Metti nel carrello Card Trader' }}
                  </button>
                  <a mat-button href="https://www.cardtrader.com/cart/edit" target="_blank" rel="noopener">Apri il carrello</a>
                  @if (hasMultipleCopies(b)) { <span class="muted">Il carrello prende una copia per offerta: le altre aggiungile sul sito.</span> }
                }
              </div>
            </mat-card-content>
          </mat-card>
        }
        @if (r.baskets.length === 0) {
          <mat-card><mat-card-content>Nessun acquisto conveniente con questo budget oggi.</mat-card-content></mat-card>
        }
      }
    </div>
  `,
  styles: [`
    .container { display: flex; flex-direction: column; gap: 12px; padding: 16px 0; }
    .toolbar { display: flex; align-items: center; gap: 16px; flex-wrap: wrap; }
    .budget { width: 140px; }
    .share { display: flex; flex-direction: column; min-width: 220px; }
    .share-label { font-size: 13px; }
    .spacer { flex: 1; }
    .shipping { display: flex; gap: 12px; flex-wrap: wrap; align-items: center; }
    .shipping mat-form-field { width: 210px; }
    .progress { display: flex; flex-direction: column; gap: 6px; font-size: 13px; margin-top: 8px; }
    .error { color: #c62828; background: #ffebee; padding: 8px; border-radius: 4px; margin-top: 8px; }
    .numbers { display: flex; gap: 32px; flex-wrap: wrap; }
    .numbers > div { display: flex; flex-direction: column; }
    .label { font-size: 12px; color: #666; }
    .big { font-size: 22px; font-weight: 500; }
    .gain { color: #2e7d32; }
    .muted { color: #777; font-size: 12px; margin-top: 4px; }
    .basket-head { display: flex; justify-content: space-between; flex-wrap: wrap; gap: 8px; margin-bottom: 8px; }
    .store { font-weight: 500; font-size: 16px; }
    .seller { font-size: 15px; }
    .basket-totals { font-size: 14px; }
    table { width: 100%; border-collapse: collapse; font-size: 13px; }
    th { text-align: left; font-weight: 500; color: #555; border-bottom: 1px solid #ddd; padding: 4px 6px; }
    td { border-bottom: 1px solid #f0f0f0; padding: 4px 6px; vertical-align: top; }
    .num { text-align: right; white-space: nowrap; }
    .flags { font-size: 12px; color: #e65100; }
    .kind { font-size: 11px; padding: 2px 6px; border-radius: 3px; white-space: nowrap; }
    .kind.Open { background: #e8f5e9; color: #2e7d32; }
    .kind.Hold { background: #e3f2fd; color: #1565c0; }
    .kind.SecretLair { background: #f3e5f5; color: #6a1b9a; }
    .actions { display: flex; gap: 8px; align-items: center; margin-top: 8px; flex-wrap: wrap; }
  `]
})
export class BudgetPlanTabComponent implements OnInit, OnDestroy {
  private purchasing = inject(PurchasingService);
  private snackBar = inject(MatSnackBar);

  budget = 300;
  openShare = 70;
  includeBorderline = false;
  shipping: BudgetShipping | null = null;
  showShipping = signal(false);

  planId = signal<string | null>(null);
  status = signal<BudgetPlanStatus | null>(null);
  error = signal<string | null>(null);
  addingCart = signal<string | null>(null);
  running = computed(() => this.status()?.state === 'running');
  result = computed<BudgetPlanResult | null>(() => this.status()?.result ?? null);
  private poll?: ReturnType<typeof setInterval>;

  ngOnInit(): void {
    const saved = localStorage.getItem(LAST_REQUEST_KEY);
    if (saved) {
      try {
        const r = JSON.parse(saved);
        this.budget = r.budget ?? this.budget;
        this.openShare = r.openSharePercent ?? this.openShare;
        this.includeBorderline = !!r.includeSecretLairBorderline;
        this.shipping = r.shipping ?? null;
      } catch { /* richiesta salvata illeggibile: si riparte dai predefiniti */ }
    }
    if (!this.shipping) {
      this.purchasing.getBudgetPlanSettings().subscribe({ next: s => this.shipping = s, error: () => { } });
    }
    // Il piano dell'ultima volta, se il servizio lo ha ancora: cambiando scheda non si perde.
    const lastId = sessionStorage.getItem(LAST_PLAN_KEY);
    if (lastId) this.follow(lastId);
  }

  ngOnDestroy(): void {
    clearInterval(this.poll);
  }

  start(): void {
    this.error.set(null);
    const request = {
      budget: this.budget,
      openSharePercent: this.openShare,
      includeSecretLairBorderline: this.includeBorderline,
      shipping: this.shipping ?? undefined
    };
    localStorage.setItem(LAST_REQUEST_KEY, JSON.stringify(request));
    this.purchasing.startBudgetPlan(request).subscribe({
      next: s => this.follow(s.id, s),
      error: err => this.error.set(err?.error?.message || 'Non riesco ad avviare il calcolo. Riprova.')
    });
  }

  private follow(id: string, first?: BudgetPlanStatus): void {
    this.planId.set(id);
    sessionStorage.setItem(LAST_PLAN_KEY, id);
    if (first) this.status.set(first);
    clearInterval(this.poll);
    const check = () => this.purchasing.getBudgetPlan(id).subscribe({
      next: s => {
        this.status.set(s);
        if (s.state !== 'running') clearInterval(this.poll);
        if (s.state === 'failed') this.error.set(s.error ?? 'Calcolo non riuscito');
      },
      error: () => {
        clearInterval(this.poll);
        sessionStorage.removeItem(LAST_PLAN_KEY);
        this.planId.set(null);
        if (first) this.error.set('Piano non trovato: ricalcolalo');
      }
    });
    check();
    this.poll = setInterval(check, 2000);
  }

  exclude(line: BudgetPlanLine): void {
    const r = this.result();
    if (!r) return;
    this.recompute([...r.excluded, line.offerKey]);
  }

  restoreAll(): void {
    this.recompute([]);
  }

  recompute(excluded?: string[]): void {
    const id = this.planId();
    const r = this.result();
    if (!id || !r || !this.shipping) return;
    localStorage.setItem(LAST_REQUEST_KEY, JSON.stringify({
      budget: r.budget, openSharePercent: r.openSharePercent, includeSecretLairBorderline: this.includeBorderline, shipping: this.shipping
    }));
    this.purchasing.recomputeBudgetPlan(id, this.shipping, excluded ?? r.excluded).subscribe({
      next: s => this.status.set(s),
      error: err => this.error.set(err?.error?.message || 'Ricalcolo non riuscito')
    });
  }

  addToCart(b: BudgetPlanBasket): void {
    const items: PlanCartItem[] = b.lines
      .filter(l => l.info.cardTraderProductId)
      .map(l => ({ cardTraderProductId: l.info.cardTraderProductId!, viaCardTraderZero: b.key === 'ctzero', productName: l.info.name }));
    if (items.length === 0) return;
    this.addingCart.set(b.key);
    this.purchasing.addPlanToCart(items).subscribe({
      next: res => {
        this.addingCart.set(null);
        const parts = [`${res.added} aggiunte`];
        if (res.alreadyInCart) parts.push(`${res.alreadyInCart} già nel carrello`);
        if (res.failed.length) parts.push(`${res.failed.length} rifiutate (vendute o cambiate)`);
        this.snackBar.open('Carrello Card Trader: ' + parts.join(', '), 'OK', { duration: 6000 });
      },
      error: err => {
        this.addingCart.set(null);
        this.snackBar.open(err?.error?.message || 'Carrello non aggiornato', 'OK', { duration: 6000 });
      }
    });
  }

  hasGain(b: BudgetPlanBasket): boolean {
    return b.lines.some(l => l.gain != null);
  }

  hasMultipleCopies(b: BudgetPlanBasket): boolean {
    return b.lines.some(l => l.quantity > 1);
  }

  kindLabel(l: BudgetPlanLine): string {
    return l.info.kind === 'Open' ? 'Apri' : l.info.kind === 'Hold' ? 'Tieni' : 'Secret Lair';
  }

  valueLabel(l: BudgetPlanLine): string {
    if (l.info.kind === 'Hold') return `${l.info.holdScore?.toFixed(0) ?? '—'} punti`;
    return this.eur(l.info.openValueNet);
  }

  valueTooltip(l: BudgetPlanLine): string {
    if (l.info.kind === 'Hold') {
      return `Punteggio "da tenere"; le carte valgono ${this.eur(l.info.openValueNet)} (pavimento ${l.info.floorPercent?.toFixed(0) ?? '—'}%)`;
    }
    if (l.info.kind === 'SecretLair') return `Valore netto stimato del drop; verdetto ${l.info.verdict}, margine ${l.info.roiPercent?.toFixed(0) ?? '—'}%`;
    return `Valore atteso netto dell'apertura; resa in classifica ${l.info.roiPercent?.toFixed(0) ?? '—'}%`;
  }

  productUrl(b: BudgetPlanBasket, l: BudgetPlanLine): string {
    if (l.info.wizardsUrl) return l.info.wizardsUrl;
    if (b.store === 'Cardmarket') {
      return 'https://www.cardmarket.com/en/Magic/Products/Search?searchString=' + encodeURIComponent(l.info.cardmarketName ?? l.info.name);
    }
    return l.info.cardTraderBlueprintId ? 'https://www.cardtrader.com/cards/' + l.info.cardTraderBlueprintId : '#';
  }

  eur(value?: number | null): string {
    return value == null ? '—' : `${value.toFixed(2).replace('.', ',')} €`;
  }
}
