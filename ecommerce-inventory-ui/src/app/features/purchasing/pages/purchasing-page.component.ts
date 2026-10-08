import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AgGridAngular } from 'ag-grid-angular';
import { GridCellCopyDirective } from '../../../shared/directives/grid-cell-copy.directive';
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
import { MatInputModule } from '@angular/material/input';
import { MatTabsModule } from '@angular/material/tabs';
import { OpeningsTabComponent } from '../components/openings-tab.component';
import { OpportunitiesTabComponent } from '../components/opportunities-tab.component';
import { AlertsTabComponent } from '../components/alerts-tab.component';
import { PurchasesTabComponent } from '../components/purchases-tab.component';
import {
  OpeningValueParams, PackValue, PurchasingService, SealedProductAnalysis, SealedSetAnalysis, SealedSetOption
} from '../services/purchasing.service';
import { GridStateDirective } from '../../../shared/directives/grid-state.directive';

/**
 * Analisi acquisti — Fase 1: convenienza fra i formati di un'uscita.
 * Ogni prodotto è confrontato con il valore delle buste che contiene, ai prezzi di riferimento
 * (il €/busta più basso fra i prodotti fatti di un solo tipo di busta). Prezzi: trend Cardmarket.
 */
@Component({
  selector: 'app-purchasing-page',
  standalone: true,
  imports: [GridStateDirective, 
    CommonModule, FormsModule, AgGridAngular, GridCellCopyDirective, MatCardModule, MatButtonModule, MatFormFieldModule,
    MatSelectModule, MatProgressSpinnerModule, MatSnackBarModule, MatIconModule, MatTooltipModule,
    MatSlideToggleModule, MatInputModule, MatTabsModule, OpeningsTabComponent, OpportunitiesTabComponent, PurchasesTabComponent, AlertsTabComponent
  ],
  template: `
    <mat-tab-group class="tabs" [(selectedIndex)]="tabIndex" animationDuration="0ms">
    <mat-tab label="Analisi uscita">
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
              matTooltip="Chiede a Card Trader i prezzi attuali dei sigillati e delle singole di questa uscita (poche chiamate)">
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
              Negativo = conviene. Mazzi e carte specifiche non sono confrontabili con le buste: per quelli vale il valore atteso.
            </div>

            <div class="warning" *ngIf="!a.detailImportedAt || !a.hasBoosterData">
              <mat-icon>info</mat-icon>
              <span *ngIf="!a.detailImportedAt">Carte, composizione delle buste e mazzi di questa uscita non sono ancora stati scaricati da MTGJSON.</span>
              <span *ngIf="a.detailImportedAt && !a.hasBoosterData">MTGJSON non ha ancora pubblicato la composizione delle buste (di solito arriva intorno all'uscita): il valore atteso delle buste non si può calcolare.</span>
              <button mat-stroked-button (click)="importDetails()" [disabled]="isImportingDetails()">
                <mat-spinner *ngIf="isImportingDetails()" diameter="18"></mat-spinner>
                Scarica dati delle buste
              </button>
            </div>

            <div class="settings">
              <span class="muted">Valore atteso:</span>
              <mat-form-field appearance="outline" class="num">
                <mat-label>Soglia bulk €</mat-label>
                <input matInput type="number" step="0.01" [(ngModel)]="params.bulkThreshold">
              </mat-form-field>
              <mat-form-field appearance="outline" class="num">
                <mat-label>Prezzo bulk €</mat-label>
                <input matInput type="number" step="0.01" [(ngModel)]="params.bulkPrice">
              </mat-form-field>
              <mat-form-field appearance="outline" class="num" [matTooltip]="sellThroughTooltip()" matTooltipClass="multiline-tooltip">
                <mat-label>Bulk venduto %</mat-label>
                <input matInput type="number" step="1" [(ngModel)]="params.bulkSellThroughPercent">
              </mat-form-field>
              <mat-form-field appearance="outline" class="num"
                [matTooltip]="a.settings.measuredCardTraderFeePercent != null ? 'Commissione Card Trader misurata sui tuoi ordini: ' + a.settings.measuredCardTraderFeePercent + '%. Il resto è spedizione, imballaggio, lavoro.' : ''">
                <mat-label>Costi vendita %</mat-label>
                <input matInput type="number" step="1" [(ngModel)]="params.sellingCostPercent">
              </mat-form-field>
              <mat-form-field appearance="outline" class="num" [matTooltip]="priceRealizationTooltip()">
                <mat-label>Prezzo realizzato %</mat-label>
                <input matInput type="number" step="1" [(ngModel)]="params.priceRealizationPercent">
              </mat-form-field>
              <button mat-stroked-button (click)="loadAnalysis()">Ricalcola</button>
              <button mat-button (click)="resetParams()" matTooltip="Torna a configurazione e valori misurati sulle vendite">Predefiniti</button>
            </div>

            <div class="pack-values" *ngIf="a.packValues.length">
              <div class="pack-value" *ngFor="let pv of a.packValues" (click)="togglePack(pv.packKey)"
                [class.open]="openPack() === pv.packKey">
                <div class="pack-title">{{ pv.label }}</div>
                <div>netto <strong>{{ formatEuro(pv.netCm) }}</strong> <span class="muted">CM</span>
                  <span *ngIf="pv.netCt != null"> · <strong>{{ formatEuro(pv.netCt) }}</strong> <span class="muted">CT</span></span>
                </div>
                <div class="muted small">lordo {{ formatEuro(pv.valueCm) }} · copertura {{ pv.coverageCm }}%</div>
              </div>
            </div>

            <div class="pack-detail" *ngIf="selectedPackValue() as pv">
              <div class="detail-col">
                <div class="detail-title">Da dove viene il valore di una busta {{ pv.label }} (CM, lordo)</div>
                <table>
                  <tr><th>Foglio</th><th>Slot/busta</th><th>Valore/slot</th><th>Contributo</th><th>Copertura</th></tr>
                  <tr *ngFor="let sh of pv.sheets">
                    <td>{{ sh.name }}</td><td>{{ sh.slotsPerPack }}</td><td>{{ formatEuro(sh.valuePerSlot) }}</td>
                    <td>{{ formatEuro(sh.slotsPerPack * sh.valuePerSlot) }}</td><td>{{ sh.coveragePercent }}%</td>
                  </tr>
                </table>
              </div>
              <div class="detail-col">
                <div class="detail-title">Carte che pesano di più</div>
                <table>
                  <tr><th>Carta</th><th>Prezzo</th><th>Prob./busta</th><th>Contributo</th></tr>
                  <tr *ngFor="let c of pv.topCards">
                    <td>{{ c.name }}<span class="muted"> {{ c.setCode }} #{{ c.number }}</span><span *ngIf="c.foil"> ✦</span></td>
                    <td>{{ formatEuro(c.value) }}</td><td>{{ c.probabilityPercent }}%</td><td>{{ formatEuro(c.expectedValue) }}</td>
                  </tr>
                </table>
              </div>
            </div>
          </div>
        </mat-card-content>
      </mat-card>

      <mat-card class="grid-card">
        <mat-card-content>
          <div class="loading" *ngIf="isLoading()"><mat-spinner diameter="32"></mat-spinner></div>
          <ag-grid-angular appGridState="purchasing-analysis-grid"
            class="ag-theme-material"
            [rowData]="visibleProducts()"
            [columnDefs]="columnDefs"
            [defaultColDef]="defaultColDef"
            style="width: 100%; height: 100%;">
          </ag-grid-angular>
        </mat-card-content>
      </mat-card>
    </div>
    </mat-tab>

    <mat-tab label="Opportunità">
      <ng-template matTabContent>
        <app-opportunities-tab (openRelease)="openReleaseFromOpportunities($event)"></app-opportunities-tab>
      </ng-template>
    </mat-tab>

    <mat-tab label="Aperture">
      <ng-template matTabContent>
        <app-openings-tab></app-openings-tab>
      </ng-template>
    </mat-tab>

    <mat-tab label="Registro acquisti">
      <app-purchases-tab [products]="analysis()?.products ?? []" [setCode]="selectedCode()"
        [setName]="analysis()?.name ?? null" [prefillProductId]="prefillProductId()"></app-purchases-tab>
    </mat-tab>

    <mat-tab label="Avvisi">
      <ng-template matTabContent>
        <app-alerts-tab [products]="analysis()?.products ?? []" [setCode]="selectedCode()" [setName]="analysis()?.name ?? null"
          (openRelease)="openReleaseFromAlert($event)"></app-alerts-tab>
      </ng-template>
    </mat-tab>
    </mat-tab-group>
  `,
  styles: [`
    :host { display: block; height: 100%; }
    .tabs { height: 100%; padding: 0 16px; box-sizing: border-box; }
    .tabs ::ng-deep .mat-mdc-tab-body-wrapper { flex: 1; }
    .tabs ::ng-deep .mat-mdc-tab-body-content { height: 100%; }
    .purchasing-container { display: flex; flex-direction: column; gap: 12px; height: 100%; padding: 16px 0; box-sizing: border-box; }
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
    :host ::ng-deep .register-btn { background: none; border: none; cursor: pointer; color: #3f51b5; }
    :host ::ng-deep .decision-open { color: #2e7d32; font-weight: 600; }
    :host ::ng-deep .decision-keep { color: #455a64; }
    :host ::ng-deep .decision-warn { color: #ef6c00; }
    .warning { display: flex; align-items: center; gap: 8px; background: #fff3e0; border-radius: 4px; padding: 6px 10px; }
    .settings { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin-top: 4px; }
    .settings .num { width: 130px; }
    .settings ::ng-deep .mat-mdc-form-field-subscript-wrapper { display: none; }
    .pack-values { display: flex; gap: 10px; flex-wrap: wrap; }
    .pack-value { border: 1px solid #c5cae9; border-radius: 6px; padding: 6px 10px; cursor: pointer; min-width: 170px; }
    .pack-value.open { background: #e8eaf6; border-color: #3f51b5; }
    .pack-title { font-weight: 600; }
    .small { font-size: 12px; }
    .pack-detail { display: flex; gap: 24px; flex-wrap: wrap; font-size: 13px; }
    .detail-title { font-weight: 600; margin-bottom: 4px; }
    .pack-detail table { border-collapse: collapse; }
    .pack-detail th, .pack-detail td { padding: 2px 10px 2px 0; text-align: left; }
    .pack-detail th { color: #757575; font-weight: 500; }
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
  isImportingDetails = signal(false);
  tabIndex = 0;
  prefillProductId = signal<number | null>(null);

  priceRealizationTooltip = computed(() => {
    const st = this.analysis()?.settings;
    if (!st) return '';
    return st.priceRealizationMeasured
      ? `Incassato sulle tue vendite degli ultimi 30 giorni rispetto al trend Cardmarket (carte da 1 € in su, ${st.priceRealizationSampleCopies} copie): ${st.measuredPriceRealizationPercent}%. Si applica alle carte sopra soglia nel valore atteso CM.`
      : 'Troppe poche vendite recenti per misurarlo: nessuna correzione (100%)';
  });
  openPack = signal<string | null>(null);

  /** Parametri del valore atteso; vuoti = configurazione e quota di bulk misurata. */
  params: OpeningValueParams = {};

  selectedPackValue = computed(() =>
    this.analysis()?.packValues.find(p => p.packKey === this.openPack()) ?? null);

  sellThroughTooltip = computed(() => {
    const st = this.analysis()?.settings;
    if (!st) return '';
    if (!st.bulkSellThroughMeasured) return 'Nessuna apertura all\'uscita da cui misurarla: valore di ripiego';
    const lines = st.bulkSellThroughExpansions.map(e => `${e.name}: ${e.sharePercent}% (${e.sold}/${e.sold + e.inStock})`);
    return `Misurata sulle tue aperture all'uscita: ${st.measuredBulkSellThroughPercent}%\n` + lines.join('\n');
  });

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
      headerName: 'Apri (CM)', field: 'openValueCm', width: 120, type: 'numericColumn', valueFormatter: this.euro,
      headerTooltip: 'Valore atteso aprendo, al netto di bulk e costi di vendita, su prezzi Cardmarket',
      tooltipValueGetter: p => this.coverageTooltip(p.data, 'cm')
    },
    {
      headerName: 'Apri (CT)', field: 'openValueCt', width: 120, type: 'numericColumn', valueFormatter: this.euro,
      headerTooltip: 'Lo stesso su prezzi Card Trader (si aggiornano con "Prezzi Card Trader")',
      tooltipValueGetter: p => this.coverageTooltip(p.data, 'ct')
    },
    {
      headerName: 'Sigillato netto', field: 'sealedNetCm', width: 130, type: 'numericColumn', valueFormatter: this.euro,
      headerTooltip: 'Ricavato rivendendolo chiuso al trend CM, al netto dei costi di vendita'
    },
    {
      headerName: 'Resa apertura', field: 'openingRoiPercent', width: 125, type: 'numericColumn',
      valueFormatter: p => p.value == null ? '' : `${p.value > 0 ? '+' : ''}${p.value.toFixed(1)}%`,
      cellClass: (p: CellClassParams) => p.value == null ? '' : p.value > 0 ? 'delta-good' : 'delta-bad',
      headerTooltip: 'Valore atteso netto aprendo rispetto al prezzo di acquisto (trend CM)'
    },
    {
      headerName: 'Decisione', field: 'decision', width: 150,
      cellClass: (p: CellClassParams) => p.value === 'Apri' ? 'decision-open'
        : p.value === 'Tieni sigillato' ? 'decision-keep' : p.value ? 'decision-warn' : '',
      tooltipValueGetter: p => {
        const d = p.data;
        if (!d) return '';
        if (d.decision === 'Prezzo CM dubbio') return `Il trend CM (${this.formatEuro(d.cmTrend)}) è lontano dalla somma di ciò che contiene (${this.formatEuro(d.componentsTrend)}): probabile abbinamento sbagliato su MTGJSON`;
        if (d.decision === 'Dati incompleti') return this.coverageTooltip(d, 'cm');
        if (d.decision) return `Aprendo ${this.formatEuro(d.openValueCm)} netti, rivendendolo chiuso ${this.formatEuro(d.sealedNetCm)} netti`;
        return '';
      }
    },
    {
      headerName: 'Note', width: 200,
      valueGetter: p => {
        const d = p.data;
        if (!d) return '';
        if (d.unresolved) return 'Contenuto non scomponibile';
        if (d.cmTrend == null && d.cmLow == null) return 'Nessun prezzo Cardmarket';
        return '';
      }
    },
    {
      headerName: '', width: 60, sortable: false, filter: false, pinned: 'right',
      cellRenderer: () => `<button class="register-btn" title="Registra un acquisto di questo prodotto"><i class="material-icons" style="font-size:18px">add_shopping_cart</i></button>`,
      onCellClicked: p => { if (p.data) this.registerPurchase(p.data); }
    },
    {
      headerName: 'CT', width: 70, sortable: false, filter: false,
      cellRenderer: (p: { data?: SealedProductAnalysis }) => p.data?.cardTraderBlueprintId
        ? `<a class="ct-link" target="_blank" rel="noopener" title="Vedi su Card Trader"
             href="https://www.cardtrader.com/cards/${p.data.cardTraderBlueprintId}"><i class="material-icons" style="font-size:18px;vertical-align:middle">open_in_new</i></a>`
        : ''
    },
    {
      // Cardmarket non ha un indirizzo per id prodotto: si apre la ricerca col nome esatto.
      headerName: 'CM', width: 70, sortable: false, filter: false,
      cellRenderer: (p: { data?: SealedProductAnalysis }) => p.data?.cardmarketName
        ? `<a class="ct-link" target="_blank" rel="noopener" title="Cerca su Cardmarket"
             href="https://www.cardmarket.com/en/Magic/Products/Search?searchString=${encodeURIComponent(p.data.cardmarketName)}"><i class="material-icons" style="font-size:18px;vertical-align:middle">open_in_new</i></a>`
        : ''
    }
  ];

  defaultColDef: ColDef = { sortable: true, resizable: true, filter: true };

  private refreshCtAfterLoad = false;

  private route = inject(ActivatedRoute);
  private destroyRef = inject(DestroyRef);

  constructor(private purchasing: PurchasingService, private snackBar: MatSnackBar) { }

  private coverageTooltip(d: SealedProductAnalysis | undefined, source: 'cm' | 'ct'): string {
    if (!d) return '';
    const coverage = source === 'cm' ? d.coverageCm : d.coverageCt;
    const missing = [...d.missingPacks.map(p => `busta ${p}`), ...d.missingDecks.map(m => `mazzo ${m}`)];
    const parts: string[] = [];
    if (coverage != null) parts.push(`Copertura prezzi ${coverage}%`);
    if (missing.length) parts.push(`Senza composizione: ${missing.join(', ')}`);
    return parts.join(' · ');
  }

  /** Dalla classifica delle opportunità all'analisi completa dell'uscita. */
  openReleaseFromOpportunities(code: string | undefined) {
    if (!code) return;
    this.selectSet(code);
    this.tabIndex = 0;
  }

  /** Da un avviso si va a decidere un acquisto: servono anche i prezzi Card Trader aggiornati. */
  openReleaseFromAlert(code: string | undefined) {
    if (!code) return;
    this.refreshCtAfterLoad = true;
    this.openReleaseFromOpportunities(code);
  }

  /**
   * I prezzi Card Trader non arrivano con l'import del mattino ma con "Prezzi Card Trader" (fra due e
   * sei chiamate all'API): vecchi se mancano o hanno più di 6 ore su qualche prodotto venduto su CT.
   */
  private ctPricesStale(analysis: SealedSetAnalysis): boolean {
    const limit = Date.now() - 6 * 60 * 60 * 1000;
    return analysis.products.some(p => p.cardTraderBlueprintId
      && (!p.ctPriceUpdatedAt || new Date(p.ctPriceUpdatedAt).getTime() < limit));
  }

  /** Dalla tabella di analisi al registro acquisti, con il prodotto già scelto. */
  registerPurchase(product: SealedProductAnalysis) {
    this.prefillProductId.set(null);
    setTimeout(() => {
      this.prefillProductId.set(product.id);
      this.tabIndex = 3;
    });
  }

  togglePack(packKey: string) {
    this.openPack.set(this.openPack() === packKey ? null : packKey);
  }

  resetParams() {
    this.params = {};
    this.loadAnalysis();
  }

  /** Allinea i campi ai valori usati dal server, così si vede cosa è stato applicato. */
  private syncParams(analysis: SealedSetAnalysis) {
    const st = analysis.settings;
    this.params = {
      bulkThreshold: st.bulkThreshold,
      bulkPrice: st.bulkPrice,
      bulkSellThroughPercent: st.bulkSellThroughPercent,
      sellingCostPercent: st.sellingCostPercent,
      priceRealizationPercent: st.priceRealizationPercent
    };
  }

  importDetails() {
    const code = this.selectedCode();
    if (!code) return;
    this.isImportingDetails.set(true);
    this.purchasing.importDetails(code).subscribe({
      next: r => {
        this.isImportingDetails.set(false);
        this.snackBar.open(`Dati MTGJSON scaricati: ${r.cards} carte, ${r.boosterTypes} tipi di busta, ${r.decks} mazzi`, 'Chiudi', { duration: 6000 });
        this.loadAnalysis();
      },
      error: err => {
        this.isImportingDetails.set(false);
        this.snackBar.open(`Errore dati delle buste: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 });
      }
    });
  }

  ngOnInit() {
    // Dalla campanella degli avvisi: ?set=TRK apre l'analisi di quell'uscita, ?tab=avvisi la scheda Avvisi.
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const set = params.get('set');
      if (set) {
        // ?set= arriva dalla campanella degli avvisi.
        this.refreshCtAfterLoad = true;
        this.selectSet(set.toUpperCase());
        this.tabIndex = 0;
      }
      if (params.get('tab') === 'avvisi') this.tabIndex = 4;
    });
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
    this.openPack.set(null);
    this.loadAnalysis();
  }

  loadAnalysis() {
    const code = this.selectedCode();
    if (!code) return;
    this.isLoading.set(true);
    this.purchasing.getAnalysis(code, this.params).subscribe({
      next: analysis => {
        this.analysis.set(analysis);
        this.syncParams(analysis);
        this.isLoading.set(false);
        if (this.refreshCtAfterLoad) {
          this.refreshCtAfterLoad = false;
          if (this.ctPricesStale(analysis) && !this.isRefreshingCt()) this.refreshCardTraderPrices();
        }
      },
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
        this.snackBar.open(`Prezzi Card Trader aggiornati: ${r.productsWithOffers}/${r.products} sigillati, ${r.cardPrices} prezzi di singole (${r.apiCalls} chiamate)`, 'Chiudi', { duration: 6000 });
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
