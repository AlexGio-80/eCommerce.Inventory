import { Component, Input, OnChanges, OnInit, SimpleChanges, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AgGridAngular } from 'ag-grid-angular';
import { GridCellCopyDirective } from '../../../shared/directives/grid-cell-copy.directive';
import { ColDef, ValueFormatterParams } from 'ag-grid-community';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import {
  ProductPurchase, ProductPurchaseInput, PurchasingService, SealedProductAnalysis
} from '../services/purchasing.service';
import { GridStateDirective } from '../../../shared/directives/grid-state.directive';

interface PurchaseForm {
  id?: number;
  sealedProductId: number | null;
  quantity: number;
  unitPrice: number | null;
  store: string;
  seller: string;
  purchasedAt: string;
  openedAt: string;
  tag: string;
  notes: string;
  costPerCard: number | null;
}

/**
 * Registro acquisti (Fase 5). Alla registrazione e all'apertura il server salva la previsione del
 * modello: è il termine di paragone per tarare il modello sulle vendite reali.
 */
@Component({
  selector: 'app-purchases-tab',
  standalone: true,
  imports: [GridStateDirective, CommonModule, FormsModule, AgGridAngular, GridCellCopyDirective, MatButtonModule, MatIconModule, MatFormFieldModule,
    MatInputModule, MatSelectModule, MatTooltipModule, MatSnackBarModule],
  template: `
    <div class="tab-container">
      <div class="form">
        <div class="form-title">{{ form.id ? 'Modifica acquisto' : 'Nuovo acquisto' }}
          <span class="muted" *ngIf="setName"> · prodotti di {{ setName }} (cambia uscita nella scheda "Analisi uscita")</span>
        </div>
        <div class="fields">
          <mat-form-field appearance="outline" class="product">
            <mat-label>Prodotto</mat-label>
            <mat-select [(ngModel)]="form.sealedProductId">
              <mat-option *ngFor="let p of products" [value]="p.id">{{ p.name }}</mat-option>
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline" class="small">
            <mat-label>Quantità</mat-label>
            <input matInput type="number" min="1" [(ngModel)]="form.quantity">
          </mat-form-field>
          <mat-form-field appearance="outline" class="small">
            <mat-label>Prezzo unitario €</mat-label>
            <input matInput type="number" step="0.01" [(ngModel)]="form.unitPrice">
          </mat-form-field>
          <mat-form-field appearance="outline" class="medium">
            <mat-label>Negozio</mat-label>
            <input matInput [(ngModel)]="form.store">
          </mat-form-field>
          <mat-form-field appearance="outline" class="medium">
            <mat-label>Venditore</mat-label>
            <input matInput [(ngModel)]="form.seller">
          </mat-form-field>
          <mat-form-field appearance="outline" class="date">
            <mat-label>Acquistato il</mat-label>
            <input matInput type="date" [(ngModel)]="form.purchasedAt">
          </mat-form-field>
          <mat-form-field appearance="outline" class="date">
            <mat-label>Aperto il</mat-label>
            <input matInput type="date" [(ngModel)]="form.openedAt">
          </mat-form-field>
          <mat-form-field appearance="outline" class="medium">
            <mat-label>Tag</mat-label>
            <input matInput [(ngModel)]="form.tag" placeholder="#TRK_PB_20261115">
            <button mat-icon-button matSuffix (click)="suggestTag()" matTooltip="Proponi il tag CODICE_TIPO_AAAAMMGG">
              <mat-icon>auto_fix_high</mat-icon>
            </button>
          </mat-form-field>
          <mat-form-field appearance="outline" class="small" [matTooltip]="costPerCardHint()">
            <mat-label>Costo per carta €</mat-label>
            <input matInput type="number" step="0.01" min="0" [(ngModel)]="form.costPerCard"
              [placeholder]="editingCalculated != null ? editingCalculated.toFixed(2) : 'calcolato'">
          </mat-form-field>
          <mat-form-field appearance="outline" class="notes">
            <mat-label>Note</mat-label>
            <input matInput [(ngModel)]="form.notes">
          </mat-form-field>
        </div>
        <div class="actions">
          <button mat-raised-button color="primary" (click)="save()" [disabled]="isSaving()">
            {{ form.id ? 'Salva modifiche' : 'Registra acquisto' }}
          </button>
          <button mat-button (click)="reset()">Annulla</button>
          <span class="muted">La previsione del modello si salva alla registrazione e di nuovo quando segni l'apertura.
            Se mancano dati (es. buste non ancora pubblicate) resta vuota e si calcola all'apertura.</span>
        </div>
      </div>
      <div class="grid-wrapper">
        <ag-grid-angular appGridState="purchasing-purchases-grid" class="ag-theme-material" [rowData]="purchases()" [columnDefs]="columnDefs"
          [defaultColDef]="defaultColDef" style="width: 100%; height: 100%;"></ag-grid-angular>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; height: 100%; }
    .tab-container { display: flex; flex-direction: column; gap: 8px; height: 100%; padding: 12px 0; box-sizing: border-box; }
    .form-title { font-weight: 600; margin-bottom: 6px; }
    .fields { display: flex; gap: 8px; flex-wrap: wrap; }
    .fields ::ng-deep .mat-mdc-form-field-subscript-wrapper { display: none; }
    .product { width: 380px; } .small { width: 120px; } .medium { width: 170px; } .date { width: 160px; } .notes { width: 300px; }
    .actions { display: flex; gap: 12px; align-items: center; margin-top: 8px; }
    .muted { color: #757575; font-size: 12px; font-weight: normal; }
    .grid-wrapper { flex: 1; min-height: 300px; }
    :host ::ng-deep .ag-theme-material { --ag-header-background-color: #3f51b5; --ag-header-foreground-color: white; }
    :host ::ng-deep .row-action { background: none; border: none; cursor: pointer; color: #3f51b5; padding: 0 4px; }
  `]
})
export class PurchasesTabComponent implements OnInit, OnChanges {
  /** Prodotti dell'uscita scelta nella scheda di analisi. */
  @Input() products: SealedProductAnalysis[] = [];
  @Input() setCode: string | null = null;
  @Input() setName: string | null = null;
  /** Prodotto da precompilare quando si arriva da "Registra" nella tabella di analisi. */
  @Input() prefillProductId: number | null = null;

