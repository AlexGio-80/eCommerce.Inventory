import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AgGridAngular } from 'ag-grid-angular';
import { CellClassParams, ColDef, ValueFormatterParams } from 'ag-grid-community';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { GridCellCopyDirective } from '../../../shared/directives/grid-cell-copy.directive';
import { GridStateDirective } from '../../../shared/directives/grid-state.directive';
import { HoldStock, HoldStockItem, PurchasingService } from '../services/purchasing.service';

/**
 * Magazzino da tenere (feature 005, Fase 3): gli acquisti segnati "da tenere" nel registro e ancora
 * sigillati, con il valore di oggi chiuso e aperto e il guadagno latente.
 */
@Component({
  selector: 'app-hold-stock-tab',
  standalone: true,
  imports: [GridStateDirective, CommonModule, AgGridAngular, GridCellCopyDirective, MatButtonModule, MatIconModule, MatTooltipModule,
    MatProgressSpinnerModule, MatSnackBarModule],
  template: `
    <div class="tab-container">
      <div class="toolbar">
        <div class="kpi"><span class="label">Costo</span><span class="value">{{ euro(stock()?.totalCost) }}</span></div>
        <div class="kpi"><span class="label">Valore di oggi (netto)</span><span class="value">{{ euro(stock()?.totalValue) }}</span></div>
        <div class="kpi">
          <span class="label">Guadagno latente</span>
          <span class="value" [class.good]="(stock()?.totalGain ?? 0) > 0" [class.bad]="(stock()?.totalGain ?? 0) < 0">
            {{ euro(stock()?.totalGain) }} <small *ngIf="gainPercent() as g">({{ g }})</small>
          </span>
        </div>
        <span class="muted" *ngIf="stock()?.date">valori della classifica del {{ stock()!.date | date:'dd/MM/yyyy' }}</span>
        <span class="spacer"></span>
        <button mat-stroked-button (click)="load()" [disabled]="isLoading()">
          <mat-spinner *ngIf="isLoading()" diameter="18"></mat-spinner>
          <mat-icon *ngIf="!isLoading()">refresh</mat-icon> Aggiorna
        </button>
      </div>
      <div class="muted hint">
        Acquisti segnati "Da tenere" nel Registro acquisti e non ancora aperti. Il valore è il migliore fra rivenderli chiusi
        al trend Cardmarket e aprirli (valore atteso della classifica), entrambi al netto dei costi di vendita.
        Quando apri un prodotto, segna la data di apertura nel registro: esce da qui.
      </div>
      <div class="empty muted" *ngIf="stock() && stock()!.items.length === 0">
        Nessun acquisto da tenere: segnalo con la casella "Da tenere" nel Registro acquisti.
      </div>
      <div class="grid-wrapper">
        <ag-grid-angular appGridState="purchasing-hold-stock-grid" class="ag-theme-material" [rowData]="stock()?.items ?? []"
          [columnDefs]="columnDefs" [defaultColDef]="defaultColDef" style="width: 100%; height: 100%;"></ag-grid-angular>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; height: 100%; }
    .tab-container { display: flex; flex-direction: column; gap: 8px; height: 100%; padding: 12px 0; box-sizing: border-box; }
    .toolbar { display: flex; align-items: center; gap: 24px; flex-wrap: wrap; }
    .kpi { display: flex; flex-direction: column; }
    .kpi .label { font-size: 12px; color: #757575; }
    .kpi .value { font-size: 20px; font-weight: 600; }
    .kpi .value.good { color: #2e7d32; } .kpi .value.bad { color: #c62828; }
    .spacer { flex: 1; }
    .muted { color: #757575; font-size: 12px; }
    .empty { padding: 12px 0; }
    .grid-wrapper { flex: 1; min-height: 300px; }
    :host ::ng-deep .ag-theme-material { --ag-header-background-color: #3f51b5; --ag-header-foreground-color: white; }
    :host ::ng-deep .delta-good { color: #2e7d32; font-weight: 600; }
    :host ::ng-deep .delta-bad { color: #c62828; font-weight: 600; }
  `]
})
export class HoldStockTabComponent implements OnInit {
  stock = signal<HoldStock | null>(null);
  isLoading = signal(false);

  private static readonly euroFormat = new Intl.NumberFormat('it-IT', { style: 'currency', currency: 'EUR' });
  private euroCell = (p: ValueFormatterParams) => p.value == null ? '' : HoldStockTabComponent.euroFormat.format(p.value);
  private percentCell = (p: ValueFormatterParams) => p.value == null ? '' : `${p.value > 0 ? '+' : ''}${p.value.toFixed(1)}%`;
  private signClass = (p: CellClassParams) => p.value == null ? '' : p.value > 0 ? 'delta-good' : p.value < 0 ? 'delta-bad' : '';

