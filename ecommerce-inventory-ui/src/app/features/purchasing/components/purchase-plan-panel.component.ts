import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { PlanOffer, PlanProduct, PurchasePlan } from '../services/purchasing.service';

/**
 * Piano d'acquisto su Card Trader: venditori che hanno più prodotti convenienti (un'unica
 * spedizione) e carrello Card Trader Zero (venditori diversi, una sola spedizione).
 */
@Component({
  selector: 'app-purchase-plan-panel',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatTooltipModule],
  template: `
    <div class="plan">
      <div class="plan-header">
        <strong>Piano d'acquisto su Card Trader</strong>
        <span class="muted">offerte in inglese, prezzi di questo momento ({{ plan.computedAt | date:'dd/MM/yyyy HH:mm' }}).
          "Resa" = valore atteso netto dell'apertura rispetto al prezzo dell'offerta. Spedizione esclusa.</span>
        <span class="spacer"></span>
        <button mat-icon-button (click)="close.emit()" matTooltip="Chiudi il piano"><mat-icon>close</mat-icon></button>
      </div>

      <div class="blocks">
        <div class="block">
          <div class="block-title">
            <mat-icon class="zero">local_shipping</mat-icon> Carrello Card Trader Zero
            <span class="muted">— venditori diversi, un'unica spedizione</span>
          </div>
          <ng-container *ngIf="plan.ctZero.productCount > 0; else noZero">
            <div class="totals">{{ plan.ctZero.productCount }} prodotti · spesa <strong>{{ euro(plan.ctZero.total) }}</strong>
              · valore atteso netto {{ euro(plan.ctZero.openValueNet) }} · margine <strong class="good">{{ euro(plan.ctZero.margin) }}</strong></div>
            <ng-container *ngTemplateOutlet="items; context: { $implicit: plan.ctZero.items, showSeller: true }"></ng-container>
          </ng-container>
          <ng-template #noZero><div class="muted">Nessuna offerta conveniente spedibile con Card Trader Zero.</div></ng-template>
        </div>

        <div class="block">
          <div class="block-title"><mat-icon>storefront</mat-icon> Venditori con più prodotti convenienti</div>
          <div class="muted" *ngIf="multiSellers().length === 0">Nessun venditore ha più di un prodotto conveniente.</div>
          <div class="seller" *ngFor="let s of multiSellers()">
            <div class="seller-head">
              <strong>{{ s.sellerName }}</strong> <span class="muted">{{ s.country }}</span>
              <span class="badge" *ngIf="s.ctZero" matTooltip="Può spedire i sigillati con Card Trader Zero">CT Zero</span>
              · {{ s.productCount }} prodotti · spesa <strong>{{ euro(s.total) }}</strong> · margine <strong class="good">{{ euro(s.margin) }}</strong>
            </div>
            <ng-container *ngTemplateOutlet="items; context: { $implicit: s.items, showSeller: false }"></ng-container>
          </div>
        </div>
      </div>

      <div class="block" *ngIf="withoutOffers().length">
        <div class="block-title"><mat-icon>info</mat-icon> Prodotti senza offerte convenienti</div>
        <div class="muted" *ngFor="let p of withoutOffers()">
          {{ p.name }}: {{ p.note || ('offerta più economica ' + euro(p.cheapestPrice) + ' (' + p.cheapestSeller + '), valore atteso netto ' + euro(p.openValueNet)) }}
        </div>
      </div>

      <ng-template #items let-items let-showSeller="showSeller">
        <table>
          <tr><th>Prodotto</th><th *ngIf="showSeller">Venditore</th><th>Prezzo</th><th matTooltip="Trend Cardmarket dello stesso prodotto, per confronto">Trend CM</th><th>Disp.</th><th>Valore atteso</th><th>Resa</th><th></th></tr>
          <tr *ngFor="let o of items">
            <td>{{ o.productName }}</td>
            <td *ngIf="showSeller">{{ o.sellerName }} <span class="muted">{{ o.sellerCountry }}</span></td>
            <td>{{ euro(o.price) }}</td>
            <td class="muted-cell">{{ euro(productOf(o)?.cmTrend) }}</td>
            <td>{{ o.available }}</td>
            <td>{{ euro(o.openValueNet) }}</td>
            <td class="good">+{{ o.roiPercent }}%</td>
            <td><a *ngIf="blueprintOf(o) as bp" [href]="'https://www.cardtrader.com/cards/' + bp" target="_blank" rel="noopener"
                   matTooltip="Apri su Card Trader"><mat-icon class="link">open_in_new</mat-icon></a>
                <a *ngIf="productOf(o)?.cardmarketName as cm" [href]="cardmarketSearch(cm)" target="_blank" rel="noopener"
                   matTooltip="Cerca su Cardmarket"><span class="cm-link">CM</span></a></td>
          </tr>
        </table>
      </ng-template>
    </div>
  `,
  styles: [`
    .plan { border: 1px solid #c5cae9; border-radius: 6px; padding: 8px 12px; display: flex; flex-direction: column; gap: 10px;
            max-height: 45vh; overflow: auto; background: #fafafa; }
    .plan-header { display: flex; align-items: center; gap: 10px; }
    .spacer { flex: 1; }
    .muted { color: #757575; font-size: 12px; }
    .blocks { display: flex; gap: 24px; flex-wrap: wrap; }
    .block { flex: 1; min-width: 420px; }
    .block-title { font-weight: 600; display: flex; align-items: center; gap: 6px; margin-bottom: 4px; }
    .totals { margin-bottom: 4px; }
    .seller { margin-bottom: 10px; }
    .badge { background: #e8f5e9; color: #2e7d32; border-radius: 10px; padding: 0 8px; font-size: 12px; margin-left: 4px; }
    .good { color: #2e7d32; font-weight: 600; }
    .zero { color: #2e7d32; }
    table { border-collapse: collapse; font-size: 13px; }
    th, td { padding: 2px 10px 2px 0; text-align: left; }
    th { color: #757575; font-weight: 500; }
    .muted-cell { color: #616161; }
    .cm-link { font-size: 11px; font-weight: 600; color: #3f51b5; margin-left: 4px; vertical-align: middle; }
    .link { font-size: 16px; height: 16px; width: 16px; vertical-align: middle; color: #3f51b5; }
  `]
})
export class PurchasePlanPanelComponent {
  @Input({ required: true }) plan!: PurchasePlan;
  /** Blueprint Card Trader per prodotto, per il link all'offerta. */
  @Input() blueprints: Record<number, number | undefined> = {};
  @Output() close = new EventEmitter<void>();

  private static readonly euroFormat = new Intl.NumberFormat('it-IT', { style: 'currency', currency: 'EUR' });

  euro(value?: number | null): string {
    return value == null ? '—' : PurchasePlanPanelComponent.euroFormat.format(value);
  }

  multiSellers() {
    return this.plan.sellers.filter(s => s.productCount > 1);
  }

  withoutOffers() {
    const covered = new Set(this.plan.sellers.flatMap(s => s.items.map(i => i.productId)));
    return this.plan.products.filter(p => !covered.has(p.productId));
  }

  blueprintOf(offer: PlanOffer): number | undefined {
    return this.blueprints[offer.productId] ?? this.productOf(offer)?.cardTraderBlueprintId;
  }

  productOf(offer: PlanOffer): PlanProduct | undefined {
    return this.plan.products.find(p => p.productId === offer.productId);
  }

  /** Cardmarket non ha un indirizzo per id prodotto: si apre la ricerca col nome esatto. */
  cardmarketSearch(name: string): string {
    return 'https://www.cardmarket.com/en/Magic/Products/Search?searchString=' + encodeURIComponent(name);
  }
}
