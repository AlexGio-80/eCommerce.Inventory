import { Component, Inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { CatalogProduct, ProductPurchaseInput, PurchasingService } from '../services/purchasing.service';
import { suggestPurchaseTag } from '../purchase-tag';

/** Riga di un ordine proposto, da registrare nel registro acquisti. */
export interface RegisterOrderLine {
  sealedProductId: number;
  name: string;
  quantity: number;
  unitPrice: number;
  store: string;
  seller?: string;
  /** Prodotto da tenere: "Da tenere" nel registro e niente Tag finché non si apre. */
  hold: boolean;
}

export interface RegisterOrderData {
  /** Es. "Card Trader Zero" o "Card Trader · Joko". */
  title: string;
  /** Spedizione dell'ordine intero: si ripartisce sulle righe spuntate in proporzione al prezzo. */
  shipping: number;
  lines: RegisterOrderLine[];
  /** Righe spuntate all'apertura (indici); assente = tutte. */
  checked?: number[];
}

interface Row extends RegisterOrderLine {
  checked: boolean;
  tag: string;
  /** Tag scritto a mano: non si ricalcola più al cambio di data. */
  tagEdited: boolean;
  product?: CatalogProduct;
  /** Data in cui lo stesso prodotto è già nel registro, se è la stessa dell'acquisto. */
  alreadyOn?: string;
}

/** Apre la registrazione di un ordine; si chiude con il numero di acquisti registrati, o senza valore se annullata. */
export function openRegisterPurchase(dialog: MatDialog, data: RegisterOrderData) {
  return dialog.open(RegisterPurchaseDialogComponent, { data, width: '1200px', maxWidth: '95vw', autoFocus: false });
}

/**
 * Registra nel registro acquisti un ordine proposto (Budget, piano d'acquisto), tutto o in parte: prezzo,
 * negozio, venditore, data e Tag già compilati, spedizione ripartita sui prezzi.
 */
@Component({
  selector: 'app-register-purchase-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule,
    MatTooltipModule, MatSnackBarModule],
  template: `
    <h2 mat-dialog-title>Registra l'acquisto · {{ data.title }}</h2>
    <mat-dialog-content>
      <div class="head">
        <mat-form-field appearance="outline" class="date">
          <mat-label>Acquistato il</mat-label>
          <input matInput type="date" [(ngModel)]="date" (ngModelChange)="onDateChange()">
        </mat-form-field>
        <mat-form-field appearance="outline" class="money">
          <mat-label>Spedizione €</mat-label>
          <input matInput type="number" min="0" step="0.5" [(ngModel)]="shipping">
        </mat-form-field>
        <span class="muted">La spedizione si ripartisce sulle righe spuntate in proporzione al prezzo ed entra nel prezzo registrato,
          quindi nel costo per carta. Tag con la data d'acquisto; i prodotti da tenere non lo hanno finché non li apri.</span>
      </div>
      <table>
        <thead><tr><th><mat-checkbox [checked]="allChecked()" [indeterminate]="someChecked()" (change)="checkAll($event.checked)"></mat-checkbox></th>
          <th>Prodotto</th><th>Negozio · venditore</th><th class="num">Q.tà</th><th class="num">Prezzo €</th>
          <th class="num">+ sped./unità</th><th class="num">Registrato</th><th>Tag</th><th>Da tenere</th></tr></thead>
        <tbody>
          @for (r of rows(); track $index) {
            <tr [class.off]="!r.checked">
              <td><mat-checkbox [(ngModel)]="r.checked"></mat-checkbox></td>
              <td>{{ r.product?.cardmarketName || r.name }}
                @if (r.alreadyOn) { <div class="warn">già nel registro con la stessa data: controlla di non registrarlo due volte</div> }</td>
              <td>{{ r.store }}@if (r.seller) { · {{ r.seller }} }</td>
              <td class="num"><input class="cell small" type="number" min="1" [(ngModel)]="r.quantity"></td>
              <td class="num"><input class="cell" type="number" min="0" step="0.01" [(ngModel)]="r.unitPrice"></td>
              <td class="num muted">{{ r.checked ? eur(shippingPerUnit(r)) : '' }}</td>
              <td class="num"><b>{{ r.checked ? eur(registeredPrice(r)) : '' }}</b></td>
              <td><input class="cell tag" [(ngModel)]="r.tag" (ngModelChange)="r.tagEdited = true" [placeholder]="r.hold ? 'all\\'apertura' : ''"></td>
              <td><mat-checkbox [(ngModel)]="r.hold" (ngModelChange)="onHoldChange(r)"></mat-checkbox></td>
            </tr>
          }
        </tbody>
      </table>
      <div class="totals">
        {{ checkedRows().length }} {{ checkedRows().length === 1 ? 'prodotto' : 'prodotti' }} · articoli {{ eur(itemsTotal()) }}
        · spedizione {{ eur(shipping || 0) }} · totale <b>{{ eur(itemsTotal() + (shipping || 0)) }}</b>
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Annulla</button>
      <button mat-raised-button color="primary" [disabled]="saving() || !checkedRows().length" (click)="save()">
        {{ saving() ? 'Registro…' : 'Registra ' + checkedRows().length + (checkedRows().length === 1 ? ' acquisto' : ' acquisti') }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .head { display: flex; gap: 12px; align-items: center; }
    .head ::ng-deep .mat-mdc-form-field-subscript-wrapper { display: none; }
    .date { width: 170px; } .money { width: 130px; }
    .muted { color: #757575; font-size: 12px; }
    table { width: 100%; border-collapse: collapse; margin-top: 12px; font-size: 13px; }
    th { text-align: left; font-weight: 500; color: #616161; border-bottom: 1px solid #e0e0e0; padding: 4px 6px; }
    td { padding: 2px 6px; border-bottom: 1px solid #f0f0f0; }
    .num { text-align: right; }
    tr.off td { color: #9e9e9e; }
    .cell { width: 80px; text-align: right; font: inherit; padding: 3px 4px; border: 1px solid #ccc; border-radius: 3px; }
    .cell.small { width: 50px; }
    .cell.tag { width: 170px; text-align: left; }
    .warn { color: #e65100; font-size: 11px; }
    .totals { margin-top: 10px; text-align: right; }
  `]
})
export class RegisterPurchaseDialogComponent implements OnInit {
  rows = signal<Row[]>([]);
  saving = signal(false);
  date = new Date().toISOString().slice(0, 10);
  shipping: number;
  private existing: { sealedProductId: number; purchasedAt?: string }[] = [];

