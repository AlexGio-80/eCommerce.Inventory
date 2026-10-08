import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AgGridAngular } from 'ag-grid-angular';
import { CellClassParams, ColDef, ValueFormatterParams } from 'ag-grid-community';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { PurchasingService, SecretLairDropRow, SecretLairRetrospective } from '../services/purchasing.service';
import { GridStateDirective } from '../../../shared/directives/grid-state.directive';
import { GridCellCopyDirective } from '../../../shared/directives/grid-cell-copy.directive';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { SecretLairShopComponent } from './secret-lair-shop.component';

/**
 * Retrospettiva dei drop Secret Lair comprati interi e venduti a singole: spesa, incassato, valore
 * ancora in vendita e resa per drop, con il riepilogo per tipo (normale, foil, bundle, Commander).
 */
@Component({
  selector: 'app-secret-lair-tab',
  standalone: true,
  imports: [GridStateDirective, GridCellCopyDirective, CommonModule, FormsModule, AgGridAngular, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatSelectModule, MatProgressSpinnerModule, MatSnackBarModule, MatTooltipModule, MatButtonToggleModule, SecretLairShopComponent],
  template: `
    <div class="tab-container">
      <mat-button-toggle-group [value]="mode()" (change)="mode.set($event.value)" class="mode">
        <mat-button-toggle value="drops">Drop comprati</mat-button-toggle>
        <mat-button-toggle value="shop">Negozio Wizards</mat-button-toggle>
      </mat-button-toggle-group>

      <app-secret-lair-shop *ngIf="mode() === 'shop'" class="shop"></app-secret-lair-shop>

      <ng-container *ngIf="mode() === 'drops'">
      <div class="toolbar">
        <mat-form-field appearance="outline" class="type">
          <mat-label>Tipo</mat-label>
          <mat-select [ngModel]="type()" (ngModelChange)="type.set($event)">
            <mat-option value="">Tutti</mat-option>
            <mat-option *ngFor="let t of types" [value]="t">{{ t }}</mat-option>
          </mat-select>
        </mat-form-field>
        <span class="muted" *ngIf="data() as d">
          Drop comprati interi: quelli con tutte le carte caricate (almeno 3), o registrati nel registro acquisti.
          Prezzo dal registro, altrimenti standard ({{ standardPricesText(d) }}). Incassato e in vendita al netto della
          commissione Card Trader ({{ d.cardTraderFeePercent }}%).
          Singole sciolte (drop non comprati interi): {{ d.looseSoldCopies }} copie vendute, {{ euro(d.looseGrossRevenue) }}.
        </span>
        <span class="spacer"></span>
        <button mat-button (click)="load()"><mat-icon>refresh</mat-icon> Aggiorna</button>
      </div>

      <div class="summary" *ngIf="data() as d">
        <div class="summary-card" *ngFor="let s of d.summary" [class.selected]="type() === s.type" (click)="type.set(type() === s.type ? '' : s.type)">
          <div class="summary-title">{{ s.type }}</div>
          <div>{{ s.drops }} drop · {{ s.copies }} copie · spesa {{ euro(s.cost) }}</div>
          <div>incassato {{ euro(s.netRevenue) }} · resa
            <strong [class.good]="s.profitWithStock >= 0" [class.bad]="s.profitWithStock < 0">
              {{ euro(s.profitWithStock) }} ({{ percent(s.returnPercent) }})</strong></div>
        </div>
      </div>

      <div class="grid-wrapper">
        <div class="loading" *ngIf="isLoading()"><mat-spinner diameter="32"></mat-spinner></div>
        <ag-grid-angular appGridState="purchasing-secret-lair-grid" class="ag-theme-material" [rowData]="visible()" [columnDefs]="columnDefs"
          [defaultColDef]="defaultColDef" style="width: 100%; height: 100%;"></ag-grid-angular>
      </div>
      </ng-container>
    </div>
  `,
  styles: [`
    .tab-container { display: flex; flex-direction: column; gap: 8px; height: 100%; padding: 12px 0; box-sizing: border-box; }
    .toolbar { display: flex; align-items: center; gap: 16px; flex-wrap: wrap; }
    .type { width: 160px; }
    .mode { align-self: flex-start; }
    .shop { flex: 1; min-height: 0; }
    .spacer { flex: 1; }
    .muted { color: #757575; font-size: 12px; max-width: 900px; }
    .summary { display: flex; gap: 10px; flex-wrap: wrap; }
    .summary-card { border: 1px solid #c5cae9; border-radius: 6px; padding: 6px 12px; cursor: pointer; font-size: 13px; }
    .summary-card.selected { background: #e8eaf6; border-color: #3f51b5; }
    .summary-title { font-weight: 600; }
    .good { color: #2e7d32; } .bad { color: #c62828; }
    .grid-wrapper { flex: 1; min-height: 400px; position: relative; }
    .loading { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; z-index: 2; background: rgba(255,255,255,0.6); }
    :host { display: block; height: 100%; }
    :host ::ng-deep .ag-theme-material { --ag-header-background-color: #3f51b5; --ag-header-foreground-color: white; }
    :host ::ng-deep .delta-good { color: #2e7d32; font-weight: 600; }
    :host ::ng-deep .delta-bad { color: #c62828; font-weight: 600; }
    :host ::ng-deep .estimated { color: #757575; font-style: italic; }
    :host ::ng-deep .sl-link { color: #3f51b5; text-decoration: none; font-size: 11px; font-weight: 600; }
  `]
})
export class SecretLairTabComponent implements OnInit {
  readonly types = ['Normale', 'Foil', 'Bundle', 'Commander'];

  data = signal<SecretLairRetrospective | null>(null);
  type = signal('');
  mode = signal<'drops' | 'shop'>('drops');
  isLoading = signal(false);

