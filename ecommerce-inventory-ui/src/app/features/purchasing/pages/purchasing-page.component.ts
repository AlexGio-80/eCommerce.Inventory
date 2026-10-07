import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, ValueFormatterParams, CellClassParams } from 'ag-grid-community';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import {
  PurchasingService, SealedProductAnalysis, SealedSetAnalysis, SealedSetOption
} from '../services/purchasing.service';

/**
 * Analisi acquisti — Fase 1: convenienza fra i formati di un'uscita.
 * Ogni prodotto è confrontato con il valore delle buste che contiene, ai prezzi di riferimento
 * (il €/busta più basso fra i prodotti fatti di un solo tipo di busta). Prezzi: trend Cardmarket.
 */
@Component({
  selector: 'app-purchasing-page',
  standalone: true,
  imports: [
    CommonModule, FormsModule, AgGridAngular, MatCardModule, MatButtonModule, MatFormFieldModule,
    MatSelectModule, MatProgressSpinnerModule, MatSnackBarModule, MatIconModule, MatTooltipModule,
    MatSlideToggleModule
  ],
  template: `
    <div class="purchasing-container">
      <mat-card class="header-card">
        <mat-card-content>
          <div class="toolbar">
            <mat-form-field appearance="outline" class="set-select">
              <mat-label>Uscita</mat-label>
              <mat-select [ngModel]="selectedCode()" (ngModelChange)="selectSet($event)">
                <mat-option *ngFor="let s of sets()" [value]="s.code">
                  {{ s.code }} — {{ s.name }}<span *ngIf="s.releaseDate"> ({{ s.releaseDate | date:'dd/MM/yyyy' }})</span>
                </mat-option>
              </mat-select>
            </mat-form-field>

            <mat-slide-toggle [ngModel]="showCases()" (ngModelChange)="showCases.set($event)">Mostra case</mat-slide-toggle>

            <span class="spacer"></span>

            <button mat-stroked-button (click)="refreshCardTraderPrices()" [disabled]="!selectedCode() || isRefreshingCt()"
              matTooltip="Chiede a Card Trader i prezzi attuali dei sigillati di questa uscita (poche chiamate)">
              <mat-spinner *ngIf="isRefreshingCt()" diameter="18"></mat-spinner>
              <mat-icon *ngIf="!isRefreshingCt()">sync</mat-icon>
              Prezzi Card Trader
            </button>
            <button mat-button (click)="importCatalog()" [disabled]="isImportingCatalog()"
              matTooltip="Riscarica da MTGJSON il catalogo dei prodotti sigillati e il loro contenuto (gira anche da solo ogni giorno)">
              <mat-spinner *ngIf="isImportingCatalog()" diameter="18"></mat-spinner>
              <mat-icon *ngIf="!isImportingCatalog()">inventory_2</mat-icon>
              Catalogo MTGJSON
            </button>
          </div>

          <div class="summary" *ngIf="analysis() as a">
            <div class="meta">
              <strong>{{ a.name }}</strong>
              <span *ngIf="a.releaseDate"> · uscita {{ a.releaseDate | date:'dd/MM/yyyy' }}</span>
              <span *ngIf="a.childSets.length"> · comprende {{ a.childSets.join(', ') }}</span>
              <span class="muted"> · prezzi Cardmarket del {{ (a.cardmarketPriceDate | date:'dd/MM/yyyy') || '—' }}
                · catalogo aggiornato il {{ (a.catalogImportedAt | date:'dd/MM/yyyy HH:mm') || '—' }}</span>
            </div>
            <div class="references">
              <span class="muted">Prezzo di riferimento per busta (trend CM):</span>
              <span class="reference" *ngFor="let r of a.references" [matTooltip]="'Da ' + r.productName">
                {{ r.label }} <strong>{{ formatEuro(r.pricePerPack) }}</strong>
              </span>
              <span *ngIf="!a.references.length" class="muted">nessuno: mancano prezzi Cardmarket per box o buste</span>
            </div>
            <div class="muted hint">
              "Δ vs buste": quanto costa il prodotto rispetto alle buste che contiene comprate al prezzo di riferimento.
              Negativo = conviene. Mazzi e carte specifiche non sono confrontabili con le buste: si valuteranno con la Fase 2.
            </div>
          </div>
        </mat-card-content>
      </mat-card>

      <mat-card class="grid-card">
        <mat-card-content>
          <div class="loading" *ngIf="isLoading()"><mat-spinner diameter="32"></mat-spinner></div>
          <ag-grid-angular
            class="ag-theme-material"
            [rowData]="visibleProducts()"
            [columnDefs]="columnDefs"
            [defaultColDef]="defaultColDef"
            style="width: 100%; height: 100%;">
          </ag-grid-angular>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .purchasing-container { display: flex; flex-direction: column; gap: 12px; height: 100%; padding: 16px; box-sizing: border-box; }
    .header-card { flex: 0 0 auto; }
    .toolbar { display: flex; align-items: center; gap: 16px; flex-wrap: wrap; }
    .set-select { width: 420px; }
    .spacer { flex: 1; }
    .summary { display: flex; flex-direction: column; gap: 6px; margin-top: -8px; }
    .references { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
    .reference { background: #e8eaf6; border-radius: 12px; padding: 2px 10px; }
    .muted { color: #757575; }
    .hint { font-size: 12px; }
    .grid-card { flex: 1; min-height: 0; display: flex; flex-direction: column; }
    .grid-card mat-card-content { flex: 1; display: flex; flex-direction: column; min-height: 0; position: relative; }
    ag-grid-angular { flex: 1; min-height: 0; }
    .loading { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; z-index: 2; background: rgba(255,255,255,0.6); }
    :host ::ng-deep .ag-theme-material { --ag-header-background-color: #3f51b5; --ag-header-foreground-color: white; }
    :host ::ng-deep .delta-good { color: #2e7d32; font-weight: 600; }
    :host ::ng-deep .delta-bad { color: #c62828; font-weight: 600; }
    :host ::ng-deep .ct-link { color: #3f51b5; text-decoration: none; }
  `]
})
export class PurchasingPageComponent implements OnInit {
  sets = signal<SealedSetOption[]>([]);
  selectedCode = signal<string | null>(null);
  analysis = signal<SealedSetAnalysis | null>(null);
  showCases = signal(false);
  isLoading = signal(false);
  isRefreshingCt = signal(false);
  isImportingCatalog = signal(false);

