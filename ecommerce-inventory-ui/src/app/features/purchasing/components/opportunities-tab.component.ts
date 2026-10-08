import { Component, EventEmitter, OnInit, Output, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AgGridAngular } from 'ag-grid-angular';
import { GridCellCopyDirective } from '../../../shared/directives/grid-cell-copy.directive';
import { CellClassParams, ColDef, ValueFormatterParams } from 'ag-grid-community';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Opportunity, OpportunityList, PurchasePlan, PurchasingService } from '../services/purchasing.service';
import { PurchasePlanPanelComponent } from './purchase-plan-panel.component';
import { GridStateDirective } from '../../../shared/directives/grid-state.directive';

/**
 * Classifica delle opportunità sui sigillati di tutte le uscite (Fase 3): valore atteso dell'apertura
 * contro prezzo d'acquisto, calcolata ogni giorno, con l'andamento di prezzo e valore atteso.
 */
@Component({
  selector: 'app-opportunities-tab',
  standalone: true,
  imports: [GridStateDirective, CommonModule, FormsModule, AgGridAngular, GridCellCopyDirective, MatButtonModule, MatIconModule, MatFormFieldModule,
    MatInputModule, MatSelectModule, MatTooltipModule, MatProgressSpinnerModule, MatSnackBarModule, PurchasePlanPanelComponent],
  template: `
    <div class="tab-container">
      <div class="toolbar">
        <mat-form-field appearance="outline" class="decision">
          <mat-label>Decisione</mat-label>
          <mat-select [ngModel]="decision()" (ngModelChange)="decision.set($event)">
            <mat-option value="Apri">Solo "Apri"</mat-option>
            <mat-option value="">Tutte</mat-option>
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" class="category">
          <mat-label>Tipo di prodotto</mat-label>
          <mat-select [ngModel]="category()" (ngModelChange)="category.set($event)">
            <mat-option value="">Tutti</mat-option>
            <mat-option *ngFor="let c of categories()" [value]="c">{{ categoryLabel(c) }}</mat-option>
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" class="num">
          <mat-label>Resa minima %</mat-label>
          <input matInput type="number" [ngModel]="minRoi()" (ngModelChange)="minRoi.set($event)">
        </mat-form-field>
        <mat-form-field appearance="outline" class="num"
          matTooltip="Nasconde le rese irreali, di solito prodotti vecchi con un prezzo Cardmarket di pochi centesimi che non si trovano davvero in vendita. Vuoto = nessun limite">
          <mat-label>Resa massima %</mat-label>
          <input matInput type="number" [ngModel]="maxRoi()" (ngModelChange)="maxRoi.set($event)">
        </mat-form-field>
        <mat-form-field appearance="outline" class="num">
          <mat-label>Copertura minima %</mat-label>
          <input matInput type="number" [ngModel]="minCoverage()" (ngModelChange)="minCoverage.set($event)">
        </mat-form-field>
        <span class="muted" *ngIf="list() as l">
          {{ visible().length }} prodotti su {{ l.items.length }} ·
          calcolata il {{ (l.computedAt | date:'dd/MM/yyyy HH:mm') || '—' }} su {{ setCount() }} uscite
        </span>
        <span class="spacer"></span>
        <button mat-raised-button color="primary" (click)="buildPlan()" [disabled]="isPlanning() || visible().length === 0"
          [matTooltip]="'Cerca su Card Trader le offerte dei ' + visible().length + ' prodotti filtrati e le raggruppa per venditore (al massimo 40; circa 3 secondi a prodotto)'">
          <mat-spinner *ngIf="isPlanning()" diameter="18"></mat-spinner>
          <mat-icon *ngIf="!isPlanning()">shopping_cart</mat-icon> Piano d'acquisto ({{ visible().length }})
        </button>
        <button mat-stroked-button (click)="compute()" [disabled]="isComputing()"
          matTooltip="Ricalcola adesso la classifica di oggi (gira comunque da sola ogni mattina)">
          <mat-spinner *ngIf="isComputing()" diameter="18"></mat-spinner>
          <mat-icon *ngIf="!isComputing()">calculate</mat-icon> Ricalcola
        </button>
      </div>
      <div class="muted hint">
        Uscite con i dati delle buste già scaricati: il worker ne aggiunge un lotto ogni giorno, partendo dalle più recenti.
        Per mazzi e prodotti a contenuto fisso il valore atteso presuppone di vendere tutte le carte, comprese
        quelle fra 0,25 e 1 € che si vendono lentamente: la resa reale arriva più tardi. Clic su una riga per l'analisi completa dell'uscita.
      </div>
      <app-purchase-plan-panel *ngIf="plan() as p" [plan]="p" (close)="plan.set(null)"></app-purchase-plan-panel>
      <div class="grid-wrapper">
        <div class="loading" *ngIf="isLoading()"><mat-spinner diameter="32"></mat-spinner></div>
        <ag-grid-angular appGridState="purchasing-opportunities-grid" class="ag-theme-material" [rowData]="visible()" [columnDefs]="columnDefs"
          [defaultColDef]="defaultColDef" (rowClicked)="openRelease.emit($event.data?.mainSetCode)"
          style="width: 100%; height: 100%;"></ag-grid-angular>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; height: 100%; }
    .tab-container { display: flex; flex-direction: column; gap: 6px; height: 100%; padding: 12px 0; box-sizing: border-box; }
    .toolbar { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
    .toolbar ::ng-deep .mat-mdc-form-field-subscript-wrapper { display: none; }
    .decision { width: 150px; } .category { width: 200px; } .num { width: 150px; }
    .spacer { flex: 1; }
    .muted { color: #757575; font-size: 12px; }
    .grid-wrapper { flex: 1; min-height: 400px; position: relative; }
    .loading { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; z-index: 2; background: rgba(255,255,255,0.6); }
    :host ::ng-deep .ag-theme-material { --ag-header-background-color: #3f51b5; --ag-header-foreground-color: white; }
    :host ::ng-deep .ag-row { cursor: pointer; }
    :host ::ng-deep .delta-good { color: #2e7d32; font-weight: 600; }
    :host ::ng-deep .delta-bad { color: #c62828; font-weight: 600; }
  `]
})
export class OpportunitiesTabComponent implements OnInit {
  /** Codice dell'uscita da aprire nella scheda di analisi. */
  @Output() openRelease = new EventEmitter<string>();