  purchases = signal<ProductPurchase[]>([]);
  isSaving = signal(false);
  form: PurchaseForm = this.emptyForm();

  private static readonly euroFormat = new Intl.NumberFormat('it-IT', { style: 'currency', currency: 'EUR' });
  private euro = (p: ValueFormatterParams) => p.value == null ? '' : PurchasesTabComponent.euroFormat.format(p.value);
  private date = (p: ValueFormatterParams) => p.value ? new Date(p.value).toLocaleDateString('it-IT') : '';

  columnDefs: ColDef<ProductPurchase>[] = [
    { headerName: 'Prodotto', field: 'productName', pinned: 'left', width: 320 },
    { headerName: 'Set', field: 'setCode', width: 80 },
    { headerName: 'Q.tà', field: 'quantity', width: 80, type: 'numericColumn' },
    { headerName: 'Prezzo', field: 'unitPrice', width: 105, type: 'numericColumn', valueFormatter: this.euro },
    { headerName: 'Totale', field: 'totalPrice', width: 110, type: 'numericColumn', valueFormatter: this.euro },
    { headerName: 'Negozio', field: 'store', width: 120 },
    { headerName: 'Venditore', field: 'seller', width: 130 },
    { headerName: 'Acquistato', field: 'purchasedAt', width: 115, valueFormatter: this.date },
    { headerName: 'Aperto', field: 'openedAt', width: 105, valueFormatter: this.date },
    { headerName: 'Tag', field: 'tag', width: 170 },
    {
      headerName: 'Costo/carta', field: 'effectiveCostPerCard', width: 115, type: 'numericColumn',
      // Senza simbolo e con il punto: con il doppio clic si copia così com'è nel campo numerico
      // "Prezzo di acquisto" del caricamento prodotti.
      valueFormatter: p => p.value == null ? '' : Number(p.value).toFixed(2),
      cellStyle: p => p.data && p.data.costPerCard == null ? { fontStyle: 'italic', color: '#616161' } : null,
      headerTooltip: 'Costo di una carta, da mettere come prezzo di acquisto nelle inserzioni. Doppio clic per copiarlo',
      tooltipValueGetter: p => !p.data ? '' : p.data.costPerCard != null
        ? `Scritto a mano${p.data.calculatedCostPerCard != null ? ` (calcolato: ${p.data.calculatedCostPerCard.toFixed(2)})` : ''}`
        : p.data.calculatedCostPerCard != null
          ? `Calcolato: prezzo unitario / ${p.data.cardsPerUnit} carte${p.data.cardsEstimated ? ' (carte per busta tipiche: MTGJSON non ha ancora la composizione)' : ''}`
          : "Contenuto del prodotto non noto: scrivilo a mano modificando l'acquisto"
    },
    {
      headerName: 'Previsto netto/unità', field: 'predictedOpenValueNet', width: 160, type: 'numericColumn', valueFormatter: this.euro,
      tooltipValueGetter: p => p.data?.predictedAt
        ? `Calcolato il ${new Date(p.data.predictedAt).toLocaleString('it-IT')}, copertura prezzi ${p.data.predictionCoverage}%`
        : 'Non ancora calcolabile: si calcola quando segni l\'apertura'
    },
    { headerName: 'Previsto totale', field: 'predictedTotalNet', width: 135, type: 'numericColumn', valueFormatter: this.euro },
    { headerName: 'Note', field: 'notes', width: 200 },
    {
      headerName: '', width: 90, pinned: 'right', sortable: false, filter: false,
      cellRenderer: () => `<button class="row-action edit" title="Modifica"><i class="material-icons" style="font-size:18px">edit</i></button>
                           <button class="row-action delete" title="Elimina"><i class="material-icons" style="font-size:18px">delete</i></button>`,
      onCellClicked: p => {
        const target = p.event?.target as HTMLElement | null;
        if (!p.data || !target) return;
        if (target.closest('.edit')) this.edit(p.data);
        if (target.closest('.delete')) this.remove(p.data);
      }
    }
  ];

