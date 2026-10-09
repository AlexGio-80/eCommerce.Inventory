import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AgGridAngular } from 'ag-grid-angular';
import { CellClassParams, ColDef } from 'ag-grid-community';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { PurchasingService, SecretLairDropEstimate, SecretLairShopProduct, SecretLairShopView, SecretLairValuation } from '../services/purchasing.service';
import { GridStateDirective } from '../../../shared/directives/grid-state.directive';
import { GridCellCopyDirective } from '../../../shared/directives/grid-cell-copy.directive';

/** Prodotto del negozio con la sua valutazione (Fase 3), se calcolata. */
type ShopRow = SecretLairShopProduct & { estimate?: SecretLairDropEstimate };

/**
 * Prodotti del negozio Secret Lair di Wizards, letti tre volte al giorno: prezzo, stato, scorte,
 * carte contenute e link al negozio. I prodotti tolti dal negozio restano, con la data.
 *
 * Accanto, la valutazione del drop: valore stimato dalle stampe esistenti col sovrapprezzo Secret Lair
 * misurato sui drop passati (o dai prezzi reali, dopo l'uscita), margine sul prezzo Wizards e suggerimento.
 */
@Component({
  selector: 'app-secret-lair-shop',
  standalone: true,
  imports: [GridStateDirective, GridCellCopyDirective, CommonModule, FormsModule, AgGridAngular, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatSelectModule, MatProgressSpinnerModule, MatSnackBarModule, MatTooltipModule],
  template: `
    <div class="shop-container">
      <div class="toolbar">
        <mat-form-field appearance="outline" class="status">
          <mat-label>Stato</mat-label>
          <mat-select [ngModel]="status()" (ngModelChange)="status.set($event)">
            <mat-option value="attivi">In negozio</mat-option>
            <mat-option *ngFor="let s of statuses" [value]="s">{{ s }}</mat-option>
            <mat-option value="">Tutti</mat-option>
          </mat-select>
        </mat-form-field>
        <span class="muted" *ngIf="view() as v">
          <ng-container *ngIf="v.runs[0] as last; else neverRun">
            Ultima lettura {{ last.startedAt | date:'dd/MM/yyyy HH:mm' }}:
            <span [class.bad]="last.outcome !== 'Succeeded'">{{ last.outcome === 'Succeeded' ? 'riuscita' : 'fallita' }}</span>
            — {{ last.message }}.
          </ng-container>
          <ng-template #neverRun>Il negozio non è ancora stato letto.</ng-template>
          {{ v.monitorEnabled ? 'Lettura automatica alle 8, 14 e 20.' : 'Lettura automatica disattivata.' }}
          Le scorte indicate dal negozio arrivano al massimo a 10.
        </span>
        <span class="muted model" *ngIf="valuation() as val">
          Stima: ogni carta vale la stampa più economica portata al prezzo tipico della versione Secret Lair
          ({{ val.model.normalSamples + val.model.foilSamples }} carte dei drop passati; una carta da pochi centesimi vale
          circa {{ val.model.normalCurve[0]?.secretLairPrice | number:'1.2-2' }} €, foil {{ val.model.foilCurve[0]?.secretLairPrice | number:'1.2-2' }} €),
          con prezzo realizzato {{ val.model.settings.priceRealizationPercent | number:'1.0-0' }}%, quota venduta per fascia
          e costi {{ val.model.settings.sellingCostPercent }}%. "Compra" sopra il {{ val.model.buyMarginPercent }}% di margine.
          <ng-container *ngIf="val.model.backtest.drops > 0">
            Sui {{ val.model.backtest.drops }} drop passati lo scarto tipico è del {{ val.model.backtest.medianAbsoluteErrorPercent | number:'1.0-0' }}%
            ({{ val.model.backtest.within25Percent | number:'1.0-0' }}% dei drop entro il 25%, {{ biasText(val) }}):
            è un'indicazione, il tema del drop conta e dai prezzi non si vede.
          </ng-container>
        </span>
        <span class="spacer"></span>
        <button mat-stroked-button (click)="refresh()" [disabled]="isRefreshing()"
          matTooltip="Legge subito catalogo e carte dei prodotti nuovi">
          <mat-spinner *ngIf="isRefreshing()" diameter="18"></mat-spinner>
          <mat-icon *ngIf="!isRefreshing()">sync</mat-icon> Leggi ora
        </button>
      </div>
      <div class="grid-wrapper">
        <div class="loading" *ngIf="isLoading()"><mat-spinner diameter="32"></mat-spinner></div>
        <ag-grid-angular appGridState="purchasing-secret-lair-shop-grid" class="ag-theme-material" [rowData]="rows()" [columnDefs]="columnDefs"
          [defaultColDef]="defaultColDef" [tooltipShowDelay]="300" style="width: 100%; height: 100%;"></ag-grid-angular>
      </div>
    </div>
  `,
  styles: [`
    .shop-container { display: flex; flex-direction: column; gap: 8px; height: 100%; }
    .toolbar { display: flex; align-items: center; gap: 16px; flex-wrap: wrap; }
    .status { width: 170px; }
    .spacer { flex: 1; }
    .muted { color: #757575; font-size: 12px; max-width: 900px; }
    .model { max-width: none; flex-basis: 100%; }
    .bad { color: #c62828; font-weight: 600; }
    .grid-wrapper { flex: 1; min-height: 400px; position: relative; }
    .loading { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; z-index: 2; background: rgba(255,255,255,0.6); }
    :host { display: block; height: 100%; }
    :host ::ng-deep .status-ok { color: #2e7d32; font-weight: 600; }
    :host ::ng-deep .status-pre { color: #3f51b5; font-weight: 600; }
    :host ::ng-deep .status-off { color: #9e9e9e; }
    :host ::ng-deep .verdict-buy { color: #2e7d32; font-weight: 700; }
    :host ::ng-deep .verdict-edge { color: #ef6c00; font-weight: 600; }
    :host ::ng-deep .verdict-skip { color: #c62828; font-weight: 600; }
    :host ::ng-deep .verdict-none { color: #9e9e9e; }
    :host ::ng-deep .positive { color: #2e7d32; }
    :host ::ng-deep .negative { color: #c62828; }
    :host ::ng-deep .sl-link { color: #3f51b5; text-decoration: none; font-size: 11px; font-weight: 600; }
  `]
})
export class SecretLairShopComponent implements OnInit {
  readonly statuses = ['Disponibile', 'Preordine', 'In arrivo', 'Esaurito', 'Tolto dal negozio'];