  list = signal<OpportunityList | null>(null);
  decision = signal<string>('Apri');
  category = signal<string>('');
  minRoi = signal<number | null>(null);
  maxRoi = signal<number | null>(300);
  minCoverage = signal<number | null>(90);
  isLoading = signal(false);
  isComputing = signal(false);
  isPlanning = signal(false);
  plan = signal<PurchasePlan | null>(null);

  categories = computed(() =>
    [...new Set((this.list()?.items ?? []).map(i => i.category).filter((c): c is string => !!c))].sort());

  setCount = computed(() => new Set((this.list()?.items ?? []).map(i => i.mainSetCode)).size);

  visible = computed(() => (this.list()?.items ?? []).filter(i =>
    (!this.decision() || i.decision === this.decision())
    && (!this.category() || i.category === this.category())
    && (this.minRoi() == null || (i.openingRoiPercent ?? -Infinity) >= this.minRoi()!)
    && (this.maxRoi() == null || (i.openingRoiPercent ?? -Infinity) <= this.maxRoi()!)
    && (this.minCoverage() == null || (i.coverageCm ?? 0) >= this.minCoverage()!)));

  private static readonly euroFormat = new Intl.NumberFormat('it-IT', { style: 'currency', currency: 'EUR' });
  private euro = (p: ValueFormatterParams) => p.value == null ? '' : OpportunitiesTabComponent.euroFormat.format(p.value);
  private percent = (p: ValueFormatterParams) => p.value == null ? '' : `${p.value > 0 ? '+' : ''}${p.value.toFixed(1)}%`;
  private signClass = (p: CellClassParams) => p.value == null ? '' : p.value > 0 ? 'delta-good' : p.value < 0 ? 'delta-bad' : '';

