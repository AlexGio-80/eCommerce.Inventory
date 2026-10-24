import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { RegisterOrderLine, openRegisterPurchase } from './register-purchase-dialog.component';
import {
  BudgetPlanBasket, BudgetPlanLine, BudgetPlanResult, BudgetPlanStatus, BudgetShipping, PastedSeller, PlanCartItem, PurchasingService,
  SellerBasket, SellerBasketLine, SellerPasteResult, SellerPlanRequest, SellerPlanResult, SellerPlanStatus, SellerRules
} from '../services/purchasing.service';

const LAST_PLAN_KEY = 'budget_plan_last';
const LAST_REQUEST_KEY = 'budget_plan_request';
const LAST_SELLER_KEY = 'budget_plan_last_seller';
const MODE_KEY = 'budget_plan_mode';
const SELLER_REQUEST_KEY = 'budget_plan_seller_request';

/**
 * Pianificatore del budget (feature 007, Fase 0): dato quanto si può spendere, quali sigillati comprare, da chi
 * e quanti, fra Card Trader, Cardmarket (al prezzo più basso, venditore da cercare) e il negozio Wizards.
 * Il calcolo legge le offerte Card Trader in background (1-2 minuti); togliere una riga o cambiare le
 * spedizioni ricalcola sulle stesse offerte, subito.
 * "Venditori Cardmarket" (Fase 1): le pagine delle offerte di un venditore, copiate e incollate dall'utente,
 * diventano carrelli veri di quel venditore nel piano.
 */