  /** I case (6 box) di solito non hanno prezzo su Cardmarket e affollano la tabella. */
  visibleProducts = computed(() =>
    (this.analysis()?.products ?? []).filter(p => this.showCases() || !p.isCase));

  private static readonly euroFormat = new Intl.NumberFormat('it-IT', { style: 'currency', currency: 'EUR' });

  formatEuro(value?: number | null): string {
    return value == null ? '' : PurchasingPageComponent.euroFormat.format(value);
  }

  private euro = (params: ValueFormatterParams) => this.formatEuro(params.value);

  columnDefs: ColDef<SealedProductAnalysis>[] = [
    { headerName: 'Prodotto', field: 'name', pinned: 'left', width: 340, filter: 'agTextColumnFilter' },
    {
      headerName: 'Contenuto', field: 'contentsDescription', width: 300,
      tooltipValueGetter: p => p.data?.unresolved ? 'Contenuto non scomponibile in buste (variabile o incompleto su MTGJSON)' : p.value
    },
    { headerName: 'Trend CM', field: 'cmTrend', width: 115, type: 'numericColumn', valueFormatter: this.euro },
    { headerName: 'Low CM', field: 'cmLow', width: 105, type: 'numericColumn', valueFormatter: this.euro },
    {
      headerName: 'Min CT (EN)', field: 'ctMinPrice', width: 125, type: 'numericColumn', valueFormatter: this.euro,
      tooltipValueGetter: p => p.data?.ctPriceUpdatedAt
        ? `${p.data.ctOfferCount ?? 0} offerte, aggiornato il ${new Date(p.data.ctPriceUpdatedAt).toLocaleString('it-IT')}`
        : 'Mai aggiornato: usa "Prezzi Card Trader"'
    },
    { headerName: '€/busta', field: 'pricePerPack', width: 105, type: 'numericColumn', valueFormatter: this.euro },
    {
      headerName: 'Valore buste', field: 'packValue', width: 125, type: 'numericColumn', valueFormatter: this.euro,
      headerTooltip: 'Le buste contenute, al prezzo di riferimento per busta'
    },
    {
      headerName: 'Δ vs buste', field: 'deltaPercent', width: 115, type: 'numericColumn', sort: 'asc',
      valueFormatter: p => p.value == null ? '' : `${p.value > 0 ? '+' : ''}${p.value.toFixed(1)}%`,
      cellClass: (p: CellClassParams) => p.value == null ? '' : p.value < 0 ? 'delta-good' : p.value > 5 ? 'delta-bad' : '',
      headerTooltip: 'Prezzo del prodotto rispetto al valore delle sue buste. Negativo = conviene'
    },
    {
      headerName: 'Note', width: 220,
      valueGetter: p => {
        const d = p.data;
        if (!d) return '';
        if (d.hasFixedContent) return 'Mazzo/carte: valutazione in Fase 2';
        if (d.unresolved) return 'Contenuto non scomponibile';
        if (d.cmTrend == null && d.cmLow == null) return 'Nessun prezzo Cardmarket';
        return '';
      }
    },
    {
      headerName: 'CT', width: 70, sortable: false, filter: false,
      cellRenderer: (p: { data?: SealedProductAnalysis }) => p.data?.cardTraderBlueprintId
        ? `<a class="ct-link" target="_blank" rel="noopener" title="Vedi su Card Trader"
             href="https://www.cardtrader.com/cards/${p.data.cardTraderBlueprintId}"><i class="material-icons" style="font-size:18px;vertical-align:middle">open_in_new</i></a>`
        : ''
    }
  ];