  columnDefs: ColDef<Opportunity>[] = [
    { headerName: 'Prodotto', field: 'name', pinned: 'left', width: 380, filter: 'agTextColumnFilter' },
    { headerName: 'Uscita', field: 'setName', width: 200, filter: 'agTextColumnFilter' },
    { headerName: 'Data uscita', field: 'releaseDate', width: 115, valueFormatter: p => p.value ? new Date(p.value).toLocaleDateString('it-IT') : '' },
    { headerName: 'Tipo', field: 'category', width: 130, valueFormatter: p => this.categoryLabel(p.value) },
    { headerName: 'Trend CM', field: 'cmTrend', width: 115, type: 'numericColumn', valueFormatter: this.euro },
    { headerName: 'Low CM', field: 'cmLow', width: 105, type: 'numericColumn', valueFormatter: this.euro },
    { headerName: 'Apri (netto)', field: 'openValueCm', width: 125, type: 'numericColumn', valueFormatter: this.euro,
      headerTooltip: 'Valore atteso netto aprendolo, su prezzi Cardmarket corretti dal prezzo realizzato' },
    { headerName: 'Sigillato netto', field: 'sealedNetCm', width: 130, type: 'numericColumn', valueFormatter: this.euro },
    { headerName: 'Resa apertura', field: 'openingRoiPercent', width: 125, type: 'numericColumn', sort: 'desc',
      valueFormatter: this.percent, cellClass: this.signClass },
    { headerName: 'Copertura', field: 'coverageCm', width: 105, type: 'numericColumn', valueFormatter: p => p.value == null ? '' : `${p.value}%` },
    { headerName: 'Decisione', field: 'decision', width: 140 },
    { headerName: 'Prezzo 7 gg', field: 'trendChange7', width: 115, type: 'numericColumn', valueFormatter: this.percent,
      headerTooltip: 'Variazione del trend del sigillato rispetto a 7 giorni prima (vuoto finché non c\'è lo storico)' },
    { headerName: 'Prezzo 30 gg', field: 'trendChange30', width: 120, type: 'numericColumn', valueFormatter: this.percent },
    { headerName: 'Valore 7 gg', field: 'valueChange7', width: 115, type: 'numericColumn', valueFormatter: this.percent, cellClass: this.signClass,
      headerTooltip: 'Variazione del valore atteso dell\'apertura rispetto a 7 giorni prima' },
    { headerName: 'Valore 30 gg', field: 'valueChange30', width: 120, type: 'numericColumn', valueFormatter: this.percent, cellClass: this.signClass }
  ];

  defaultColDef: ColDef = { sortable: true, resizable: true, filter: true };

  constructor(private purchasing: PurchasingService, private snackBar: MatSnackBar) { }

  ngOnInit() {
    this.load();
  }

  categoryLabel(category?: string | null): string {
    const labels: Record<string, string> = {
      booster_box: 'Box', booster_pack: 'Busta', bundle: 'Bundle', deck: 'Mazzo', box_set: 'Box set',
      limited_aid_tool: 'Draft/Prerelease', subset: 'Set di mazzi', deck_box: 'Deck box'
    };
    return category ? labels[category] ?? category : '';
  }

  load() {
    this.isLoading.set(true);
    this.purchasing.getOpportunities().subscribe({
      next: list => { this.list.set(list); this.isLoading.set(false); },
      error: err => {
        this.isLoading.set(false);
        this.snackBar.open(`Errore nella classifica: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 });
      }
    });
  }

  /** Piano d'acquisto su Card Trader per i prodotti filtrati in questo momento. */
  buildPlan() {
    const ids = this.visible().map(i => i.sealedProductId);
    if (ids.length > 40) {
      this.snackBar.open(`Sono ${ids.length} prodotti: restringi i filtri (al massimo 40, una chiamata a Card Trader ciascuno)`, 'Chiudi', { duration: 6000 });
      return;
    }
    this.isPlanning.set(true);
    this.purchasing.buildPurchasePlan(ids).subscribe({
      next: plan => { this.isPlanning.set(false); this.plan.set(plan); },
      error: err => {
        this.isPlanning.set(false);
        this.snackBar.open(`Errore nel piano d'acquisto: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 });
      }
    });
  }

  compute() {
    this.isComputing.set(true);
    this.purchasing.computeOpportunities().subscribe({
      next: () => { this.isComputing.set(false); this.load(); },
      error: err => {
        this.isComputing.set(false);
        this.snackBar.open(`Errore nel ricalcolo: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 });
      }
    });
  }
}