  visible = computed(() => (this.data()?.drops ?? []).filter(d => !this.type() || d.type === this.type()));

  private static readonly euroFormat = new Intl.NumberFormat('it-IT', { style: 'currency', currency: 'EUR' });

  euro(value?: number | null): string {
    return value == null ? '' : SecretLairTabComponent.euroFormat.format(value);
  }

  percent(value?: number | null): string {
    return value == null ? '' : `${value > 0 ? '+' : ''}${value.toFixed(1)}%`;
  }

  standardPricesText(d: SecretLairRetrospective): string {
    return Object.entries(d.standardPrices).map(([type, price]) => `${type.toLowerCase()} ${this.euro(price)}`).join(', ');
  }

  private euroCell = (p: ValueFormatterParams) => this.euro(p.value);
  private signClass = (p: CellClassParams) => p.value == null ? '' : p.value >= 0 ? 'delta-good' : 'delta-bad';

  columnDefs: ColDef<SecretLairDropRow>[] = [
    { headerName: 'Drop', field: 'name', pinned: 'left', width: 320, filter: 'agTextColumnFilter',
      valueFormatter: p => (p.value ?? '').replace(/^Secret Lair Drop /, '') },
    { headerName: 'Tipo', field: 'type', width: 105 },
    { headerName: 'Dal', field: 'firstSeen', width: 105, sort: 'desc',
      valueFormatter: p => p.value ? new Date(p.value).toLocaleDateString('it-IT') : '',
      headerTooltip: 'Prima carta del drop caricata o venduta' },
    { headerName: 'Carte', field: 'distinctCards', width: 85, type: 'numericColumn' },
    { headerName: 'Copie', field: 'copies', width: 85, type: 'numericColumn',
      tooltipValueGetter: p => p.data?.registeredPurchase ? 'Dal registro acquisti' : 'Stimate dalle carte caricate' },
    { headerName: 'Prezzo', field: 'unitPrice', width: 100, type: 'numericColumn', valueFormatter: this.euroCell,
      cellClass: (p: CellClassParams) => p.data?.registeredPurchase ? '' : 'estimated',
      tooltipValueGetter: p => p.data?.registeredPurchase ? 'Dal registro acquisti' : 'Prezzo standard per tipo' },
    { headerName: 'Spesa', field: 'cost', width: 100, type: 'numericColumn', valueFormatter: this.euroCell },
    { headerName: 'Vendute', field: 'soldCopies', width: 95, type: 'numericColumn' },
    { headerName: 'Incassato netto', field: 'netRevenue', width: 130, type: 'numericColumn', valueFormatter: this.euroCell },
    { headerName: 'Rientrato', field: 'recoveredPercent', width: 105, type: 'numericColumn',
      valueFormatter: p => p.value == null ? '' : `${p.value.toFixed(0)}%`,
      headerTooltip: 'Quota della spesa già rientrata con le vendite' },
    { headerName: 'In vendita', field: 'stockCopies', width: 100, type: 'numericColumn' },
    { headerName: 'Valore listino', field: 'stockListingValue', width: 125, type: 'numericColumn', valueFormatter: this.euroCell },
    { headerName: 'Resa', field: 'profitWithStock', width: 110, type: 'numericColumn', valueFormatter: this.euroCell,
      cellClass: this.signClass,
      headerTooltip: 'Incassato netto + copie in vendita al listino netto − spesa' },
    { headerName: 'Resa %', field: 'returnPercent', width: 100, type: 'numericColumn',
      valueFormatter: p => this.percent(p.value), cellClass: this.signClass },
    { headerName: 'Utile a oggi', field: 'profitSoFar', width: 120, type: 'numericColumn', valueFormatter: this.euroCell,
      cellClass: this.signClass, headerTooltip: 'Solo incassato netto − spesa, senza le copie in vendita' },
    { headerName: 'Valore CM / copia', field: 'cardmarketValuePerCopy', width: 140, type: 'numericColumn', valueFormatter: this.euroCell,
      headerTooltip: 'Una copia del drop aperta, ai trend Cardmarket di oggi delle sue carte' },
    {
      headerName: 'Wizards', width: 90, sortable: false, filter: false,
      headerTooltip: 'Pagina del drop nel negozio Wizards: solo per i drop visti dal monitoraggio',
      cellRenderer: (p: { data?: SecretLairDropRow }) => p.data?.wizardsUrl
        ? `<a class="sl-link" target="_blank" rel="noopener" title="Apri nel negozio Secret Lair" href="${p.data.wizardsUrl}">Negozio</a>`
        : ''
    },
    {
      headerName: 'CT', width: 70, sortable: false, filter: false,
      cellRenderer: (p: { data?: SecretLairDropRow }) => p.data?.cardTraderBlueprintId
        ? `<a class="sl-link" target="_blank" rel="noopener" title="Vedi su Card Trader"
             href="https://www.cardtrader.com/cards/${p.data.cardTraderBlueprintId}">CT</a>`
        : ''
    },
    {
      headerName: 'CM', width: 70, sortable: false, filter: false,
      cellRenderer: (p: { data?: SecretLairDropRow }) => p.data
        ? `<a class="sl-link" target="_blank" rel="noopener" title="Cerca su Cardmarket"
             href="https://www.cardmarket.com/en/Magic/Products/Search?searchString=${encodeURIComponent(p.data.cardmarketName ?? p.data.name)}">CM</a>`
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
    this.purchasing.getSecretLairDrops().subscribe({
      next: data => { this.data.set(data); this.isLoading.set(false); },
      error: err => {
        this.isLoading.set(false);
        this.snackBar.open(`Errore nella retrospettiva Secret Lair: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 });
      }
    });
  }
}
