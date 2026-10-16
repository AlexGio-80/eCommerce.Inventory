import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CONDITIONS, CardValuation, LANGUAGES, MobileService, errorMessage } from './mobile.service';

/**
 * Scheda di valutazione: prezzo delle regole dell'autopricer in evidenza, poi mercato Card Trader,
 * listino Cardmarket e le mie inserzioni. Condizione, lingua e foil ricalcolano tutto.
 */
@Component({
  selector: 'app-mobile-card',
  standalone: true,
  imports: [FormsModule, DecimalPipe, DatePipe, MatSelectModule, MatSlideToggleModule, MatProgressSpinnerModule],
  template: `
    @if (valuation(); as v) {
      <div class="head">
        @if (v.card.imageUrl) { <img [src]="v.card.imageUrl" alt=""> }
        <div>
          <div class="name">{{ v.card.name }}</div>
          @if (v.card.italianName && v.card.italianName !== v.card.name) { <div class="sub">{{ v.card.italianName }}</div> }
          <div class="sub">{{ v.card.expansionName }} · #{{ v.card.collectorNumber || '?' }}</div>
          @if (v.card.version) { <div class="sub">{{ v.card.version }}</div> }
        </div>
      </div>
    }

    <div class="filters">
      <mat-form-field appearance="outline" class="cond">
        <mat-label>Condizione</mat-label>
        <mat-select [(ngModel)]="condition" (selectionChange)="load()">
          @for (c of conditions; track c) { <mat-option [value]="c">{{ c }}</mat-option> }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline" class="lang">
        <mat-label>Lingua</mat-label>
        <mat-select [(ngModel)]="language" (selectionChange)="load()">
          @for (l of languages; track l) { <mat-option [value]="l">{{ l }}</mat-option> }
        </mat-select>
      </mat-form-field>
      <mat-slide-toggle [(ngModel)]="isFoil" (change)="load()">Foil</mat-slide-toggle>
    </div>

    @if (loading()) { <div class="loading"><mat-spinner diameter="28"></mat-spinner></div> }
    @if (error()) { <p class="error">{{ error() }}</p> }

    @if (valuation(); as v) {
      <div class="rules">
        <div class="label">Prezzo delle regole</div>
        @if (v.rulesPrice?.suggestedPrice != null) {
          <div class="price">{{ v.rulesPrice!.suggestedPrice | number:'1.2-2' }} €</div>
          <div class="reason">{{ v.rulesPrice!.comparableOffersCount }} offerte comparabili</div>
        } @else {
          <div class="none">Nessun prezzo</div>
          <div class="reason">{{ v.rulesPrice?.reason }}</div>
        }
      </div>

      <div class="section">Card Trader <span class="muted">(prezzi in vetrina, escluse le mie)</span></div>
      <div class="row"><span>Più bassa</span><span>{{ money(v.ctLowest) }}</span></div>
      <div class="row"><span>Più bassa CT Zero</span><span>{{ money(v.ctZeroLowest) }}</span></div>
      <div class="row"><span>Offerte</span><span>{{ v.ctOffers }}</span></div>

      <div class="section">Cardmarket @if (v.cmDate) { <span class="muted">(listino del {{ v.cmDate | date:'dd/MM' }})</span> }</div>
      @if (v.cmTrend == null && v.cmLow == null) {
        <div class="muted">Nessun prezzo Cardmarket per questa carta.</div>
      } @else {
        <div class="row"><span>Trend</span><span>{{ money(v.cmTrend) }}</span></div>
        <div class="row"><span>Più bassa</span><span>{{ money(v.cmLow) }}</span></div>
      }

      <div class="section">Le mie inserzioni</div>
      @if (v.myListings.length === 0) {
        <div class="muted">Nessuna copia in vendita.</div>
      }
      @for (m of v.myListings; track $index) {
        <div class="row"><span>{{ m.quantity }} × {{ m.condition }} {{ m.language }}@if (m.isFoil) { foil }</span><span>{{ money(m.price) }}</span></div>
      }

      <a class="ct" [href]="'https://www.cardtrader.com/cards/' + v.card.cardTraderId" target="_blank" rel="noopener">Apri su Card Trader</a>
    }
  `,
  styles: [`
    .head { display: flex; gap: 12px; align-items: center; margin-bottom: 12px; }
    .head img { width: 90px; border-radius: 5px; }
    .name { font-weight: 500; font-size: 17px; }
    .sub { font-size: 12px; color: #666; }
    .filters { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
    .cond { flex: 1 1 150px; }
    .lang { flex: 1 1 120px; }
    .loading { display: flex; justify-content: center; padding: 8px; }
    .error { color: #c62828; background: #ffebee; padding: 8px; border-radius: 4px; }
    .rules { background: #e8eaf6; color: #283593; border-radius: 8px; padding: 10px 12px; margin: 4px 0 12px; }
    .label { font-size: 12px; }
    .price { font-size: 26px; font-weight: 500; }
    .none { font-size: 18px; font-weight: 500; }
    .reason { font-size: 12px; }
    .section { font-weight: 500; margin: 14px 0 4px; }
    .row { display: flex; justify-content: space-between; padding: 4px 0; border-bottom: 1px solid #eee; font-size: 14px; }
    .muted { color: #888; font-size: 12px; font-weight: 400; }
    .ct { display: block; text-align: center; margin: 16px 0 8px; color: #3f51b5; }
  `]
})
export class MobileCardComponent implements OnInit {
  private mobile = inject(MobileService);
  private route = inject(ActivatedRoute);

  conditions = CONDITIONS;
  languages = LANGUAGES;

  blueprintId = 0;
  condition = 'Near Mint';
  language = 'English';
  isFoil = false;

  valuation = signal<CardValuation | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);

  ngOnInit(): void {
    this.route.paramMap.subscribe(p => {
      this.blueprintId = Number(p.get('id'));
      const q = this.route.snapshot.queryParamMap;
      this.language = q.get('language') && LANGUAGES.includes(q.get('language')!) ? q.get('language')! : 'English';
      this.isFoil = q.get('foil') === '1';
      this.valuation.set(null);
      this.load();
    });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.mobile.valuate(this.blueprintId, this.condition, this.language, this.isFoil).subscribe({
      next: v => {
        this.valuation.set(v);
        this.loading.set(false);
      },
      error: err => {
        this.loading.set(false);
        this.error.set(errorMessage(err, 'Non riesco a leggere i prezzi. Riprova.'));
      }
    });
  }

  money(value?: number | null): string {
    return value == null ? '—' : `${value.toFixed(2).replace('.', ',')} €`;
  }
}