  columnDefs: ColDef<HoldStockItem>[] = [
    { headerName: 'Prodotto', field: 'name', pinned: 'left', width: 340, filter: 'agTextColumnFilter' },
    { headerName: 'Set', field: 'setCode', width: 80 },
    { headerName: 'Q.tà', field: 'quantity', width: 80, type: 'numericColumn' },
    { headerName: 'Prezzo pagato', field: 'unitPrice', width: 125, type: 'numericColumn', valueFormatter: this.euroCell },
    { headerName: 'Costo', field: 'cost', width: 110, type: 'numericColumn', valueFormatter: this.euroCell },
    { headerName: 'Acquistato', field: 'purchasedAt', width: 115, valueFormatter: p => p.value ? new Date(p.value).toLocaleDateString('it-IT') : '' },
    { headerName: 'Mesi', field: 'monthsHeld', width: 80, type: 'numericColumn', headerTooltip: 'Mesi di possesso' },
    { headerName: 'Trend CM', field: 'cmTrend', width: 110, type: 'numericColumn', valueFormatter: this.euroCell },
    { headerName: 'Trend all\'acquisto', field: 'trendAtPurchase', width: 140, type: 'numericColumn', valueFormatter: this.euroCell,
      headerTooltip: 'Trend Cardmarket alla data d\'acquisto. Vuoto se lo storico (dal 07/10/2026) non ci arriva' },
    { headerName: 'Trend da acquisto', field: 'trendChangeSincePurchase', width: 135, type: 'numericColumn',
      valueFormatter: this.percentCell, cellClass: this.signClass },
    { headerName: 'Chiuso netto/unità', field: 'sealedNetUnit', width: 150, type: 'numericColumn', valueFormatter: this.euroCell,
      headerTooltip: 'Rivendendolo chiuso al trend Cardmarket, al netto dei costi di vendita' },
    { headerName: 'Aperto netto/unità', field: 'openNetUnit', width: 150, type: 'numericColumn', valueFormatter: this.euroCell,
      headerTooltip: 'Aprendolo e vendendo le carte: valore atteso netto della classifica' },
    { headerName: 'Min CT', field: 'ctMinPrice', width: 105, type: 'numericColumn', valueFormatter: this.euroCell,
      headerTooltip: 'Offerta più bassa su Card Trader all\'ultimo aggiornamento dei prezzi CT del prodotto' },
    { headerName: 'Uscita migliore', field: 'bestExit', width: 130, valueFormatter: p => p.value === 'Open' ? 'Aprirlo' : p.value === 'Sealed' ? 'Chiuso' : '' },
    { headerName: 'Valore oggi', field: 'valueTotal', width: 120, type: 'numericColumn', valueFormatter: this.euroCell },
    { headerName: 'Guadagno', field: 'gain', width: 115, type: 'numericColumn', valueFormatter: this.euroCell, cellClass: this.signClass },
    { headerName: 'Guadagno %', field: 'gainPercent', width: 120, type: 'numericColumn', sort: 'desc',
      valueFormatter: this.percentCell, cellClass: this.signClass },
    { headerName: 'Negozio', field: 'store', width: 120 },
    {
      headerName: 'CT', width: 70, sortable: false, filter: false,
      cellRenderer: (p: { data?: HoldStockItem }) => p.data?.cardTraderBlueprintId
        ? `<a href="https://www.cardtrader.com/cards/${p.data.cardTraderBlueprintId}" target="_blank" rel="noopener" title="Vedi su Card Trader">CT</a>`
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
    this.purchasing.getHoldStock().subscribe({
      next: stock => { this.stock.set(stock); this.isLoading.set(false); },
      error: err => {
        this.isLoading.set(false);
        this.snackBar.open(`Errore nel magazzino: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 });
      }
    });
  }

  euro(value?: number | null): string {
    return value == null ? '—' : HoldStockTabComponent.euroFormat.format(value);
  }

  /** Guadagno latente sul costo dei soli acquisti con un prezzo di oggi. */
  gainPercent(): string | null {
    const s = this.stock();
    if (!s || s.totalValue === 0) return null;
    const valuedCost = s.totalValue - s.totalGain;
    if (valuedCost <= 0) return null;
    const pct = s.totalGain / valuedCost * 100;
    return `${pct > 0 ? '+' : ''}${pct.toFixed(1)}%`;
  }
}
