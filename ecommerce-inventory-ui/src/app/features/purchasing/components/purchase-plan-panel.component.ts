import { Component, EventEmitter, Input, OnInit, Output, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CardTraderCartLine, PlanCartItem, PlanCartResult, PlanOffer, PlanProduct, PurchasePlan, PurchasingService } from '../services/purchasing.service';

/** Offerta del piano scelta: la stessa offerta può stare nel carrello CT Zero e nel blocco del venditore. */
interface PickedOffer { offer: PlanOffer; viaZero: boolean; }

/** Link da aprire in una scheda nuova. */
interface OpenLink { label: string; url: string; }

/**
 * Piano d'acquisto su Card Trader: venditori che hanno più prodotti convenienti (un'unica
 * spedizione) e carrello Card Trader Zero (venditori diversi, una sola spedizione).
 *
 * Per fare il carrello: "Metti nel carrello Card Trader" aggiunge le offerte al carrello dell'account
 * via API (non compra); "Apri" apre le pagine dei prodotti su Card Trader o Cardmarket. Chrome
 * lascia aprire una sola scheda per clic finché non si consentono i popup del sito: le schede
 * bloccate restano in un elenco di link, apribili uno per uno.
 *
 * Il carrello Card Trader si legge all'apertura del piano e dopo ogni modifica: le righe già nel
 * carrello hanno il segno "nel carrello" e si tolgono da lì, così si vede cosa c'è davvero.
 */