  private static readonly euroFormat = new Intl.NumberFormat('it-IT', { style: 'currency', currency: 'EUR' });

  constructor(
    @Inject(MAT_DIALOG_DATA) public data: RegisterOrderData,
    private dialogRef: MatDialogRef<RegisterPurchaseDialogComponent, number>,
    private purchasing: PurchasingService,
    private snackBar: MatSnackBar
  ) {
    this.shipping = data.shipping;
    this.rows.set(data.lines.map((l, i) => ({
      ...l, checked: !data.checked || data.checked.includes(i), tag: '', tagEdited: false
    })));
  }

  ngOnInit() {
    const ids = [...new Set(this.data.lines.map(l => l.sealedProductId))];
    forkJoin([this.purchasing.getCatalogProducts(ids), this.purchasing.getPurchases()]).subscribe({
      next: ([products, purchases]) => {
        const byId = new Map(products.map(p => [p.id, p]));
        this.existing = purchases.map(p => ({ sealedProductId: p.sealedProductId, purchasedAt: p.purchasedAt ?? undefined }));
        for (const r of this.rows()) r.product = byId.get(r.sealedProductId);
        this.onDateChange();
        // Un ordine già registrato oggi si riapre senza spunte sui doppioni.
        for (const r of this.rows()) if (r.alreadyOn) r.checked = false;
        this.rows.set([...this.rows()]);
      },
      error: err => this.snackBar.open(`Errore nella lettura del catalogo: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 })
    });
  }

  checkedRows = () => this.rows().filter(r => r.checked);
  allChecked = () => this.rows().every(r => r.checked);
  someChecked = () => !this.allChecked() && this.rows().some(r => r.checked);
  checkAll(checked: boolean) { for (const r of this.rows()) r.checked = checked; }

  itemsTotal(): number {
    return this.checkedRows().reduce((sum, r) => sum + (r.quantity || 0) * (r.unitPrice || 0), 0);
  }

  /** Quota della spedizione per unità, in proporzione al peso della riga sul totale degli articoli spuntati. */
  shippingPerUnit(r: Row): number {
    const total = this.itemsTotal();
    if (!this.shipping || !r.quantity) return 0;
    const share = total > 0
      ? this.shipping * (r.quantity * (r.unitPrice || 0)) / total
      : this.shipping / this.checkedRows().length;
    return Math.round(share / r.quantity * 100) / 100;
  }

  registeredPrice(r: Row): number {
    return Math.round(((r.unitPrice || 0) + this.shippingPerUnit(r)) * 100) / 100;
  }

  onDateChange() {
    for (const r of this.rows()) {
      if (!r.tagEdited) r.tag = r.hold || !r.product || !this.date ? '' : suggestPurchaseTag(r.product, this.date);
      r.alreadyOn = this.existing.some(e => e.sealedProductId === r.sealedProductId && e.purchasedAt === this.date) ? this.date : undefined;
    }
  }

  onHoldChange(r: Row) {
    r.tagEdited = false;
    r.tag = r.hold || !r.product || !this.date ? '' : suggestPurchaseTag(r.product, this.date);
  }

  save() {
    const rows = this.checkedRows();
    if (rows.some(r => !r.quantity || r.quantity < 1 || r.unitPrice == null || r.unitPrice < 0)) {
      this.snackBar.open('Quantità e prezzo sono obbligatori sulle righe spuntate', 'Chiudi', { duration: 4000 });
      return;
    }
    const inputs: ProductPurchaseInput[] = rows.map(r => {
      const share = this.shippingPerUnit(r);
      return {
        sealedProductId: r.sealedProductId,
        quantity: r.quantity,
        unitPrice: this.registeredPrice(r),
        store: r.store,
        seller: r.seller || null,
        purchasedAt: this.date || null,
        openedAt: null,
        tag: r.tag.trim() || null,
        notes: share > 0 ? `Prezzo ${this.eur(r.unitPrice)} + spedizione ${this.eur(share)} per unità` : null,
        costPerCard: null,
        holdForResale: r.hold
      };
    });
    this.saving.set(true);
    this.purchasing.savePurchases(inputs).subscribe({
      next: count => {
        this.snackBar.open(`${count} ${count === 1 ? 'acquisto registrato' : 'acquisti registrati'} nel registro`, 'Chiudi', { duration: 5000 });
        this.dialogRef.close(count);
      },
      error: err => {
        this.saving.set(false);
        this.snackBar.open(`Errore nella registrazione: ${err.error?.message || err.message}`, 'Chiudi', { duration: 8000 });
      }
    });
  }

  eur(value: number | null | undefined): string {
    return value == null ? '' : RegisterPurchaseDialogComponent.euroFormat.format(value);
  }
}