@Component({
  selector: 'app-budget-plan-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatCheckboxModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatProgressBarModule, MatTooltipModule, MatSnackBarModule, MatDialogModule],
  template: `
    <div class="container">
      <div class="modes">
        <button mat-button [class.active]="mode() === 'sellers'" (click)="setMode('sellers')"><mat-icon>storefront</mat-icon> Per venditore</button>
        <button mat-button [class.active]="mode() === 'plan'" (click)="setMode('plan')"><mat-icon>calculate</mat-icon> Piano automatico</button>
      </div>

      @if (mode() === 'sellers') {
        <mat-card>
          <mat-card-content>
            <div class="toolbar">
              <mat-form-field appearance="outline" class="budget">
                <mat-label>Budget venditori €</mat-label>
                <input matInput type="number" min="1" step="10" [(ngModel)]="budget">
              </mat-form-field>
              <mat-form-field appearance="outline" class="small">
                <mat-label>Traino ≥ %</mat-label>
                <input matInput type="number" min="0" step="5" [(ngModel)]="rules.anchorRoiPercent">
              </mat-form-field>
              <mat-form-field appearance="outline" class="small">
                <mat-label>Altri ≥ %</mat-label>
                <input matInput type="number" min="0" step="5" [(ngModel)]="rules.addOnRoiPercent">
              </mat-form-field>
              <mat-form-field appearance="outline" class="small">
                <mat-label>Netto ≥ %</mat-label>
                <input matInput type="number" min="0" step="5" [(ngModel)]="rules.minNetPercent">
              </mat-form-field>
              <mat-form-field appearance="outline" class="small">
                <mat-label>Wizards €</mat-label>
                <input matInput type="number" min="0" step="50" [(ngModel)]="wizardsBudget">
              </mat-form-field>
              <div class="stores">
                <span class="label">Compro su</span>
                @for (s of storeOptions; track s.key) {
                  <mat-checkbox [checked]="!excludedStores.includes(s.key)" (change)="toggleStore(s.key, $event.checked)">{{ s.label }}</mat-checkbox>
                }
              </div>
              <button mat-stroked-button (click)="showShipping.set(!showShipping())"><mat-icon>local_shipping</mat-icon> Spedizioni</button>
              <span class="spacer"></span>
              @if (sellerResult()) {
                <button mat-stroked-button [disabled]="sellerRunning()" (click)="recomputeSellers()">Aggiorna</button>
              }
              <button mat-raised-button color="primary" [disabled]="sellerRunning()" (click)="startSellers()">
                <mat-icon>search</mat-icon> Cerca i venditori
              </button>
            </div>
            <div class="muted">Un venditore entra se ha un prodotto da aprire con resa ≥ traino; poi si aggiungono le sue altre aperture ≥ "altri"
              e i suoi prodotti da tenere a non oltre il trend +{{ rules.maxHoldPremiumPercent }}%. Conviene se il guadagno delle aperture,
              tolta la spedizione, è almeno il "netto" della spesa per aprirle. Una copia per prodotto.</div>
            @if (showShipping() && shipping) {
              <div class="shipping">
                <mat-form-field appearance="outline"><mat-label>Card Trader, per venditore €</mat-label>
                  <input matInput type="number" min="0" step="0.5" [(ngModel)]="shipping.cardTrader"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Card Trader Zero, una volta €</mat-label>
                  <input matInput type="number" min="0" step="0.5" [(ngModel)]="shipping.cardTraderZero"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Cardmarket, per venditore €</mat-label>
                  <input matInput type="number" min="0" step="0.5" [(ngModel)]="shipping.cardmarket"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Wizards €</mat-label>
                  <input matInput type="number" min="0" step="0.5" [(ngModel)]="shipping.wizards"></mat-form-field>
              </div>
            }
            @if (sellerRunning()) {
              <div class="progress">
                <div>Leggo le offerte Card Trader: espansione {{ sellerStatus()?.done ?? 0 }} di {{ sellerStatus()?.total || '…' }}
                  (20 richieste al minuto: circa 5 minuti; puoi cambiare scheda, la lettura continua)</div>
                <mat-progress-bar [mode]="sellerStatus()?.total ? 'determinate' : 'indeterminate'"
                  [value]="sellerStatus()?.total ? (sellerStatus()!.done / sellerStatus()!.total) * 100 : 0"></mat-progress-bar>
              </div>
            }
            @if (error()) { <div class="error">{{ error() }}</div> }
          </mat-card-content>
        </mat-card>
      }

      @if (mode() === 'plan') {
      <mat-card>
        <mat-card-content>
          <div class="toolbar">
            <mat-form-field appearance="outline" class="budget">
              <mat-label>Budget €</mat-label>
              <input matInput type="number" min="1" step="10" [(ngModel)]="budget">
            </mat-form-field>
            <div class="share">
              <div class="share-label">Aprire <b>{{ openShare }}%</b> · Tenere <b>{{ 100 - openShare }}%</b></div>
              <input type="range" min="0" max="100" step="10" [(ngModel)]="openShare">
            </div>
            <mat-checkbox [(ngModel)]="includeBorderline">Secret Lair anche "Al limite"</mat-checkbox>
            <div class="stores">
              <span class="label">Compro su</span>
              @for (s of storeOptions; track s.key) {
                <mat-checkbox [checked]="!excludedStores.includes(s.key)" (change)="toggleStore(s.key, $event.checked)">{{ s.label }}</mat-checkbox>
              }
            </div>
            <button mat-stroked-button (click)="showShipping.set(!showShipping())">
              <mat-icon>local_shipping</mat-icon> Spedizioni
            </button>
            <span class="spacer"></span>
            <button mat-raised-button color="primary" [disabled]="running() || !(budget > 0)" (click)="start()">
              <mat-icon>calculate</mat-icon> Calcola il piano
            </button>
          </div>

          @if (showShipping() && shipping) {
            <div class="shipping">
              <mat-form-field appearance="outline"><mat-label>Card Trader, per venditore €</mat-label>
                <input matInput type="number" min="0" step="0.5" [(ngModel)]="shipping.cardTrader"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Card Trader Zero, una volta €</mat-label>
                <input matInput type="number" min="0" step="0.5" [(ngModel)]="shipping.cardTraderZero"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Cardmarket, per venditore €</mat-label>
                <input matInput type="number" min="0" step="0.5" [(ngModel)]="shipping.cardmarket"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Wizards €</mat-label>
                <input matInput type="number" min="0" step="0.5" [(ngModel)]="shipping.wizards"></mat-form-field>
              @if (planId()) {
                <button mat-stroked-button (click)="recompute()">Ricalcola con queste spedizioni</button>
              }
            </div>
          }

          @if (running()) {
            <div class="progress">
              <div>Leggo le offerte Card Trader: {{ status()?.done ?? 0 }} di {{ status()?.total || '…' }} prodotti
                (20 richieste al minuto, ci vuole un minuto o due)</div>
              <mat-progress-bar [mode]="status()?.total ? 'determinate' : 'indeterminate'"
                [value]="status()?.total ? (status()!.done / status()!.total) * 100 : 0"></mat-progress-bar>
            </div>
          }
          @if (error()) { <div class="error">{{ error() }}</div> }
        </mat-card-content>
      </mat-card>

      }

      <mat-card>
        <mat-card-content>
          <div class="sellers-head" (click)="showSellers.set(!showSellers())">
            <mat-icon>{{ showSellers() ? 'expand_less' : 'expand_more' }}</mat-icon>
            <b>Venditori Cardmarket</b>
            <span class="muted">· {{ sellers().length ? sellers().length + ' incollati negli ultimi giorni' : 'incolla le pagine delle offerte di un venditore' }}</span>
          </div>
          @if (showSellers()) {
            @if (sellers().length) {
              <div class="sellers">
                @for (s of sellers(); track s.seller) {
                  <span class="seller-chip">
                    <a href="" [class.current]="pasteResult()?.seller === s.seller" (click)="$event.preventDefault(); showSeller(s.seller)"
                       matTooltip="Rivedi le sue offerte">{{ s.seller }}</a>
                    <a [href]="sellerUrl(s.seller)" target="_blank" rel="noopener" matTooltip="Apri su Cardmarket"><mat-icon class="tiny">open_in_new</mat-icon></a>
                    · {{ s.offers }} offerte · <span [class.stale]="isStale(s.pastedAt)" [matTooltip]="isStale(s.pastedAt) ? 'Incollato più di 7 giorni fa: reincolla le sue pagine per aggiornare prezzi e disponibilità' : ''">{{ s.pastedAt | date:'dd/MM HH:mm' }}</span>
                    <button mat-icon-button matTooltip="Togli le offerte di questo venditore" (click)="deleteSeller(s)"><mat-icon>close</mat-icon></button>
                  </span>
                }
              </div>
            }
            <div class="open-seller">
              <mat-form-field appearance="outline">
                <mat-label>Venditore Cardmarket da aprire</mat-label>
                <input matInput [(ngModel)]="pasteSeller" (keydown.enter)="openSellerPage('Preconstructed-Decks')">
              </mat-form-field>
              <span class="muted">Apri le sue offerte in inglese:</span>
              @for (cat of sellerCategories; track cat.slug) {
                <button mat-stroked-button [disabled]="!pasteSeller.trim()" (click)="openSellerPage(cat.slug)">{{ cat.label }}</button>
              }
            </div>
            <p class="muted">Sulla pagina che si apre premi Ctrl+A, Ctrl+C e incolla qui sotto. Una pagina alla volta: pagina 2 e altre categorie
              si aggiungono; reincollando la pagina 1 di una categoria la sostituisci (i prodotti venduti spariscono). I venditori restano
              nell'analisi finché non li togli: tocca il nome per rivederli, la data dice quando li hai incollati.</p>
            <div class="paste">
              <mat-form-field appearance="outline" class="paste-text">
                <mat-label>Testo della pagina</mat-label>
                <textarea matInput rows="4" [(ngModel)]="pasteText"></textarea>
              </mat-form-field>
              <div class="paste-side">
                <button mat-raised-button color="primary" [disabled]="pasting() || !pasteText.trim()" (click)="paste()">
                  {{ pasting() ? 'Leggo…' : 'Leggi le offerte' }}
                </button>
              </div>
            </div>
            @if (pasteResult(); as p) {
              <div class="paste-result">
                <div><b>{{ p.seller }}</b>@if (p.category) { · {{ p.category }} }: {{ p.offers.length }} offerte abbinate su {{ p.offersRead }}</div>
                @if (sellerBasketOf(p); as sb) {
                  <div class="seller-basket" [class.no]="!sb.ok">
                    @if (sb.ok) {
                      <b>Da {{ p.seller }} ti conviene:</b> {{ sb.count }} {{ sb.count === 1 ? 'prodotto' : 'prodotti' }}
                      ({{ sb.names }}) · {{ eur(sb.items) }} + spedizione {{ eur(sb.shipping) }} = <b>{{ eur(sb.items + sb.shipping) }}</b>
                      · guadagno atteso netto <b class="gain">{{ eur(sb.gain) }}</b> ({{ sb.netPercent.toFixed(0) }}%)
                    } @else {
                      <b>Da {{ p.seller }}, con le tue regole, non conviene un ordine:</b> {{ sb.reason }}
                    }
                    <div class="muted">Regole della schermata: traino ≥ {{ rules.anchorRoiPercent }}%, altri ≥ {{ rules.addOnRoiPercent }}%,
                      netto ≥ {{ rules.minNetPercent }}%, budget {{ eur(budget) }}. Le righe verdi sono i prodotti convenienti uno per uno.</div>
                  </div>
                }
                @if (!p.englishFilter) { <div class="error">Non vedo il filtro Language: English nel testo: le offerte potrebbero essere in altre lingue.</div> }
                <table>
                  <thead><tr><th>Prodotto</th><th class="num">Prezzo</th><th class="num">Spedizione</th><th class="num">Q.tà</th><th class="num">Trend CM</th><th class="num">Resa</th><th>Valutazione</th></tr></thead>
                  <tbody>
                    @for (o of p.offers; track o.offerId) {
                      <tr [class.good]="o.verdict.startsWith('Da aprire') || o.verdict.startsWith('Da tenere:')">
                        <td><a [href]="sellerOfferUrl(p.seller, o.name)" target="_blank" rel="noopener" matTooltip="Questo prodotto fra le offerte di {{ p.seller }}">{{ o.name }}</a>
                          <a [href]="cmSearchUrl(o.name)" target="_blank" rel="noopener" matTooltip="Cerca il prodotto su tutto Cardmarket"><mat-icon class="tiny">search</mat-icon></a>
                          @if (o.comment) { <div class="muted">{{ o.comment }}</div> }</td>
                        <td class="num">{{ eur(o.price) }}</td>
                        <td class="num">{{ eur(o.shipping) }}</td>
                        <td class="num">{{ o.quantity }}</td>
                        <td class="num">{{ eur(o.cmTrend) }}</td>
                        <td class="num">{{ o.roiPercent != null ? o.roiPercent.toFixed(0) + '%' : '—' }}</td>
                        <td class="verdict">{{ o.verdict }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
                @if (p.unmatched.length) {
                  <div class="muted">Non abbinate al catalogo ({{ p.unmatched.length }}): {{ p.unmatched.join(' · ') }}</div>
                }
              </div>
            }
          }
        </mat-card-content>
      </mat-card>

      @if (mode() === 'sellers' && sellerResult(); as sr) {
        <mat-card class="summary">
          <mat-card-content>
            <div class="numbers">
              <div><span class="label">Venditori convenienti</span><span class="big">{{ sr.sellers.length }}</span>
                <span class="label">{{ sr.inBudgetCount }} nel budget</span></div>
              <div><span class="label">Nel budget</span><span class="big">{{ eur(sr.inBudgetTotal) }}</span><span class="label">di {{ eur(sr.request.budget) }}</span></div>
              <div><span class="label">Guadagno atteso netto</span><span class="big gain">{{ eur(sr.inBudgetGain) }}</span></div>
              <div><span class="label">Wizards</span><span class="big">{{ eur(sr.wizardsTotal) }}</span>
                <span class="label">guadagno {{ eur(sr.wizardsGain) }}</span></div>
            </div>
            <div class="muted">{{ sr.products }} prodotti considerati; {{ sr.expansionsRead }} espansioni Card Trader lette
              @if (sr.expansionFailures) { ({{ sr.expansionFailures }} non riuscite) }; {{ sr.cardmarketSellers }} venditori Cardmarket incollati.
              Offerte Card Trader lette il <b [class.stale]="isStale(sr.collectedAt)">{{ sr.collectedAt | date:'dd/MM HH:mm' }}</b>:
              per offerte fresche, "Cerca i venditori".</div>
          </mat-card-content>
        </mat-card>

        @if (choice(sr); as ch) {
          <div class="choice-bar">
            <span><b>La tua scelta:</b> {{ ch.count }} {{ ch.count === 1 ? 'ordine' : 'ordini' }}</span>
            <span>spesa <b [class.over]="ch.total > sr.request.budget">{{ eur(ch.total) }}</b> di {{ eur(sr.request.budget) }}
              @if (ch.total > sr.request.budget) { <span class="over">(oltre il budget di {{ eur(ch.total - sr.request.budget) }})</span> }</span>
            <span>spedizioni {{ eur(ch.shipping) }}</span>
            <span>guadagno atteso netto <b class="gain">{{ eur(ch.gain) }}</b></span>
            @if (ch.duplicates) { <span class="over">{{ ch.duplicates }} prodotti in più di un ordine scelto: li compreresti due volte</span> }
            <button mat-button (click)="clearChoice()">Azzera la scelta</button>
          </div>
        }

        @if (!excludedStores.includes('Wizards') && sr.wizards.length) {
          <mat-card class="basket">
            <mat-card-content>
              <div class="basket-head">
                <div><span class="store">Wizards</span><span class="muted"> · drop acquistabili con verdetto "Compra", il margine migliore per primo</span></div>
                <div class="basket-totals">scelti {{ chosenDrops(sr) }} · totale <b>{{ eur(sr.wizardsTotal) }}</b> · guadagno <b class="gain">{{ eur(sr.wizardsGain) }}</b></div>
              </div>
              <table>
                <thead><tr><th></th><th>Drop</th><th class="num">Prezzo</th><th class="num">Valore netto</th><th class="num">Margine</th><th>Avvisi</th></tr></thead>
                <tbody>
                  @for (w of sr.wizards; track w.drop.wizardsProductId) {
                    <tr [class.good]="w.chosen">
                      <td>@if (w.chosen) { <mat-icon class="tiny">check</mat-icon> }</td>
                      <td><a [href]="w.drop.url" target="_blank" rel="noopener">{{ w.drop.title }}</a></td>
                      <td class="num">{{ eur(w.drop.price) }}</td>
                      <td class="num">{{ eur(w.drop.netValue) }}</td>
                      <td class="num">{{ w.drop.marginPercent != null ? w.drop.marginPercent.toFixed(0) + '%' : '—' }}</td>
                      <td class="flags">@for (f of w.drop.flags; track f) { <div>{{ f }}</div> }</td>
                    </tr>
                  }
                </tbody>
              </table>
            </mat-card-content>
          </mat-card>
        }

        @for (b of visibleSellers(sr); track b.key) {
          <mat-card class="basket" [class.in-budget]="b.inBudget">
            <mat-card-content>
              <div class="basket-head">
                <div>
                  <mat-checkbox class="choose" [checked]="chosen().has(b.key)" (change)="toggleChoice(b.key, $event.checked)"
                    matTooltip="Scelgo questo ordine: in cima vedi il totale della tua combinazione">Scelgo questo</mat-checkbox>
                  @if (b.inBudget) { <span class="badge-in">nel budget</span> }
                  <span class="store">{{ b.store }}</span> <span class="seller">· {{ b.seller }}</span>
                  @if (b.country) { <span class="muted"> ({{ b.country }})</span> }
                  @if (b.store === 'Cardmarket') {
                    <a class="muted" [href]="sellerUrl(b.seller)" target="_blank" rel="noopener"> · offerte del venditore</a>
                    @if (pastedAt(b); as at) { <span class="muted" [class.stale]="isStale(at)"> · incollato il {{ at | date:'dd/MM' }}</span> }
                  }
                </div>
                <div class="basket-totals">
                  aperture {{ eur(b.openCost) }}@if (b.holdCost) { · da tenere {{ eur(b.holdCost) }} } · spedizione {{ eur(b.shipping) }} ·
                  totale <b>{{ eur(b.total) }}</b> · guadagno netto <b class="gain">{{ eur(b.openGain) }}</b> ({{ b.netPercent.toFixed(0) }}%)
                </div>
              </div>
              <table>
                <thead><tr><th>Tipo</th><th>Prodotto</th>@if (b.key === 'ctzero') { <th>Venditore</th> }<th class="num">Prezzo</th>
                  <th class="num">Trend CM</th><th class="num">Resa / sul trend</th><th class="num">Guadagno / punteggio</th></tr></thead>
                <tbody>
                  @for (l of b.lines; track l.offer.productId + l.offer.seller) {
                    <tr>
                      <td><span [class]="'kind ' + (l.kind === 'Open' ? 'Open' : 'Hold')">{{ l.kind === 'Open' ? 'Apri' : 'Tieni' }}</span></td>
                      <td><a [href]="sellerLineUrl(b, l)" target="_blank" rel="noopener">{{ l.product.name }}</a>
                        @if (b.store === 'Cardmarket') {
                          <a [href]="cmSearchUrl(l.product.cardmarketName ?? l.product.name)" target="_blank" rel="noopener" matTooltip="Cerca il prodotto su tutto Cardmarket"><mat-icon class="tiny">search</mat-icon></a>
                        }
                        <span class="muted"> · {{ l.product.setCode }}</span>
                        <button class="reg-line" matTooltip="Registra solo questo prodotto nel registro acquisti" (click)="registerSeller(b, $index)">
                          <mat-icon>post_add</mat-icon></button>
                        @if (alsoIn(sr, b, l); as others) { <div class="also">anche in: {{ others }}</div> }</td>
                      @if (b.key === 'ctzero') { <td>{{ l.offer.seller }}@if (l.offer.country) { ({{ l.offer.country }}) }</td> }
                      <td class="num">{{ eur(l.offer.price) }}</td>
                      <td class="num">{{ eur(l.product.cmTrend) }}</td>
                      <td class="num" [matTooltip]="l.kind === 'Hold' ? 'Prezzo rispetto al trend Cardmarket: per un prodotto da tenere conta comprarlo vicino al suo valore di mercato' : 'Resa dell’apertura al prezzo di questo venditore'">
                        {{ l.kind === 'Open' ? (l.roiPercent != null ? l.roiPercent.toFixed(0) + '%' : '—') : vsTrend(l) }}</td>
                      <td class="num" [class.gain]="l.kind === 'Open'">{{ l.kind === 'Open' ? eur(l.gain) : (l.product.holdScore?.toFixed(0) + ' punti') }}</td>
                    </tr>
                  }
                </tbody>
              </table>
              <div class="actions">
                @if (b.store !== 'Cardmarket') {
                  <button mat-stroked-button color="primary" [disabled]="addingCart() === b.key" (click)="sellerAddToCart(b)">
                    <mat-icon>add_shopping_cart</mat-icon> {{ addingCart() === b.key ? 'Aggiungo…' : 'Metti nel carrello Card Trader' }}
                  </button>
                  <a mat-button href="https://www.cardtrader.com/cart/edit" target="_blank" rel="noopener">Apri il carrello</a>
                }
                <button mat-stroked-button (click)="registerSeller(b)"
                  matTooltip="Dopo l'acquisto: registra l'ordine nel registro acquisti, con prezzi, spedizione e Tag">
                  <mat-icon>post_add</mat-icon> Registra l'acquisto
                </button>
              </div>
            </mat-card-content>
          </mat-card>
        }
        @if (sr.sellers.length > visibleSellers(sr).length) {
          <button mat-button (click)="showAllSellers.set(true)">Mostra tutti i {{ sr.sellers.length }} venditori</button>
        }
        @if (sr.sellers.length === 0) {
          <mat-card><mat-card-content>Nessun venditore con un carrello conveniente secondo queste regole. Prova ad abbassare le soglie,
            o incolla le pagine di qualche venditore Cardmarket.</mat-card-content></mat-card>
        }
      }

      @if (mode() === 'plan' && result(); as r) {
        <mat-card class="summary">
          <mat-card-content>
            <div class="numbers">
              <div><span class="label">Speso</span><span class="big">{{ eur(r.spent) }}</span><span class="label">di {{ eur(r.budget) }}</span></div>
              <div><span class="label">Guadagno atteso aperture</span><span class="big gain">{{ eur(r.openGain) }}</span></div>
              <div><span class="label">Da tenere</span><span class="big">{{ eur(r.holdSpent) }}</span>
                <span class="label">carte per {{ eur(r.holdFloor) }}</span></div>
              <div><span class="label">Spedizioni</span><span class="big">{{ eur(r.shipping) }}</span></div>
              <div><span class="label">Rimasto</span><span class="big">{{ eur(r.remaining) }}</span></div>
            </div>
            @if (r.remainingReason) { <div class="muted">{{ r.remainingReason }}</div> }
            <div class="muted">
              Candidati di oggi: {{ r.openCandidates }} da aprire, {{ r.holdCandidates }} da tenere; offerte lette per
              {{ r.openRead }} e {{ r.holdRead }} (i migliori). Offerte lette alle {{ r.collectedAt | date:'HH:mm' }}.
              @if (r.excluded.length) {
                {{ r.excluded.length }} righe tolte · <a href="" (click)="$event.preventDefault(); restoreAll()">rimettile</a>
              }
            </div>
          </mat-card-content>
        </mat-card>

        @for (b of r.baskets; track b.key) {
          <mat-card class="basket">
            <mat-card-content>
              <div class="basket-head">
                <div>
                  <span class="store">{{ b.store }}</span>
                  @if (b.seller) { <span class="seller">· {{ b.seller }}</span> }
                  @if (b.country) { <span class="muted"> ({{ b.country }})</span> }
                  @if (b.store === 'Cardmarket' && !b.seller) { <span class="muted"> · venditore da cercare, al trend (o al prezzo più basso, se più alto) come nella classifica</span> }
                  @if (b.store === 'Cardmarket' && b.seller) { <a class="muted" [href]="sellerUrl(b.seller)" target="_blank" rel="noopener"> · offerte del venditore</a> }
                </div>
                <div class="basket-totals">
                  {{ b.lines.length }} {{ b.lines.length === 1 ? 'prodotto' : 'prodotti' }} · spedizione {{ eur(b.shipping) }} ·
                  totale <b>{{ eur(b.total) }}</b>
                  @if (hasGain(b)) { · guadagno <b class="gain">{{ eur(b.gain) }}</b> }
                </div>
              </div>
              <table>
                <thead>
                  <tr><th>Tipo</th><th>Prodotto</th><th class="num">Q.tà</th><th class="num">Prezzo</th>
                    <th class="num">Valore / punteggio</th><th class="num">Guadagno</th><th>Avvisi</th><th></th></tr>
                </thead>
                <tbody>
                  @for (l of b.lines; track l.offerKey) {
                    <tr>
                      <td><span [class]="'kind ' + l.info.kind">{{ kindLabel(l) }}</span></td>
                      <td>
                        <a [href]="productUrl(b, l)" target="_blank" rel="noopener">{{ l.info.name }}</a>
                        @if (b.store === 'Cardmarket' && b.seller) {
                          <a [href]="cmSearchUrl(l.info.cardmarketName ?? l.info.name)" target="_blank" rel="noopener" matTooltip="Cerca il prodotto su tutto Cardmarket"><mat-icon class="tiny">search</mat-icon></a>
                        }
                        @if (l.info.setCode) { <span class="muted"> · {{ l.info.setCode }}</span> }
                        @if (l.info.productId && b.store !== 'Wizards') {
                          <button class="reg-line" matTooltip="Registra solo questo prodotto nel registro acquisti" (click)="registerPlan(b, l.offerKey)">
                            <mat-icon>post_add</mat-icon></button>
                        }
                      </td>
                      <td class="num">{{ l.quantity }}</td>
                      <td class="num">{{ eur(l.unitPrice) }}</td>
                      <td class="num" [matTooltip]="valueTooltip(l)">{{ valueLabel(l) }}</td>
                      <td class="num gain">{{ l.gain != null ? eur(l.gain) : '—' }}</td>
                      <td class="flags">@for (f of l.info.flags; track f) { <div>{{ f }}</div> }</td>
                      <td><button mat-icon-button matTooltip="Togli dal piano e ricalcola" (click)="exclude(l)"><mat-icon>close</mat-icon></button></td>
                    </tr>
                  }
                </tbody>
              </table>
              <div class="actions">
                @if (b.store === 'Card Trader' || b.store === 'Card Trader Zero') {
                  <button mat-stroked-button color="primary" [disabled]="addingCart() === b.key" (click)="addToCart(b)">
                    <mat-icon>add_shopping_cart</mat-icon> {{ addingCart() === b.key ? 'Aggiungo…' : 'Metti nel carrello Card Trader' }}
                  </button>
                  <a mat-button href="https://www.cardtrader.com/cart/edit" target="_blank" rel="noopener">Apri il carrello</a>
                  @if (hasMultipleCopies(b)) { <span class="muted">Il carrello prende una copia per offerta: le altre aggiungile sul sito.</span> }
                }
                @if (b.store !== 'Wizards') {
                  <button mat-stroked-button (click)="registerPlan(b)"
                    matTooltip="Dopo l'acquisto: registra l'ordine nel registro acquisti, con prezzi, spedizione e Tag">
                    <mat-icon>post_add</mat-icon> Registra l'acquisto
                  </button>
                }
              </div>
            </mat-card-content>
          </mat-card>
        }
        @if (r.baskets.length === 0) {
          <mat-card><mat-card-content>Nessun acquisto conveniente con questo budget oggi.</mat-card-content></mat-card>
        }
      }
    </div>
  `,
  styles: [`
    .container { display: flex; flex-direction: column; gap: 12px; padding: 16px 0; }
    .toolbar { display: flex; align-items: center; gap: 16px; flex-wrap: wrap; }
    .budget { width: 140px; }
    .share { display: flex; flex-direction: column; min-width: 220px; }
    .share-label { font-size: 13px; }
    .spacer { flex: 1; }
    .stores { display: flex; align-items: center; gap: 4px; }
    .shipping { display: flex; gap: 12px; flex-wrap: wrap; align-items: center; }
    .shipping mat-form-field { width: 210px; }
    .progress { display: flex; flex-direction: column; gap: 6px; font-size: 13px; margin-top: 8px; }
    .error { color: #c62828; background: #ffebee; padding: 8px; border-radius: 4px; margin-top: 8px; }
    .numbers { display: flex; gap: 32px; flex-wrap: wrap; }
    .numbers > div { display: flex; flex-direction: column; }
    .label { font-size: 12px; color: #666; }
    .big { font-size: 22px; font-weight: 500; }
    .gain { color: #2e7d32; }
    .muted { color: #777; font-size: 12px; margin-top: 4px; }
    .basket-head { display: flex; justify-content: space-between; flex-wrap: wrap; gap: 8px; margin-bottom: 8px; }
    .store { font-weight: 500; font-size: 16px; }
    .seller { font-size: 15px; }
    .basket-totals { font-size: 14px; }
    table { width: 100%; border-collapse: collapse; font-size: 13px; }
    th { text-align: left; font-weight: 500; color: #555; border-bottom: 1px solid #ddd; padding: 4px 6px; }
    td { border-bottom: 1px solid #f0f0f0; padding: 4px 6px; vertical-align: top; }
    .num { text-align: right; white-space: nowrap; }
    .flags { font-size: 12px; color: #e65100; }
    .kind { font-size: 11px; padding: 2px 6px; border-radius: 3px; white-space: nowrap; }
    .kind.Open { background: #e8f5e9; color: #2e7d32; }
    .kind.Hold { background: #e3f2fd; color: #1565c0; }
    .kind.SecretLair { background: #f3e5f5; color: #6a1b9a; }
    .actions { display: flex; gap: 8px; align-items: center; margin-top: 8px; flex-wrap: wrap; }
    .modes { display: flex; gap: 8px; }
    .modes button.active { background: #e8eaf6; color: #283593; font-weight: 600; }
    .small { width: 110px; }
    .basket.in-budget { border-left: 4px solid #43a047; }
    .stale { color: #e65100; font-weight: 600; }
    .badge-in { background: #43a047; color: #fff; font-size: 11px; border-radius: 3px; padding: 2px 6px; margin-right: 6px; }
    .sellers-head { display: flex; align-items: center; gap: 6px; cursor: pointer; }
    .paste { display: flex; gap: 12px; flex-wrap: wrap; align-items: flex-start; }
    .paste-text { flex: 1 1 500px; }
    .paste-side { display: flex; flex-direction: column; gap: 4px; }
    .paste-result { margin: 4px 0 8px; display: flex; flex-direction: column; gap: 6px; }
    .seller-basket.no { background: #fff3e0; border-left-color: #fb8c00; }
    .also { color: #e65100; font-size: 11px; }
    .reg-line { background: none; border: none; padding: 0 2px; cursor: pointer; color: #3f51b5; vertical-align: middle; line-height: 1; }
    .reg-line mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .choose { margin-right: 6px; }
    .choice-bar { background: #e8eaf6; border-radius: 6px; padding: 8px 12px; display: flex; gap: 24px; flex-wrap: wrap; align-items: baseline; }
    .over { color: #c62828; font-weight: 600; }
    .seller-basket { background: #e8f5e9; border-left: 4px solid #43a047; padding: 8px 12px; border-radius: 4px; font-size: 14px; }
    tr.good td { background: #dcedc8; }
    tr.good td:first-child { box-shadow: inset 4px 0 0 #43a047; }
    tr.good .verdict { color: #1b5e20; font-weight: 600; }
    .sellers { display: flex; gap: 8px; flex-wrap: wrap; }
    .open-seller { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .seller-chip a.current { font-weight: 600; }
    .tiny { font-size: 14px; height: 14px; width: 14px; vertical-align: middle; }
    .seller-chip { display: inline-flex; align-items: center; gap: 4px; background: #f5f5f5; border-radius: 16px; padding: 0 0 0 12px; font-size: 13px; }
  `]
})
export class BudgetPlanTabComponent implements OnInit, OnDestroy {
  private purchasing = inject(PurchasingService);
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);

  budget = 300;
  openShare = 70;
  includeBorderline = false;
  shipping: BudgetShipping | null = null;
  excludedStores: string[] = [];
  readonly storeOptions = [
    { key: 'CardTrader', label: 'Card Trader' },
    { key: 'Cardmarket', label: 'Cardmarket' },
    { key: 'Wizards', label: 'Wizards' }
  ];
  showShipping = signal(false);

  planId = signal<string | null>(null);
  status = signal<BudgetPlanStatus | null>(null);
  error = signal<string | null>(null);
  addingCart = signal<string | null>(null);
  running = computed(() => this.status()?.state === 'running');
  result = computed<BudgetPlanResult | null>(() => this.status()?.result ?? null);
  private poll?: ReturnType<typeof setInterval>;

  // --- Per venditore ---
  mode = signal<'sellers' | 'plan'>((localStorage.getItem(MODE_KEY) as 'sellers' | 'plan') || 'sellers');
  wizardsBudget = 500;
  rules: SellerRules = { anchorRoiPercent: 50, addOnRoiPercent: 33, minNetPercent: 20, maxHoldPremiumPercent: 10 };
  sellerStatus = signal<SellerPlanStatus | null>(null);
  sellerRunning = computed(() => this.sellerStatus()?.state === 'running');
  sellerResult = computed<SellerPlanResult | null>(() => this.sellerStatus()?.result ?? null);
  showAllSellers = signal(false);
  private sellerPoll?: ReturnType<typeof setInterval>;

  setMode(mode: 'sellers' | 'plan'): void {
    this.mode.set(mode);
    localStorage.setItem(MODE_KEY, mode);
  }

  private sellerRequest(): SellerPlanRequest {
    const request: SellerPlanRequest = {
      budget: this.budget, wizardsBudget: this.wizardsBudget, rules: this.rules,
      shipping: this.shipping ?? { cardTrader: 7, cardTraderZero: 8, cardmarket: 7, wizards: 10 },
      excludedStores: this.excludedStores
    };
    localStorage.setItem(SELLER_REQUEST_KEY, JSON.stringify(request));
    return request;
  }

  startSellers(): void {
    this.error.set(null);
    this.showAllSellers.set(false);
    this.purchasing.startSellerPlan(this.sellerRequest()).subscribe({
      next: s => this.followSellers(s.id, s),
      error: err => this.error.set(err?.error?.message || 'Non riesco ad avviare la ricerca dei venditori')
    });
  }

  recomputeSellers(): void {
    const id = this.sellerStatus()?.id;
    if (!id) return;
    this.purchasing.recomputeSellerPlan(id, this.sellerRequest()).subscribe({
      next: s => this.sellerStatus.set(s),
      error: err => this.error.set(err?.error?.message || 'Ricalcolo non riuscito')
    });
  }

  private followSellers(id: string, first?: SellerPlanStatus): void {
    if (first) this.sellerStatus.set(first);
    clearInterval(this.sellerPoll);
    const check = () => this.purchasing.getSellerPlan(id).subscribe({
      next: s => {
        this.sellerStatus.set(s);
        if (s.state !== 'running') clearInterval(this.sellerPoll);
        if (s.state === 'failed') this.error.set(s.error ?? 'Lettura non riuscita');
      },
      error: () => clearInterval(this.sellerPoll)
    });
    check();

    this.sellerPoll = setInterval(check, 3000);
  }

  /** Incollato più di 7 giorni fa: prezzi e disponibilità del venditore vanno ricontrollati. */
  isStale(at?: string | null): boolean {
    return !!at && Date.now() - new Date(at).getTime() > 7 * 24 * 3600 * 1000;
  }

  /** Data dell'incolla più vecchia fra le righe di un venditore Cardmarket. */
  pastedAt(b: SellerBasket): string | null {
    const dates = b.lines.map(l => l.offer.pastedAt).filter((d): d is string => !!d).sort();
    return dates[0] ?? null;
  }

  /** Prezzo di un prodotto da tenere rispetto al trend Cardmarket, la misura che conta per comprarlo. */
  vsTrend(l: SellerBasketLine): string {
    const trend = l.product.cmTrend;
    if (!trend) return '—';
    const diff = Math.round((l.offer.price - trend) / trend * 100);
    return diff === 0 ? 'al trend' : diff < 0 ? `${-diff}% sotto` : `${diff}% sopra`;
  }

  // --- Scelta degli ordini ---
  chosen = signal(new Set<string>());

  toggleChoice(key: string, on: boolean): void {
    this.chosen.update(s => {
      const n = new Set(s);
      if (on) n.add(key); else n.delete(key);
      return n;
    });
  }

  clearChoice(): void {
    this.chosen.set(new Set());
  }

  /** Totali degli ordini scelti; null se non ne è scelto nessuno. */
  choice(sr: SellerPlanResult) {
    const picked = sr.sellers.filter(b => this.chosen().has(b.key));
    if (!picked.length) return null;
    const seen = new Map<number, number>();
    for (const b of picked) for (const l of b.lines) seen.set(l.product.productId, (seen.get(l.product.productId) ?? 0) + 1);
    return {
      count: picked.length,
      total: picked.reduce((t, b) => t + b.total, 0),
      shipping: picked.reduce((t, b) => t + b.shipping, 0),
      gain: picked.reduce((t, b) => t + b.openGain, 0),
      duplicates: [...seen.values()].filter(n => n > 1).length
    };
  }

  /** Gli altri ordini della lista in cui c'è lo stesso prodotto: facendoli tutti lo si comprerebbe più volte. */
  alsoIn(sr: SellerPlanResult, b: SellerBasket, l: SellerBasketLine): string | null {
    const others = sr.sellers
      .filter(x => x.key !== b.key && x.lines.some(y => y.product.productId === l.product.productId))
      .map(x => x.key === 'ctzero' ? 'Card Trader Zero' : x.seller);
    if (!others.length) return null;
    return others.length > 3 ? `${others.slice(0, 3).join(', ')} e altri ${others.length - 3}` : others.join(', ');
  }

  visibleSellers(sr: SellerPlanResult): SellerBasket[] {
    return this.showAllSellers() ? sr.sellers : sr.sellers.slice(0, 25);
  }

  chosenDrops(sr: SellerPlanResult): number {
    return sr.wizards.filter(w => w.chosen).length;
  }

  sellerLineUrl(b: SellerBasket, l: SellerBasketLine): string {
    if (b.store === 'Cardmarket') return this.sellerOfferUrl(b.seller, l.product.cardmarketName ?? l.product.name);
    return l.product.cardTraderBlueprintId ? 'https://www.cardtrader.com/cards/' + l.product.cardTraderBlueprintId : '#';
  }

  /** Registra nel registro acquisti l'ordine di un venditore, o solo la riga `index` (le altre restano da spuntare). */
  registerSeller(b: SellerBasket, index?: number): void {
    const lines: RegisterOrderLine[] = b.lines.map(l => ({
      sealedProductId: l.product.productId, name: l.product.name, quantity: 1, unitPrice: l.offer.price,
      store: b.store, seller: l.offer.seller, hold: l.kind === 'Hold'
    }));
    openRegisterPurchase(this.dialog, {
      title: b.key === 'ctzero' ? b.store : `${b.store} · ${b.seller}`,
      shipping: b.shipping, lines, checked: index != null ? [index] : undefined
    });
  }

  /** Come {@link registerSeller} per un carrello del piano automatico; le righe senza prodotto di catalogo restano fuori. */
  registerPlan(b: BudgetPlanBasket, offerKey?: string): void {
    const planLines = b.lines.filter(l => l.info.productId);
    const lines: RegisterOrderLine[] = planLines.map(l => ({
      sealedProductId: l.info.productId!, name: l.info.name, quantity: l.quantity, unitPrice: l.unitPrice,
      store: b.store, seller: l.info.seller ?? b.seller, hold: l.info.kind === 'Hold'
    }));
    const index = offerKey ? planLines.findIndex(l => l.offerKey === offerKey) : -1;
    openRegisterPurchase(this.dialog, {
      title: b.seller ? `${b.store} · ${b.seller}` : b.store,
      shipping: b.shipping, lines, checked: index >= 0 ? [index] : undefined
    });
  }

  sellerAddToCart(b: SellerBasket): void {
    const items: PlanCartItem[] = b.lines
      .filter(l => l.offer.cardTraderProductId)
      .map(l => ({ cardTraderProductId: l.offer.cardTraderProductId!, viaCardTraderZero: b.key === 'ctzero', productName: l.product.name }));
    if (items.length === 0) return;
    this.addingCart.set(b.key);
    this.purchasing.addPlanToCart(items).subscribe({
      next: res => {
        this.addingCart.set(null);
        const parts = [`${res.added} aggiunte`];
        if (res.alreadyInCart) parts.push(`${res.alreadyInCart} già nel carrello`);
        if (res.failed.length) parts.push(`${res.failed.length} rifiutate (vendute o cambiate)`);
        this.snackBar.open('Carrello Card Trader: ' + parts.join(', '), 'OK', { duration: 6000 });
      },
      error: err => {
        this.addingCart.set(null);
        this.snackBar.open(err?.error?.message || 'Carrello non aggiornato', 'OK', { duration: 6000 });
      }
    });
  }

  // --- Venditori Cardmarket incollati (Fase 1) ---
  showSellers = signal(false);
  sellers = signal<PastedSeller[]>([]);
  pasteText = '';
  pasteSeller = '';
  pasting = signal(false);
  pasteResult = signal<SellerPasteResult | null>(null);

  /**
   * Il carrello consigliato del solo venditore incollato: un'unità di ogni offerta da aprire o da tenere, una
   * spedizione (quella impostata per Cardmarket: il testo della pagina non la contiene).
   */
  /**
   * Il carrello consigliato del solo venditore incollato, con le stesse regole della lista "Per venditore" (quelle della
   * schermata): un traino, le altre aperture sopra la loro soglia, i tenuti a buon prezzo, dentro il budget; conviene se
   * il guadagno netto delle aperture è almeno il "netto" della loro spesa. Se no, dice perché.
   */
  sellerBasketOf(p: SellerPasteResult) {
    const r = this.rules;
    const ship = (o: { shipping?: number }) => o.shipping ?? this.shipping?.cardmarket ?? 0;
    const opens = p.offers
      .filter(o => o.verdict.startsWith('Da aprire') && (o.roiPercent ?? 0) >= r.addOnRoiPercent)
      .sort((x, y) => (y.roiPercent ?? 0) - (x.roiPercent ?? 0));
    const holds = p.offers
      .filter(o => o.verdict.startsWith('Da tenere:'))
      .sort((x, y) => (y.holdScore ?? 0) - (x.holdScore ?? 0));
    const best = Math.max(0, ...p.offers.filter(o => o.verdict.startsWith('Da aprire')).map(o => o.roiPercent ?? 0));
    const anchor = opens.find(o => (o.roiPercent ?? 0) >= r.anchorRoiPercent && o.price + ship(o) <= this.budget);
    if (!anchor) {
      return { ok: false, reason: best >= r.anchorRoiPercent
        ? `il traino da solo supera il budget di ${this.eur(this.budget)}.`
        : `manca un traino: l'apertura che rende di più rende il ${best.toFixed(0)}%, la soglia è il ${r.anchorRoiPercent}%.`,
        count: 0, names: '', items: 0, shipping: 0, gain: 0, netPercent: 0 };
    }
    const chosen = [anchor];
    let items = anchor.price;
    let shipping = ship(anchor);
    for (const o of [...opens.filter(x => x !== anchor), ...holds]) {
      const withShipping = Math.max(shipping, ship(o));
      if (items + o.price + withShipping > this.budget) continue;
      chosen.push(o);
      items += o.price;
      shipping = withShipping;
    }
    const opened = chosen.filter(o => o.verdict.startsWith('Da aprire'));
    const openCost = opened.reduce((t, o) => t + o.price, 0);
    const gain = opened.reduce((t, o) => t + ((o.openValueNet ?? o.price) - o.price), 0) - shipping;
    const netPercent = openCost > 0 ? gain / openCost * 100 : 0;
    const ok = gain >= openCost * r.minNetPercent / 100;
    return {
      ok,
      reason: `il guadagno netto delle aperture, tolta la spedizione, è ${this.eur(gain)}: il ${netPercent.toFixed(0)}% della spesa, sotto il ${r.minNetPercent}%.`,
      count: chosen.length,
      names: chosen.map(o => o.name.replace(/^Commander:\s*/, '').replace(/ Commander Deck$/, '')).join(', '),
      items, shipping, gain, netPercent
    };
  }

  ngOnInit(): void {
    this.loadSellers();
    const savedSellers = localStorage.getItem(SELLER_REQUEST_KEY);
    if (savedSellers) {
      try {
        const r = JSON.parse(savedSellers);
        this.wizardsBudget = r.wizardsBudget ?? this.wizardsBudget;
        this.rules = r.rules ?? this.rules;
      } catch { /* richiesta salvata illeggibile: valori predefiniti */ }
    } else {
      this.purchasing.getSellerPlanSettings().subscribe({ next: r => this.rules = r, error: () => { } });
    }
    // L'ultima lettura per venditore, se il servizio l'ha ancora: si riapre senza rileggere Card Trader.
    this.purchasing.getLatestSellerPlan().subscribe({ next: s => { if (!this.sellerStatus()) this.followSellers(s.id, s); }, error: () => { } });
    const saved = localStorage.getItem(LAST_REQUEST_KEY);
    if (saved) {
      try {
        const r = JSON.parse(saved);
        this.budget = r.budget ?? this.budget;
        this.openShare = r.openSharePercent ?? this.openShare;
        this.includeBorderline = !!r.includeSecretLairBorderline;
        this.shipping = r.shipping ?? null;
        this.excludedStores = r.excludedStores ?? [];
      } catch { /* richiesta salvata illeggibile: si riparte dai predefiniti */ }
    }
    if (!this.shipping) {
      this.purchasing.getBudgetPlanSettings().subscribe({ next: s => this.shipping = s, error: () => { } });
    }
    // Il piano dell'ultima volta, se il servizio lo ha ancora: cambiando scheda non si perde.
    const lastId = sessionStorage.getItem(LAST_PLAN_KEY);
    if (lastId) this.follow(lastId);
  }

  ngOnDestroy(): void {
    clearInterval(this.poll);
    clearInterval(this.sellerPoll);
  }

  start(): void {
    this.error.set(null);
    const request = {
      budget: this.budget,
      openSharePercent: this.openShare,
      includeSecretLairBorderline: this.includeBorderline,
      shipping: this.shipping ?? undefined,
      excludedStores: this.excludedStores
    };
    localStorage.setItem(LAST_REQUEST_KEY, JSON.stringify(request));
    this.purchasing.startBudgetPlan(request).subscribe({
      next: s => this.follow(s.id, s),
      error: err => this.error.set(err?.error?.message || 'Non riesco ad avviare il calcolo. Riprova.')
    });
  }

  private follow(id: string, first?: BudgetPlanStatus): void {
    this.planId.set(id);
    sessionStorage.setItem(LAST_PLAN_KEY, id);
    if (first) this.status.set(first);
    clearInterval(this.poll);
    const check = () => this.purchasing.getBudgetPlan(id).subscribe({
      next: s => {
        this.status.set(s);
        if (s.state !== 'running') clearInterval(this.poll);
        if (s.state === 'failed') this.error.set(s.error ?? 'Calcolo non riuscito');
      },
      error: () => {
        clearInterval(this.poll);
        sessionStorage.removeItem(LAST_PLAN_KEY);
        this.planId.set(null);
        if (first) this.error.set('Piano non trovato: ricalcolalo');
      }
    });
    check();
    this.poll = setInterval(check, 2000);
  }

  /** Categorie delle offerte di un venditore con prodotti sigillati (indirizzi delle pagine di Cardmarket). */
  readonly sellerCategories = [
    { slug: 'Preconstructed-Decks', label: 'Mazzi' },
    { slug: 'Sets', label: 'Set' },
    { slug: 'Booster-Boxes', label: 'Box di buste' },
    { slug: 'Boosters', label: 'Buste' },
    { slug: '', label: 'Tutte le categorie' }
  ];

  loadSellers(): void {
    this.purchasing.getCardmarketSellers().subscribe({
      next: s => {
        this.sellers.set(s);
        // Tornando sulla scheda si riapre l'ultimo venditore guardato, se le sue offerte valgono ancora.
        const last = localStorage.getItem(LAST_SELLER_KEY);
        if (!this.pasteResult() && last && s.some(x => x.seller === last)) this.showSeller(last);
      },
      error: () => { }
    });
  }

  showSeller(seller: string): void {
    this.purchasing.getCardmarketSeller(seller).subscribe({
      next: r => {
        this.pasteResult.set(r);
        this.showSellers.set(true);
        localStorage.setItem(LAST_SELLER_KEY, seller);
      },
      error: err => this.error.set(err?.error?.message || 'Non riesco a leggere le offerte del venditore')
    });
  }

  /** Pagina delle offerte del venditore su Cardmarket, in inglese e per nome: è quella da copiare. */
  openSellerPage(slug: string): void {
    const seller = this.pasteSeller.trim();
    if (!seller) return;
    const base = `https://www.cardmarket.com/en/Magic/Users/${encodeURIComponent(seller)}/Offers`;
    window.open(slug ? `${base}/${slug}?idLanguages=1&sortBy=name_asc` : base, '_blank', 'noopener');
  }

  paste(): void {
    this.pasting.set(true);
    this.error.set(null);
    this.purchasing.pasteCardmarketSeller(this.pasteText, this.pasteSeller.trim() || undefined).subscribe({
      next: r => {
        this.pasting.set(false);
        this.pasteResult.set(r);
        this.pasteText = '';
        this.pasteSeller = r.seller;
        localStorage.setItem(LAST_SELLER_KEY, r.seller);
        this.loadSellers();
      },
      error: err => {
        this.pasting.set(false);
        this.error.set(err?.error?.message || 'Non riesco a leggere il testo incollato');
      }
    });
  }

  deleteSeller(s: PastedSeller): void {
    this.purchasing.deleteCardmarketSeller(s.seller).subscribe({
      next: () => {
        if (this.pasteResult()?.seller === s.seller) this.pasteResult.set(null);
        this.loadSellers();
      },
      error: () => { }
    });
  }

  sellerUrl(seller: string): string {
    return `https://www.cardmarket.com/en/Magic/Users/${encodeURIComponent(seller)}/Offers`;
  }

  /** Le offerte del venditore che corrispondono al nome del prodotto (la ricerca della sua pagina). */
  sellerOfferUrl(seller: string, name: string): string {
    return `${this.sellerUrl(seller)}/Search?search=${encodeURIComponent(name).replace(/%20/g, '+')}`;
  }

  /** Il prodotto cercato su tutto Cardmarket, fra tutti i venditori. */
  cmSearchUrl(name: string): string {
    return 'https://www.cardmarket.com/en/Magic/Products/Search?searchString=' + encodeURIComponent(name);
  }

  /** Un negozio acceso o spento: con un piano già calcolato si ricalcola subito, senza rileggere Card Trader. */
  toggleStore(key: string, on: boolean): void {
    this.excludedStores = on ? this.excludedStores.filter(s => s !== key) : [...this.excludedStores, key];
    if (this.mode() === 'plan' && this.result()) this.recompute();
    if (this.mode() === 'sellers' && this.sellerResult()) this.recomputeSellers();
  }

  exclude(line: BudgetPlanLine): void {
    const r = this.result();
    if (!r) return;
    this.recompute([...r.excluded, line.offerKey]);
  }

  restoreAll(): void {
    this.recompute([]);
  }

  recompute(excluded?: string[]): void {
    const id = this.planId();
    const r = this.result();
    if (!id || !r || !this.shipping) return;
    localStorage.setItem(LAST_REQUEST_KEY, JSON.stringify({
      budget: r.budget, openSharePercent: r.openSharePercent, includeSecretLairBorderline: this.includeBorderline, shipping: this.shipping,
      excludedStores: this.excludedStores
    }));
    this.purchasing.recomputeBudgetPlan(id, this.shipping, excluded ?? r.excluded, this.excludedStores).subscribe({
      next: s => this.status.set(s),
      error: err => this.error.set(err?.error?.message || 'Ricalcolo non riuscito')
    });
  }

  addToCart(b: BudgetPlanBasket): void {
    const items: PlanCartItem[] = b.lines
      .filter(l => l.info.cardTraderProductId)
      .map(l => ({ cardTraderProductId: l.info.cardTraderProductId!, viaCardTraderZero: b.key === 'ctzero', productName: l.info.name }));
    if (items.length === 0) return;
    this.addingCart.set(b.key);
    this.purchasing.addPlanToCart(items).subscribe({
      next: res => {
        this.addingCart.set(null);
        const parts = [`${res.added} aggiunte`];
        if (res.alreadyInCart) parts.push(`${res.alreadyInCart} già nel carrello`);
        if (res.failed.length) parts.push(`${res.failed.length} rifiutate (vendute o cambiate)`);
        this.snackBar.open('Carrello Card Trader: ' + parts.join(', '), 'OK', { duration: 6000 });
      },
      error: err => {
        this.addingCart.set(null);
        this.snackBar.open(err?.error?.message || 'Carrello non aggiornato', 'OK', { duration: 6000 });
      }
    });
  }

  hasGain(b: BudgetPlanBasket): boolean {
    return b.lines.some(l => l.gain != null);
  }

  hasMultipleCopies(b: BudgetPlanBasket): boolean {
    return b.lines.some(l => l.quantity > 1);
  }

  kindLabel(l: BudgetPlanLine): string {
    return l.info.kind === 'Open' ? 'Apri' : l.info.kind === 'Hold' ? 'Tieni' : 'Secret Lair';
  }

  valueLabel(l: BudgetPlanLine): string {
    if (l.info.kind === 'Hold') return `${l.info.holdScore?.toFixed(0) ?? '—'} punti`;
    return this.eur(l.info.openValueNet);
  }

  valueTooltip(l: BudgetPlanLine): string {
    if (l.info.kind === 'Hold') {
      return `Punteggio "da tenere"; le carte valgono ${this.eur(l.info.openValueNet)} (pavimento ${l.info.floorPercent?.toFixed(0) ?? '—'}%)`;
    }
    if (l.info.kind === 'SecretLair') return `Valore netto stimato del drop; verdetto ${l.info.verdict}, margine ${l.info.roiPercent?.toFixed(0) ?? '—'}%`;
    return `Valore atteso netto dell'apertura; resa in classifica ${l.info.roiPercent?.toFixed(0) ?? '—'}%`;
  }

  productUrl(b: BudgetPlanBasket, l: BudgetPlanLine): string {
    if (l.info.wizardsUrl) return l.info.wizardsUrl;
    if (b.store === 'Cardmarket') {
      const name = l.info.cardmarketName ?? l.info.name;
      return b.seller ? this.sellerOfferUrl(b.seller, name) : this.cmSearchUrl(name);
    }
    return l.info.cardTraderBlueprintId ? 'https://www.cardtrader.com/cards/' + l.info.cardTraderBlueprintId : '#';
  }

  eur(value?: number | null): string {
    return value == null ? '—' : `${value.toFixed(2).replace('.', ',')} €`;
  }
}