@Component({
  selector: 'app-purchase-plan-panel',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatTooltipModule, MatCheckboxModule, MatProgressSpinnerModule],
  template: `
    <div class="plan">
      <div class="plan-header">
        <strong>Piano d'acquisto su Card Trader</strong>
        <span class="muted">offerte in inglese, prezzi di questo momento ({{ plan.computedAt | date:'dd/MM/yyyy HH:mm' }}).
          "Resa" = valore atteso netto dell'apertura rispetto al prezzo dell'offerta. Spedizione esclusa.</span>
        <span class="spacer"></span>
        <button mat-icon-button (click)="close.emit()" matTooltip="Chiudi il piano"><mat-icon>close</mat-icon></button>
      </div>

      <div class="actions">
        <span class="scope" [class.scope-picked]="picked().size">{{ scopeLabel() }}</span>
        <button mat-stroked-button (click)="openAll('ct')" [disabled]="!scopeProducts()"
                matTooltip="Apre in schede nuove la pagina Card Trader di ogni prodotto: delle offerte spuntate, o di tutto il piano se non ne hai spuntata nessuna">
          <mat-icon>open_in_new</mat-icon> Apri su Card Trader ({{ scopeProducts() }})</button>
        <button mat-stroked-button (click)="openAll('cm')" [disabled]="!scopeProducts()"
                matTooltip="Apre in schede nuove la ricerca Cardmarket di ogni prodotto: delle offerte spuntate, o di tutto il piano se non ne hai spuntata nessuna">
          <mat-icon>open_in_new</mat-icon> Apri su Cardmarket ({{ scopeProducts() }})</button>
        <button mat-flat-button color="primary" (click)="addToCart(pickedList())" [disabled]="!picked().size || cartBusy()"
                matTooltip="Mette le offerte spuntate nel carrello Card Trader del tuo account, una unità ciascuna; quelle già nel carrello si saltano. Non compra: l'acquisto si conclude sul sito">
          <mat-icon>add_shopping_cart</mat-icon> Metti nel carrello ({{ picked().size }})</button>
        <button mat-button *ngIf="picked().size" (click)="clearSelection()">Togli le spunte</button>
        <span class="spacer"></span>
        <span class="cart-state" *ngIf="cart() as c">
          <mat-icon class="good-icon">shopping_cart</mat-icon>
          Nel carrello Card Trader: {{ planInCart() }} offerte del piano<span *ngIf="otherInCart()">, {{ otherInCart() }} altre</span>
          <a [href]="cartUrl" target="_blank" rel="noopener">Apri il carrello</a>
          <button mat-icon-button (click)="loadCart()" [disabled]="cartBusy()" matTooltip="Rileggi il carrello Card Trader"><mat-icon class="link">refresh</mat-icon></button>
          <button mat-button color="warn" *ngIf="planInCart()" (click)="removeFromCart(planCartIds())" [disabled]="cartBusy()"
                  matTooltip="Toglie dal carrello Card Trader tutte le offerte di questo piano (le altre restano)">Togli le offerte del piano</button>
        </span>
        <mat-spinner *ngIf="cartBusy()" diameter="20"></mat-spinner>
      </div>

      <div class="notice" *ngIf="blocked().length">
        <div><mat-icon class="warn">block</mat-icon> Il browser ha bloccato {{ blocked().length }} schede.
          Per aprirle tutte con un clic consenti i popup per questo sito (icona a destra nella barra degli indirizzi,
          "Consenti sempre popup e reindirizzamenti"), oppure aprile da qui:
          <button mat-icon-button (click)="blocked.set([])" matTooltip="Chiudi"><mat-icon>close</mat-icon></button></div>
        <div class="link-list">
          <a *ngFor="let l of blocked()" [href]="l.url" target="_blank" rel="noopener">{{ l.label }}</a>
        </div>
      </div>

      <div class="notice" *ngIf="cartMessage() as m">
        <div>{{ m.text }}
          <a [href]="cartUrl" target="_blank" rel="noopener">Apri il carrello</a>
          per controllare spedizione e totale e concludere l'acquisto.
          <button mat-icon-button (click)="cartMessage.set(null)" matTooltip="Chiudi"><mat-icon>close</mat-icon></button></div>
        <div *ngFor="let f of m.failed" class="warn-text">{{ f.productName || f.cardTraderProductId }}: {{ f.reason }}</div>
      </div>

      <div class="blocks">
        <div class="block">
          <div class="block-title">
            <mat-icon class="zero">local_shipping</mat-icon> Carrello Card Trader Zero
            <span class="muted">— venditori diversi, un'unica spedizione</span>
          </div>
          <ng-container *ngIf="plan.ctZero.productCount > 0; else noZero">
            <div class="totals">{{ plan.ctZero.productCount }} prodotti · spesa <strong>{{ euro(plan.ctZero.total) }}</strong>
              · valore atteso netto {{ euro(plan.ctZero.openValueNet) }} · margine <strong class="good">{{ euro(plan.ctZero.margin) }}</strong>
              <button mat-icon-button (click)="addToCart(asPicked(plan.ctZero.items, true))" [disabled]="cartBusy()"
                      matTooltip="Metti tutto il carrello CT Zero nel carrello Card Trader (via Card Trader Zero)"><mat-icon class="link">add_shopping_cart</mat-icon></button></div>
            <ng-container *ngTemplateOutlet="items; context: { $implicit: plan.ctZero.items, showSeller: true, viaZero: true }"></ng-container>
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
              <button mat-icon-button (click)="addToCart(asPicked(s.items, false))" [disabled]="cartBusy()"
                      matTooltip="Metti i prodotti di questo venditore nel carrello Card Trader (spedizione diretta dal venditore)"><mat-icon class="link">add_shopping_cart</mat-icon></button>
            </div>
            <ng-container *ngTemplateOutlet="items; context: { $implicit: s.items, showSeller: false, viaZero: false }"></ng-container>
          </div>
        </div>
      </div>

      <div class="block" *ngIf="withoutOffers().length">
        <div class="block-title"><mat-icon>info</mat-icon> Prodotti senza offerte convenienti</div>
        <div class="muted" *ngFor="let p of withoutOffers()">
          {{ p.name }}: {{ p.note || ('offerta più economica ' + euro(p.cheapestPrice) + ' (' + p.cheapestSeller + '), valore atteso netto ' + euro(p.openValueNet)) }}
        </div>
      </div>

      <ng-template #items let-items let-showSeller="showSeller" let-viaZero="viaZero">
        <table>
          <tr><th></th><th>Prodotto</th><th *ngIf="showSeller">Venditore</th><th>Prezzo</th><th matTooltip="Trend Cardmarket dello stesso prodotto, per confronto">Trend CM</th><th>Disp.</th><th>Valore atteso</th><th>Resa</th><th></th><th></th></tr>
          <tr *ngFor="let o of items" [class.picked]="isPicked(o, viaZero)" [class.in-cart]="inCart(o)">
            <td><mat-checkbox [checked]="isPicked(o, viaZero)" (change)="toggle(o, viaZero)"
                              [matTooltip]="viaZero ? 'Spunta (via Card Trader Zero)' : 'Spunta (direttamente dal venditore)'"></mat-checkbox></td>
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
            <td><span class="cart-chip" *ngIf="inCart(o) as q" matTooltip="Questa offerta è nel carrello Card Trader">nel carrello{{ q > 1 ? ' ×' + q : '' }}
                <button class="chip-remove" (click)="removeFromCart([o.cardTraderProductId])" [disabled]="cartBusy()"
                        matTooltip="Togli dal carrello Card Trader"><mat-icon>close</mat-icon></button></span></td>
          </tr>
        </table>
      </ng-template>
    </div>
  `,
  styles: [`
    .plan { border: 1px solid #c5cae9; border-radius: 6px; padding: 8px 12px; display: flex; flex-direction: column; gap: 10px;
            max-height: 45vh; overflow: auto; background: #fafafa; }
    .plan-header { display: flex; align-items: center; gap: 10px; }
    .actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .notice { border: 1px solid #ffe082; background: #fffde7; border-radius: 4px; padding: 6px 10px; font-size: 13px; }
    .link-list { display: flex; flex-wrap: wrap; gap: 4px 16px; margin-top: 4px; }
    .warn { color: #e65100; vertical-align: middle; }
    .warn-text { color: #e65100; }
    .good-icon { color: #2e7d32; vertical-align: middle; }
    .scope { font-size: 12px; color: #757575; }
    .scope-picked { color: #1a237e; font-weight: 600; }
    .cart-state { display: flex; align-items: center; gap: 6px; font-size: 13px; }
    tr.picked td { background: #e8eaf6; }
    tr.in-cart td:nth-child(2) { font-weight: 600; }
    .cart-chip { display: inline-flex; align-items: center; gap: 2px; background: #e8f5e9; color: #2e7d32; border-radius: 10px;
                 padding: 0 2px 0 8px; font-size: 12px; white-space: nowrap; }
    .chip-remove { border: none; background: none; cursor: pointer; padding: 0; display: inline-flex; color: #2e7d32; }
    .chip-remove mat-icon { font-size: 14px; height: 14px; width: 14px; }
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
export class PurchasePlanPanelComponent implements OnInit {
  @Input({ required: true }) plan!: PurchasePlan;
  /** Blueprint Card Trader per prodotto, per il link all'offerta. */
  @Input() blueprints: Record<number, number | undefined> = {};
  @Output() close = new EventEmitter<void>();

  /** Offerte selezionate, per chiave offerta + modo di spedizione. */
  picked = signal(new Map<string, PickedOffer>());
  /** Schede che il browser non ha lasciato aprire. */
  blocked = signal<OpenLink[]>([]);
  cartBusy = signal(false);
  /** Esito dell'ultima aggiunta o rimozione. */
  cartMessage = signal<{ text: string; failed: PlanCartResult['failed'] } | null>(null);
  /** Carrello Card Trader dell'account: null finché non è letto. */
  cart = signal<CardTraderCartLine[] | null>(null);

  /** /cart dà 404: il carrello del sito è /cart/edit (prova del 16/10). */
  readonly cartUrl = 'https://www.cardtrader.com/cart/edit';

  /** Quantità nel carrello per offerta. */
  private cartQuantities = computed(() => {
    const map = new Map<number, number>();
    for (const l of this.cart() ?? []) map.set(l.productId, (map.get(l.productId) ?? 0) + l.quantity);
    return map;
  });

  private static readonly euroFormat = new Intl.NumberFormat('it-IT', { style: 'currency', currency: 'EUR' });

  constructor(private purchasing: PurchasingService) { }

  ngOnInit(): void {
    this.loadCart();
  }

  loadCart(): void {
    this.purchasing.getPlanCart().subscribe({
      next: c => this.cart.set(c),
      error: err => this.cartMessage.set({ text: `Carrello Card Trader non letto: ${err.error?.message || err.message}.`, failed: [] })
    });
  }

  /** Unità di questa offerta nel carrello Card Trader (0 se non c'è). */
  inCart(offer: PlanOffer): number {
    return this.cartQuantities().get(offer.cardTraderProductId) ?? 0;
  }

  /** Id delle offerte del piano che sono nel carrello. */
  planCartIds(): number[] {
    const ids = new Set(this.planOffers().map(o => o.cardTraderProductId));
    return [...this.cartQuantities().keys()].filter(id => ids.has(id));
  }

  planInCart(): number {
    return this.planCartIds().length;
  }

  otherInCart(): number {
    return this.cartQuantities().size - this.planInCart();
  }

  /** Prodotti su cui agiscono i pulsanti "Apri": quelli spuntati, o tutto il piano. */
  scopeProducts(): number {
    return new Set(this.scopeOffers().map(o => o.productId)).size;
  }

  scopeLabel(): string {
    return this.picked().size
      ? `${this.picked().size} offerte spuntate`
      : `Nessuna spunta: "Apri" vale per tutti i ${this.scopeProducts()} prodotti del piano`;
  }

  private scopeOffers(): PlanOffer[] {
    return this.picked().size ? this.pickedList().map(p => p.offer) : this.planOffers();
  }

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

  private key(offer: PlanOffer, viaZero: boolean): string {
    return (viaZero ? 'z' : 's') + offer.cardTraderProductId;
  }

  isPicked(offer: PlanOffer, viaZero: boolean): boolean {
    return this.picked().has(this.key(offer, viaZero));
  }

  toggle(offer: PlanOffer, viaZero: boolean): void {
    const next = new Map(this.picked());
    const key = this.key(offer, viaZero);
    if (next.has(key)) next.delete(key); else next.set(key, { offer, viaZero });
    this.picked.set(next);
  }

  clearSelection(): void {
    this.picked.set(new Map());
  }

  pickedList(): PickedOffer[] {
    return [...this.picked().values()];
  }

  asPicked(offers: PlanOffer[], viaZero: boolean): PickedOffer[] {
    return offers.map(offer => ({ offer, viaZero }));
  }

  /** Offerte visibili nel piano: carrello CT Zero e venditori con più prodotti. */
  private planOffers(): PlanOffer[] {
    return [...this.plan.ctZero.items, ...this.multiSellers().flatMap(s => s.items)];
  }

  /** Un link per prodotto (non per offerta): la pagina CT del blueprint o la ricerca CM. */
  linksFor(site: 'ct' | 'cm', offers: PlanOffer[]): OpenLink[] {
    const links: OpenLink[] = [];
    const seen = new Set<number>();
    for (const o of offers) {
      if (seen.has(o.productId)) continue;
      seen.add(o.productId);
      if (site === 'ct') {
        const bp = this.blueprintOf(o);
        if (bp) links.push({ label: o.productName, url: 'https://www.cardtrader.com/cards/' + bp });
      } else {
        const name = this.productOf(o)?.cardmarketName;
        if (name) links.push({ label: o.productName, url: this.cardmarketSearch(name) });
      }
    }
    return links;
  }

  /**
   * Apre i prodotti della selezione (o di tutto il piano) in schede nuove. Chrome consente una sola
   * scheda per clic finché i popup del sito non sono consentiti: window.open restituisce null per
   * le altre, che finiscono nell'elenco di link da aprire a mano.
   */
  openAll(site: 'ct' | 'cm'): void {
    const blocked: OpenLink[] = [];
    for (const link of this.linksFor(site, this.scopeOffers())) {
      const tab = window.open(link.url, '_blank');
      if (tab) tab.opener = null; else blocked.push(link);
    }
    this.blocked.set(blocked);
  }

  /**
   * Mette le offerte nel carrello Card Trader dell'account (una unità ciascuna; il server salta quelle
   * già nel carrello). Non compra. Dopo l'aggiunta le spunte si tolgono: le righe aggiunte hanno il
   * segno "nel carrello".
   */
  addToCart(offers: PickedOffer[]): void {
    if (!offers.length || this.cartBusy()) return;
    // La stessa offerta spuntata in due blocchi entra una volta sola; due offerte diverse dello
    // stesso prodotto, invece, vorrebbero dire comprarlo due volte.
    const products = new Set(offers.map(p => p.offer.productId));
    const distinctOffers = new Set(offers.map(p => p.offer.cardTraderProductId));
    if (products.size < distinctOffers.size
        && !confirm('Alcuni prodotti sono spuntati due volte (CT Zero e venditore, offerte diverse): li compreresti due volte. Procedere?'))
      return;

    const items: PlanCartItem[] = offers.map(p => ({
      cardTraderProductId: p.offer.cardTraderProductId, viaCardTraderZero: p.viaZero, productName: p.offer.productName
    }));
    this.cartBusy.set(true);
    this.cartMessage.set(null);
    this.purchasing.addPlanToCart(items).subscribe({
      next: r => {
        this.cart.set(r.cart);
        const already = r.alreadyInCart ? `, ${r.alreadyInCart} erano già nel carrello` : '';
        const failed = r.failed.length ? `, ${r.failed.length} non aggiunte (sotto il motivo)` : '';
        this.cartMessage.set({ text: `${r.added} offerte aggiunte al carrello Card Trader${already}${failed}.`, failed: r.failed });
        this.clearSelection();
        this.cartBusy.set(false);
      },
      error: err => {
        this.cartMessage.set({ text: `Carrello non aggiornato: ${err.error?.message || err.message}.`, failed: [] });
        this.cartBusy.set(false);
      }
    });
  }

  /** Toglie dal carrello Card Trader le offerte indicate, tutte le unità. */
  removeFromCart(ids: number[]): void {
    if (!ids.length || this.cartBusy()) return;
    this.cartBusy.set(true);
    this.cartMessage.set(null);
    this.purchasing.removeFromPlanCart(ids).subscribe({
      next: r => {
        this.cart.set(r.cart);
        if (r.failed.length) this.cartMessage.set({ text: 'Alcune offerte non sono state tolte dal carrello.', failed: r.failed });
        this.cartBusy.set(false);
      },
      error: err => {
        this.cartMessage.set({ text: `Carrello non aggiornato: ${err.error?.message || err.message}.`, failed: [] });
        this.cartBusy.set(false);
      }
    });
  }
}