  defaultColDef: ColDef = { sortable: true, resizable: true, filter: true };

  constructor(private purchasing: PurchasingService, private snackBar: MatSnackBar) { }

  ngOnInit() {
    this.loadSets();
  }

  loadSets() {
    this.purchasing.getSets().subscribe({
      next: sets => {
        this.sets.set(sets);
        if (!this.selectedCode() && sets.length) this.selectSet(this.defaultSet(sets));
      },
      error: err => this.snackBar.open(`Errore nel caricare le uscite: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 })
    });
  }

  /** L'uscita più vicina fra quelle in arrivo, altrimenti l'ultima uscita. */
  private defaultSet(sets: SealedSetOption[]): string {
    const today = new Date().toISOString().slice(0, 10);
    const upcoming = sets.filter(s => s.releaseDate && s.releaseDate >= today);
    return (upcoming.length ? upcoming[upcoming.length - 1] : sets[0]).code;
  }

  selectSet(code: string) {
    this.selectedCode.set(code);
    this.loadAnalysis();
  }

  loadAnalysis() {
    const code = this.selectedCode();
    if (!code) return;
    this.isLoading.set(true);
    this.purchasing.getAnalysis(code).subscribe({
      next: analysis => { this.analysis.set(analysis); this.isLoading.set(false); },
      error: err => {
        this.isLoading.set(false);
        this.snackBar.open(`Errore nell'analisi: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 });
      }
    });
  }

  refreshCardTraderPrices() {
    const code = this.selectedCode();
    if (!code) return;
    this.isRefreshingCt.set(true);
    this.purchasing.refreshCardTraderPrices(code).subscribe({
      next: r => {
        this.isRefreshingCt.set(false);
        this.snackBar.open(`Prezzi Card Trader aggiornati: ${r.productsWithOffers}/${r.products} prodotti con offerte`, 'Chiudi', { duration: 6000 });
        this.loadAnalysis();
      },
      error: err => {
        this.isRefreshingCt.set(false);
        this.snackBar.open(`Errore prezzi Card Trader: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 });
      }
    });
  }

  importCatalog() {
    this.isImportingCatalog.set(true);
    this.purchasing.importCatalog().subscribe({
      next: r => {
        this.isImportingCatalog.set(false);
        this.snackBar.open(`Catalogo MTGJSON importato: ${r.products} prodotti, ${r.newProducts} nuovi`, 'Chiudi', { duration: 6000 });
        this.loadSets();
        this.loadAnalysis();
      },
      error: err => {
        this.isImportingCatalog.set(false);
        this.snackBar.open(`Errore catalogo MTGJSON: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 });
      }
    });
  }
}
