import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AgGridAngular } from 'ag-grid-angular';
import { GridCellCopyDirective } from '../../../shared/directives/grid-cell-copy.directive';
import { CellClassParams, ColDef, ValueFormatterParams } from 'ag-grid-community';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { OpeningBalance, PurchasingService } from '../services/purchasing.service';
import { GridStateDirective } from '../../../shared/directives/grid-state.directive';

/**
 * Bilancio reale delle aperture (Fase 5): costo, venduto e ancora in vendita per tag, con l'incasso
 * entro 30/60/90/180 giorni. Le modifiche fatte dalla maschera contano solo per le copie aggiunte.
 */
@Component({
  selector: 'app-openings-tab',
  standalone: true,
  imports: [GridStateDirective, CommonModule, FormsModule, AgGridAngular, GridCellCopyDirective, MatButtonModule, MatIconModule, MatSlideToggleModule,
    MatProgressSpinnerModule, MatSnackBarModule],
  template: `
    <div class="tab-container">
      <div class="toolbar">
        <mat-slide-toggle [ngModel]="onlyAtRelease()" (ngModelChange)="onlyAtRelease.set($event)">
          Solo aperture all'uscita
        </mat-slide-toggle>
        <span class="muted">
          Costo dai prezzi d'acquisto per carta (le modifiche contano solo per le copie aggiunte); incassato al netto
          della commissione Card Trader. L'utile non conta le carte ancora in vendita.
        </span>
        <span class="spacer"></span>
        <button mat-button (click)="load()"><mat-icon>refresh</mat-icon> Aggiorna</button>
      </div>
      <div class="grid-wrapper">
        <div class="loading" *ngIf="isLoading()"><mat-spinner diameter="32"></mat-spinner></div>
        <ag-grid-angular appGridState="purchasing-openings-grid" class="ag-theme-material" [rowData]="visible()" [columnDefs]="columnDefs"
          [defaultColDef]="defaultColDef" style="width: 100%; height: 100%;"></ag-grid-angular>
      </div>
    </div>
  `,
  styles: [`
    .tab-container { display: flex; flex-direction: column; gap: 8px; height: 100%; padding: 12px 0; box-sizing: border-box; }
    .toolbar { display: flex; align-items: center; gap: 16px; flex-wrap: wrap; }
    .spacer { flex: 1; }
    .muted { color: #757575; font-size: 12px; max-width: 760px; }
    .grid-wrapper { flex: 1; min-height: 400px; position: relative; }
    .loading { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; z-index: 2; background: rgba(255,255,255,0.6); }
    :host { display: block; height: 100%; }
    :host ::ng-deep .ag-theme-material { --ag-header-background-color: #3f51b5; --ag-header-foreground-color: white; }
    :host ::ng-deep .delta-good { color: #2e7d32; font-weight: 600; }
    :host ::ng-deep .delta-bad { color: #c62828; font-weight: 600; }
  `]
})
export class OpeningsTabComponent implements OnInit {
  openings = signal<OpeningBalance[]>([]);
  onlyAtRelease = signal(true);
  isLoading = signal(false);

  visible = computed(() => this.openings().filter(o => !this.onlyAtRelease() || o.openedAtRelease));

  private static readonly euroFormat = new Intl.NumberFormat('it-IT', { style: 'currency', currency: 'EUR' });
  private euro = (p: ValueFormatterParams) => p.value == null ? '' : OpeningsTabComponent.euroFormat.format(p.value);

  private curve(days: number): ColDef<OpeningBalance> {
    return {
      headerName: `Entro ${days} gg`, width: 115, type: 'numericColumn', valueFormatter: this.euro,
      valueGetter: p => p.data?.revenueCurve.find(c => c.days === days)?.netRevenue ?? null,
      headerTooltip: `Incassato netto entro ${days} giorni dal primo caricamento (vuoto se l'apertura è più recente)`
    };
  }

  columnDefs: ColDef<OpeningBalance>[] = [
    { headerName: 'Tag', field: 'tag', pinned: 'left', width: 160, filter: 'agTextColumnFilter' },
    { headerName: 'Espansione', field: 'expansion', width: 220, filter: 'agTextColumnFilter' },
    { headerName: 'Aperta il', field: 'firstUpload', width: 110, valueFormatter: p => p.value ? new Date(p.value).toLocaleDateString('it-IT') : '' },
    { headerName: 'Giorni', field: 'ageDays', width: 90, type: 'numericColumn' },
    { headerName: 'Costo', field: 'cost', width: 110, type: 'numericColumn', valueFormatter: this.euro },
    { headerName: 'Copie', field: 'copies', width: 95, type: 'numericColumn' },
    { headerName: 'Vendute', field: 'soldCopies', width: 100, type: 'numericColumn' },
    { headerName: 'Incassato netto', field: 'netRevenue', width: 135, type: 'numericColumn', valueFormatter: this.euro },
    {
      headerName: 'Utile a oggi', field: 'profitSoFar', width: 125, type: 'numericColumn', valueFormatter: this.euro,
      cellClass: (p: CellClassParams) => p.value == null ? '' : p.value >= 0 ? 'delta-good' : 'delta-bad'
    },
    {
      headerName: 'Utile %', field: 'profitSoFarPercent', width: 100, type: 'numericColumn',
      valueFormatter: p => p.value == null ? '' : `${p.value > 0 ? '+' : ''}${p.value.toFixed(1)}%`,
      cellClass: (p: CellClassParams) => p.value == null ? '' : p.value >= 0 ? 'delta-good' : 'delta-bad'
    },
    this.curve(30), this.curve(60), this.curve(90), this.curve(180),
    { headerName: 'In vendita', field: 'stockCopies', width: 105, type: 'numericColumn',
      tooltipValueGetter: p => p.data ? `di cui ${p.data.stockBulkCopies} bulk` : '' },
    { headerName: 'Valore listino', field: 'stockListingValue', width: 125, type: 'numericColumn', valueFormatter: this.euro,
      headerTooltip: 'Copie ancora in vendita ai prezzi di listino: il bulk si vende solo in parte' },
    { headerName: 'Costo registro', field: 'registeredCost', width: 125, type: 'numericColumn', valueFormatter: this.euro,
      headerTooltip: 'Costo dal registro acquisti, se l\'apertura vi è registrata con questo tag' },
    { headerName: 'Previsto', field: 'predictedNet', width: 115, type: 'numericColumn', valueFormatter: this.euro,
      headerTooltip: 'Valore atteso netto previsto dal modello all\'apertura, dal registro acquisti' }
  ];

  defaultColDef: ColDef = { sortable: true, resizable: true, filter: true };

  constructor(private purchasing: PurchasingService, private snackBar: MatSnackBar) { }

  ngOnInit() {
    this.load();
  }

  load() {
    this.isLoading.set(true);
    this.purchasing.getOpenings().subscribe({
      next: openings => { this.openings.set(openings); this.isLoading.set(false); },
      error: err => {
        this.isLoading.set(false);
        this.snackBar.open(`Errore nel bilancio delle aperture: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 });
      }
    });
  }
}