  defaultColDef: ColDef = { sortable: true, resizable: true, filter: true };

  constructor(private purchasing: PurchasingService, private snackBar: MatSnackBar) { }

  ngOnInit() {
    this.load();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['prefillProductId'] && this.prefillProductId) {
      this.form = { ...this.emptyForm(), sealedProductId: this.prefillProductId };
    }
  }

  load() {
    this.purchasing.getPurchases().subscribe({
      next: purchases => this.purchases.set(purchases),
      error: err => this.snackBar.open(`Errore nel registro acquisti: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 })
    });
  }

  /** CODICE_TIPO_AAAAMMGG: tipo PB/CB per i box Play/Collector, altrimenti dalla categoria. */
  suggestTag() {
    const product = this.products.find(p => p.id === this.form.sealedProductId);
    if (!product) {
      this.snackBar.open('Scegli prima il prodotto', 'Chiudi', { duration: 3000 });
      return;
    }
    const date = (this.form.openedAt || new Date().toISOString().slice(0, 10)).replace(/-/g, '');
    if (product.subtype?.startsWith('secret_lair')) {
      this.form.tag = `#SLD_${this.secretLairCode(product.name)}_${date}`;
      return;
    }
    const type = product.subtype === 'play' ? 'PB'
      : product.subtype === 'collector' ? 'CB'
      : (product.subtype || product.category || 'X').slice(0, 3).toUpperCase();
    this.form.tag = `#${product.setCode}_${type}_${date}`;
  }

  /**
   * Codice breve di un drop Secret Lair: iniziali delle parole del nome (al massimo 5), più F se
   * foil. "Secret Lair x Marvel Earth's Mightiest Pets Foil Edition" → MEMPF. Un tag per drop tiene
   * separato il bilancio di ciascuno nella scheda Secret Lair.
   */
  private secretLairCode(name: string): string {
    const foil = /\b(foil|etched)\b/i.test(name);
    const words = name
      .replace(/^Secret Lair( Drop)?( x)?\s*/i, '')
      .replace(/\b(Foil|Rainbow|Etched|Edition|Bundle)\b/gi, '')
      .split(/[^A-Za-z0-9]+/)
      .filter(w => w.length > 1 && !/^(the|of|and|an|in|to)$/i.test(w));
    return (words.slice(0, 5).map(w => w[0].toUpperCase()).join('') || 'SL') + (foil ? 'F' : '');
  }

  save() {
    if (!this.form.sealedProductId || !this.form.quantity || this.form.unitPrice == null) {
      this.snackBar.open('Prodotto, quantità e prezzo sono obbligatori', 'Chiudi', { duration: 4000 });
      return;
    }
    const input: ProductPurchaseInput = {
      sealedProductId: this.form.sealedProductId,
      quantity: this.form.quantity,
      unitPrice: this.form.unitPrice,
      store: this.form.store || null,
      seller: this.form.seller || null,
      purchasedAt: this.form.purchasedAt || null,
      openedAt: this.form.openedAt || null,
      tag: this.form.tag || null,
      notes: this.form.notes || null,
      costPerCard: this.form.costPerCard || null
    };
    this.isSaving.set(true);
    this.purchasing.savePurchase(input, this.form.id).subscribe({
      next: saved => {
        this.isSaving.set(false);
        const prediction = saved.predictedOpenValueNet != null
          ? ` Previsione: ${PurchasesTabComponent.euroFormat.format(saved.predictedOpenValueNet)} netti per unità.`
          : ' Previsione non ancora calcolabile.';
        this.snackBar.open(`Acquisto salvato.${prediction}`, 'Chiudi', { duration: 6000 });
        this.reset();
        this.load();
      },
      error: err => {
        this.isSaving.set(false);
        this.snackBar.open(`Errore nel salvataggio: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 });
      }
    });
  }

  edit(purchase: ProductPurchase) {
    this.form = {
      id: purchase.id,
      sealedProductId: purchase.sealedProductId,
      quantity: purchase.quantity,
      unitPrice: purchase.unitPrice,
      store: purchase.store ?? '',
      seller: purchase.seller ?? '',
      purchasedAt: purchase.purchasedAt ?? '',
      openedAt: purchase.openedAt ?? '',
      tag: purchase.tag ?? '',
      notes: purchase.notes ?? '',
      costPerCard: purchase.costPerCard ?? null
    };
    this.editingCalculated = purchase.calculatedCostPerCard ?? null;
    this.editingCards = purchase.cardsPerUnit ?? null;
    if (!this.products.some(p => p.id === purchase.sealedProductId)) {
      // Il prodotto è di un'altra uscita: lo si aggiunge alla tendina per poterlo mostrare.
      this.products = [...this.products, { id: purchase.sealedProductId, name: purchase.productName, setCode: purchase.setCode } as SealedProductAnalysis];
    }
  }

  remove(purchase: ProductPurchase) {
    if (!confirm(`Eliminare l'acquisto "${purchase.productName}" x${purchase.quantity}?`)) return;
    this.purchasing.deletePurchase(purchase.id).subscribe({
      next: () => this.load(),
      error: err => this.snackBar.open(`Errore nell'eliminazione: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 })
    });
  }

  reset() {
    this.form = this.emptyForm();
    this.editingCalculated = null;
    this.editingCards = null;
  }

  /** Costo per carta calcolato dell'acquisto in modifica (prezzo unitario / carte contenute). */
  editingCalculated: number | null = null;
  editingCards: number | null = null;

  costPerCardHint(): string {
    const base = 'Vuoto = calcolato dal prezzo unitario diviso le carte contenute nel prodotto';
    return this.editingCalculated != null
      ? `${base}: ${this.editingCalculated.toFixed(2)} € (${this.editingCards} carte)`
      : base;
  }

  private emptyForm(): PurchaseForm {
    return { sealedProductId: null, quantity: 1, unitPrice: null, store: 'Cardmarket', seller: '', purchasedAt: '', openedAt: '', tag: '', notes: '', costPerCard: null };
  }
}
