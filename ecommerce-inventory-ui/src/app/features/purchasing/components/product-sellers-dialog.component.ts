import { Component, Inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { PlanCartItem, ProductSellerOrder, ProductSellers, PurchasingService, SellerProduct } from '../services/purchasing.service';
import { RegisterOrderLine, openRegisterPurchase } from './register-purchase-dialog.component';

export interface ProductSellersData {
  productId: number;
  name: string;
}

export function openProductSellers(dialog: MatDialog, data: ProductSellersData) {
  return dialog.open(ProductSellersDialogComponent, { data, width: '1200px', maxWidth: '95vw', autoFocus: false });
}

/**
 * "Chi lo vende" (20/11): per un prodotto che l'utente vuole comprare comunque, ogni venditore che lo ha con l'ordine
 * che conviene fare da lui, cioè il prodotto più le sue aperture e i suoi tenuti secondo le regole del Budget. In cima chi
 * costa meno contando il guadagno atteso delle aggiunte.
 */
@Component({
  selector: 'app-product-sellers-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule, MatSnackBarModule],
  template: `
    <h2 mat-dialog-title>Chi vende {{ data.name }}</h2>
    <mat-dialog-content>
      @if (loading()) { <mat-progress-bar mode="indeterminate"></mat-progress-bar><p class="muted">Leggo le offerte Card Trader del prodotto…</p> }
      @if (error()) { <div class="error">{{ error() }}</div> }
      @if (result(); as r) {
        <p class="muted">
          Offerte Card Trader del prodotto {{ r.freshCardTrader ? 'lette adesso' : 'dalla lettura del ' + (r.collectedAt | date:'dd/MM HH:mm') }};
          gli altri prodotti dei venditori dalla lettura "Cerca i venditori" del <b>{{ r.collectedAt | date:'dd/MM HH:mm' }}</b>
          (solo le espansioni lette), Cardmarket solo dai venditori incollati.
          Con il prodotto entrano le aperture con resa ≥ {{ r.rules.addOnRoiPercent }}% e i tenuti a non oltre il trend +{{ r.rules.maxHoldPremiumPercent }}%,
          fino al budget di {{ eur(r.budget) }}. <b>Costo effettivo</b> = prezzo del prodotto + spedizione − guadagno atteso delle aggiunte.
        </p>
        @if (r.orders.length === 0) { <p>Nessuno lo vende fra le offerte lette.</p> }
        @for (o of r.orders; track o.key; let first = $first) {
          <div class="order" [class.best]="first">
            <div class="head">
              <div>
                <span class="store">{{ o.store }}</span> · <b>{{ o.seller }}</b>@if (o.country) { <span class="muted"> ({{ o.country }})</span> }
                · prodotto <b>{{ eur(o.offer.price) }}</b>@if (o.key === 'ctzero') { <span class="muted"> da {{ o.offer.seller }}</span> }
                @if (o.targetRoiPercent != null) { <span class="muted"> (resa {{ o.targetRoiPercent.toFixed(0) }}%)</span> }
                · spedizione {{ eur(o.shipping) }}
              </div>
              <div class="totals">
                @if (o.addOns.length) {
                  + {{ o.addOns.length }} {{ o.addOns.length === 1 ? 'aggiunta' : 'aggiunte' }} {{ eur(o.addOnCost) }} (guadagno <span class="gain">{{ eur(o.addOnGain) }}</span>) ·
                }
                ordine <b>{{ eur(o.total) }}</b> · costo effettivo <b class="eff">{{ eur(o.effectiveCost) }}</b>
              </div>
            </div>
            @if (o.addOns.length) {
              <table>
                <tbody>
                  @for (l of o.addOns; track l.offer.productId) {
                    <tr class="good">
                      <td><span class="kind">{{ l.kind === 'Open' ? 'Apri' : 'Tieni' }}</span></td>
                      <td><a [href]="productUrl(o, l.product)" target="_blank" rel="noopener">{{ l.product.name }}</a>
                        <span class="muted"> · {{ l.product.setCode }}</span>@if (o.key === 'ctzero') { <span class="muted"> · {{ l.offer.seller }}</span> }</td>
                      <td class="num">{{ eur(l.offer.price) }}</td>
                      <td class="num">{{ eur(l.product.cmTrend) }}</td>
                      <td class="num">{{ l.roiPercent != null ? l.roiPercent.toFixed(0) + '%' : '—' }}</td>
                      <td class="num">{{ l.kind === 'Open' ? eur(l.gain) : (l.product.holdScore?.toFixed(0) + ' punti') }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            }
            <div class="actions">
              @if (o.store !== 'Cardmarket') {
                <button mat-stroked-button color="primary" [disabled]="adding() === o.key" (click)="addToCart(o)">
                  <mat-icon>add_shopping_cart</mat-icon> {{ adding() === o.key ? 'Aggiungo…' : 'Metti nel carrello Card Trader' }}
                </button>
              } @else {
                <a mat-stroked-button [href]="cmSellerUrl(o.seller, r.product)" target="_blank" rel="noopener">Il prodotto fra le sue offerte</a>
              }
              <button mat-stroked-button (click)="register(o)"><mat-icon>post_add</mat-icon> Registra l'acquisto</button>
              @if (o.others.length) {
                <button mat-button (click)="toggle(o.key)">
                  <mat-icon>{{ open() === o.key ? 'expand_less' : 'list' }}</mat-icon>
                  {{ o.key === 'ctzero' ? 'Altre offerte interessanti' : 'Le altre sue offerte' }} ({{ o.others.length }})
                </button>
              }
            </div>
            @if (open() === o.key) {
              <table>
                <thead><tr><th>Prodotto</th><th class="num">Prezzo</th><th class="num">Trend CM</th><th class="num">Resa</th><th class="num">Guadagno</th><th>Valutazione</th></tr></thead>
                <tbody>
                  @for (x of o.others; track x.offer.productId + x.offer.seller) {
                    <tr [class.near]="x.group === 1" [class.maybe]="x.group === 2">
                      <td><a [href]="productUrl(o, x.product)" target="_blank" rel="noopener">{{ x.product.name }}</a>
                        <span class="muted"> · {{ x.product.setCode }}</span>@if (o.key === 'ctzero') { <span class="muted"> · {{ x.offer.seller }}</span> }</td>
                      <td class="num">{{ eur(x.offer.price) }}</td>
                      <td class="num">{{ eur(x.product.cmTrend) }}</td>
                      <td class="num">{{ x.roiPercent != null ? x.roiPercent.toFixed(0) + '%' : '—' }}</td>
                      <td class="num">{{ x.gain != null ? eur(x.gain) : '—' }}</td>
                      <td>{{ x.verdict }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </div>
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Chiudi</button></mat-dialog-actions>
  `,
  styles: [`
    .muted { color: #757575; font-size: 12px; }
    .error { color: #c62828; margin: 8px 0; }
    .order { border: 1px solid #e0e0e0; border-radius: 6px; padding: 8px 12px; margin: 8px 0; }
    .order.best { border-color: #43a047; box-shadow: inset 4px 0 0 #43a047; }
    .head { display: flex; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .store { font-weight: 600; color: #3f51b5; }
    .gain { color: #2e7d32; font-weight: 600; }
    .eff { color: #1b5e20; }
    table { width: 100%; border-collapse: collapse; margin-top: 6px; font-size: 13px; }
    th, td { padding: 3px 6px; border-bottom: 1px solid #f0f0f0; text-align: left; }
    .num { text-align: right; white-space: nowrap; }
    tr.good td { background: #dcedc8; }
    tr.near td { background: #fff8e1; }
    tr.maybe td { background: #e3f2fd; }
    .kind { font-size: 11px; font-weight: 600; }
    .actions { display: flex; gap: 8px; margin-top: 6px; flex-wrap: wrap; }
  `]
})
export class ProductSellersDialogComponent implements OnInit {
  loading = signal(true);
  error = signal<string | null>(null);
  result = signal<ProductSellers | null>(null);
  open = signal<string | null>(null);
  adding = signal<string | null>(null);

  constructor(@Inject(MAT_DIALOG_DATA) public data: ProductSellersData, private purchasing: PurchasingService,
    private dialog: MatDialog, private snackBar: MatSnackBar) { }

  ngOnInit(): void {
    this.purchasing.getProductSellers(this.data.productId).subscribe({
      next: r => { this.loading.set(false); this.result.set(r); },
      error: err => { this.loading.set(false); this.error.set(err?.error?.message || 'Venditori non letti'); }
    });
  }

  toggle(key: string): void {
    this.open.set(this.open() === key ? null : key);
  }

  eur(v?: number | null): string {
    return v == null ? '—' : v.toLocaleString('it-IT', { style: 'currency', currency: 'EUR' });
  }

  productUrl(o: ProductSellerOrder, p: SellerProduct): string {
    if (o.store === 'Cardmarket') return this.cmSellerUrl(o.seller, p);
    return p.cardTraderBlueprintId ? 'https://www.cardtrader.com/cards/' + p.cardTraderBlueprintId : '#';
  }

  cmSellerUrl(seller: string, p: SellerProduct): string {
    const name = encodeURIComponent(p.cardmarketName ?? p.name).replace(/%20/g, '+');
    return `https://www.cardmarket.com/en/Magic/Users/${encodeURIComponent(seller)}/Offers/Search?search=${name}`;
  }

  /** Il prodotto e le aggiunte nel carrello Card Trader (via API, non compra). */
  addToCart(o: ProductSellerOrder): void {
    const items: PlanCartItem[] = [o.offer, ...o.addOns.map(l => l.offer)]
      .filter(x => x.cardTraderProductId)
      .map(x => ({ cardTraderProductId: x.cardTraderProductId!, viaCardTraderZero: o.key === 'ctzero', productName: this.nameOf(x.productId) }));
    if (items.length === 0) return;
    this.adding.set(o.key);
    this.purchasing.addPlanToCart(items).subscribe({
      next: res => {
        this.adding.set(null);
        const parts = [`${res.added} aggiunte`];
        if (res.alreadyInCart) parts.push(`${res.alreadyInCart} già nel carrello`);
        if (res.failed.length) parts.push(`${res.failed.length} rifiutate (vendute o cambiate)`);
        this.snackBar.open('Carrello Card Trader: ' + parts.join(', '), 'OK', { duration: 6000 });
      },
      error: err => {
        this.adding.set(null);
        this.snackBar.open(err?.error?.message || 'Carrello non aggiornato', 'OK', { duration: 6000 });
      }
    });
  }

  register(o: ProductSellerOrder): void {
    const r = this.result();
    if (!r) return;
    const lines: RegisterOrderLine[] = [
      { sealedProductId: r.product.productId, name: r.product.name, quantity: 1, unitPrice: o.offer.price, store: o.store, seller: o.offer.seller, hold: false },
      ...o.addOns.map(l => ({ sealedProductId: l.product.productId, name: l.product.name, quantity: 1, unitPrice: l.offer.price,
        store: o.store, seller: l.offer.seller, hold: l.kind === 'Hold' }))
    ];
    openRegisterPurchase(this.dialog, { title: o.key === 'ctzero' ? o.store : `${o.store} · ${o.seller}`, shipping: o.shipping, lines });
  }

  private nameOf(productId: number): string {
    const r = this.result();
    if (!r) return '';
    if (r.product.productId === productId) return r.product.name;
    for (const o of r.orders) {
      const l = o.addOns.find(a => a.product.productId === productId);
      if (l) return l.product.name;
    }
    return '';
  }
}