  view = signal<SecretLairShopView | null>(null);
  valuation = signal<SecretLairValuation | null>(null);
  status = signal('attivi');
  isLoading = signal(false);
  isRefreshing = signal(false);

  visible = computed(() => (this.view()?.products ?? []).filter(p =>
    !this.status() ? true
      : this.status() === 'attivi' ? p.status !== 'Tolto dal negozio'
      : p.status === this.status()));

  private estimates = computed(() => new Map((this.valuation()?.products ?? []).map(e => [e.wizardsProductId, e])));
  rows = computed<ShopRow[]>(() => this.visible().map(p => ({ ...p, estimate: this.estimates().get(p.wizardsProductId) })));

  private static readonly euroFormat = new Intl.NumberFormat('it-IT', { style: 'currency', currency: 'EUR' });
  private static date(value?: string | null): string {
    return value ? new Date(value).toLocaleDateString('it-IT') : '';
  }
  private static euro(value?: number | null): string {
    return value == null ? '' : SecretLairShopComponent.euroFormat.format(value);
  }
  private static percent(value?: number | null): string {
    return value == null ? '' : `${value > 0 ? '+' : ''}${value.toLocaleString('it-IT', { maximumFractionDigits: 0 })}%`;
  }

  /** Dettaglio della stima, riga per riga, per il tooltip del valore. */
  private static breakdown(e?: SecretLairDropEstimate): string {
    if (!e) return '';
    const euro = SecretLairShopComponent.euro;
    const real = (price?: number) => price != null ? `, reale ${euro(price)}` : '';
    const lines = e.cards.map(c => {
      const head = `${c.quantity}x ${c.line}${c.foil ? ' (foil)' : ''}`;
      switch (c.kind) {
        case 'Carta': return `${head}: stampa base ${euro(c.basePrice)} (${c.baseSet}) → SL ${euro(c.estimatedPrice)}${real(c.realPrice)}`;
        case 'Senza stampe': return `${head}: mai stampata prima, stima debole ${euro(c.estimatedPrice)}${real(c.realPrice)}`;
        case 'Prodotto': return `${head}: drop del bundle, stima ${euro(c.estimatedPrice)}${real(c.realPrice)}`;
        default: return `${head}: non stimata`;
      }
    });
    const totals = [`Somma stimata (trend CM): ${euro(e.estimatedTrend)}, netto ${euro(e.estimatedNetValue)}`];
    if (e.realNetValue != null) totals.push(`Prezzi reali (trend CM): ${euro(e.realTrend)}, netto ${euro(e.realNetValue)}`);
    if (e.weakSharePercent > 0) totals.push(`${e.weakSharePercent.toLocaleString('it-IT')}% del valore da carte senza stampe precedenti`);
    return [...lines, '', ...totals].join('\n');
  }

  biasText(valuation: SecretLairValuation): string {
    const bias = valuation.model.backtest.medianBiasPercent ?? 0;
    return `tendenza a ${bias < 0 ? 'sottostimare' : 'sovrastimare'} del ${Math.abs(bias).toLocaleString('it-IT', { maximumFractionDigits: 0 })}%`;
  }

  columnDefs: ColDef<ShopRow>[] = [
    { headerName: 'Superdrop', field: 'dropName', width: 230, filter: 'agTextColumnFilter' },
    { headerName: 'Prodotto', field: 'title', pinned: 'left', width: 330, filter: 'agTextColumnFilter',
      valueFormatter: p => (p.value ?? '').replace(/^Secret Lair x /, '') },
    { headerName: 'Foil', field: 'isFoil', width: 80, valueFormatter: p => p.value ? 'Foil' : '' },
    { headerName: 'Prezzo', field: 'price', width: 95, type: 'numericColumn',
      valueFormatter: p => p.value == null ? '' : SecretLairShopComponent.euroFormat.format(p.value) },
    { headerName: 'Stima', colId: 'verdict', width: 115, valueGetter: p => p.data?.estimate?.verdict ?? '',
      cellClass: (p: CellClassParams) => p.value === 'Compra' ? 'verdict-buy' : p.value === 'Al limite' ? 'verdict-edge'
        : p.value === 'Lascia' ? 'verdict-skip' : 'verdict-none',
      headerTooltip: 'Suggerimento dal valore netto contro il prezzo Wizards: Compra sopra la soglia di margine, Al limite fra 0 e la soglia, Lascia sotto zero' },
    { headerName: 'Valore netto', colId: 'netValue', width: 120, type: 'numericColumn',
      valueGetter: p => p.data?.estimate?.realNetValue ?? p.data?.estimate?.estimatedNetValue ?? null,
      valueFormatter: p => SecretLairShopComponent.euro(p.value),
      tooltipValueGetter: p => SecretLairShopComponent.breakdown(p.data?.estimate),
      headerTooltip: 'Quanto si incassa vendendo le carte a singole, al netto di costi e quota invenduta: ai prezzi reali se il drop ne ha, altrimenti stimato. Passa il mouse per il dettaglio carta per carta' },
    { headerName: 'Fonte', colId: 'source', width: 110,
      valueGetter: p => !p.data?.estimate ? '' : p.data.estimate.realNetValue != null ? 'Prezzi reali'
        : p.data.estimate.estimatedNetValue != null ? (p.data.estimate.weakSharePercent >= 50 ? 'Stima debole' : 'Stima') : '',
      headerTooltip: 'Prezzi reali = trend Cardmarket delle carte del drop; Stima = stampe esistenti col sovrapprezzo Secret Lair; debole se metà del valore viene da carte mai stampate prima' },
    { headerName: 'Margine', colId: 'margin', width: 100, type: 'numericColumn',
      valueGetter: p => p.data?.estimate?.marginPercent ?? null,
      valueFormatter: p => SecretLairShopComponent.percent(p.value),
      cellClass: (p: CellClassParams) => p.value == null ? '' : p.value >= 0 ? 'positive' : 'negative',
      headerTooltip: 'Valore netto contro il prezzo Wizards' },
    { headerName: 'Non stimate', colId: 'unknown', width: 110, type: 'numericColumn',
      valueGetter: p => p.data?.estimate?.unknownLines || null,
      headerTooltip: 'Righe del negozio senza stima (carte a sorpresa, ristampe non elencate, gadget): il valore vero è più alto' },
    { headerName: 'Stato', field: 'status', width: 140,
      cellClass: (p: CellClassParams) => p.value === 'Disponibile' ? 'status-ok'
        : p.value === 'Preordine' || p.value === 'In arrivo' ? 'status-pre' : 'status-off' },
    { headerName: 'Scorte', field: 'stock', width: 90, type: 'numericColumn',
      valueFormatter: p => p.value == null ? '' : p.value >= 10 ? '10+' : String(p.value),
      headerTooltip: 'Copie disponibili secondo il negozio: oltre 10 indica sempre 10' },
    { headerName: 'Limite', field: 'limitPerCustomer', width: 85, type: 'numericColumn', headerTooltip: 'Copie massime per cliente' },
    { headerName: 'Carte', width: 320,
      valueGetter: p => p.data?.contentsKnown ? p.data.cards.map(c => (c.quantity > 1 ? `${c.quantity}x ` : '') + c.cardName).join(', ') : '(da leggere)',
      tooltipValueGetter: p => p.data?.cards.map(c => `${c.quantity}x ${c.cardName}${c.displayName ? ` — "${c.displayName}"` : ''}`).join('\n') ?? '' },
    { headerName: 'In vendita dal', field: 'saleStart', width: 125, valueFormatter: p => SecretLairShopComponent.date(p.value) },
    { headerName: 'Spedizione', field: 'releaseDate', width: 115, valueFormatter: p => SecretLairShopComponent.date(p.value),
      headerTooltip: 'Data di uscita indicata dal negozio' },
    { headerName: 'Visto dal', field: 'firstSeenAt', width: 110, sort: 'desc', valueFormatter: p => SecretLairShopComponent.date(p.value) },
    { headerName: 'Esaurito il', field: 'soldOutAt', width: 110, valueFormatter: p => SecretLairShopComponent.date(p.value) },
    { headerName: 'Tolto il', field: 'removedAt', width: 100, valueFormatter: p => SecretLairShopComponent.date(p.value) },
    { headerName: "Stima prima dell'uscita", colId: 'frozen', width: 150, type: 'numericColumn',
      valueGetter: p => p.data?.estimate?.frozenNetValue ?? null,
      valueFormatter: p => SecretLairShopComponent.euro(p.value),
      tooltipValueGetter: p => p.data?.estimate?.frozenAt ? `Congelata il ${SecretLairShopComponent.date(p.data.estimate.frozenAt)}` : '',
      headerTooltip: 'Valore netto stimato quando il drop non aveva ancora prezzi propri: non cambia più' },
    { headerName: 'Scarto stima', colId: 'frozenError', width: 115, type: 'numericColumn',
      valueGetter: p => p.data?.estimate?.frozenErrorPercent ?? null,
      valueFormatter: p => SecretLairShopComponent.percent(p.value),
      headerTooltip: "Stima prima dell'uscita contro il valore ai prezzi reali: positivo = la stima era troppo alta" },
    {
      headerName: 'Wizards', width: 90, sortable: false, filter: false,
      cellRenderer: (p: { data?: ShopRow }) => p.data
        ? `<a class="sl-link" target="_blank" rel="noopener" title="Apri nel negozio Secret Lair" href="${p.data.url}">Negozio</a>`
        : ''
    }
  ];

  defaultColDef: ColDef = { sortable: true, resizable: true, filter: true };

  constructor(private purchasing: PurchasingService, private snackBar: MatSnackBar) { }

  ngOnInit() {
    this.load();
  }

  load() {
    this.isLoading.set(true);
    this.purchasing.getSecretLairValuation().subscribe({
      next: valuation => this.valuation.set(valuation),
      error: err => this.snackBar.open(`Stima dei drop non disponibile: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 })
    });
    this.purchasing.getSecretLairShop().subscribe({
      next: view => { this.view.set(view); this.isLoading.set(false); },
      error: err => {
        this.isLoading.set(false);
        this.snackBar.open(`Errore nel negozio Secret Lair: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 });
      }
    });
  }

  refresh() {
    this.isRefreshing.set(true);
    this.purchasing.refreshSecretLairShop().subscribe({
      next: r => {
        this.isRefreshing.set(false);
        this.snackBar.open(`Negozio letto: ${r.message}`, 'Chiudi', { duration: 6000 });
        this.load();
      },
      error: err => {
        this.isRefreshing.set(false);
        this.snackBar.open(`Lettura del negozio non riuscita: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 });
        this.load();
      }
    });
  }
}
