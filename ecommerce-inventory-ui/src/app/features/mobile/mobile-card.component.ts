import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { GradingResult, GradingService } from '../../core/services/grading.service';
import { CONDITIONS, CardValuation, LANGUAGES, MobileService, errorMessage, shrinkPhoto } from './mobile.service';

/**
 * Scheda di valutazione: prezzo delle regole dell'autopricer in evidenza, poi mercato Card Trader,
 * listino Cardmarket e le mie inserzioni. Condizione, lingua e foil ricalcolano tutto.
 * "Stima condizione" (Fase 2) propone una condizione dalle foto: si applica solo col tocco dell'utente.
 */
@Component({
  selector: 'app-mobile-card',
  standalone: true,
  imports: [FormsModule, DecimalPipe, DatePipe, MatSelectModule, MatSlideToggleModule, MatProgressSpinnerModule, MatButtonModule, MatIconModule],
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

    @if (!gradingOpen()) {
      <button mat-stroked-button color="primary" class="grade-open" (click)="openGrading()">
        <mat-icon>auto_awesome</mat-icon> Stima condizione
      </button>
    } @else {
      <div class="grading">
        <input #shotInput type="file" accept="image/*" capture="environment" hidden (change)="onShot($event)">

        @if (gradingResult(); as g) {
          <div class="proposal">
            <div class="label">Condizione proposta</div>
            <div class="cond">{{ g.conditionName }}</div>
            <div class="label">Sicurezza {{ confidenceLabel(g.confidence) }} · {{ g.imagesAnalyzed }} foto</div>
          </div>
          @if (g.cornersNote) { <div class="row"><span>Angoli</span><span class="note">{{ g.cornersNote }}</span></div> }
          @if (g.edgesNote) { <div class="row"><span>Bordi</span><span class="note">{{ g.edgesNote }}</span></div> }
          @if (g.surfaceNote) { <div class="row"><span>Superficie</span><span class="note">{{ g.surfaceNote }}</span></div> }
          @if (g.centeringNote) { <div class="row"><span>Centratura</span><span class="note">{{ g.centeringNote }}</span></div> }
          @if (g.summary) { <p class="tip">{{ g.summary }}</p> }
          <p class="tip">Nel dubbio la stima sceglie la condizione peggiore.</p>
          <div class="actions">
            <button mat-raised-button color="primary" (click)="useGrading(g)">Usa questa condizione</button>
            <button mat-button (click)="closeGrading()">Annulla</button>
          </div>
          @if (g.nearMintPossible) {
            <button mat-stroked-button class="nm" (click)="useCondition('Near Mint')">Usa Near Mint (dopo averla controllata)</button>
          }
        } @else {
          <div class="shots">
            @for (slot of slots; track slot.name; let i = $index) {
              <button class="shot" [class.small]="i >= 2" [class.done]="photos()[i]" (click)="pick(i, shotInput)">
                @if (previews()[i]) { <img [src]="previews()[i]" alt=""> } @else { <mat-icon>photo_camera</mat-icon> }
                <span>{{ slot.label }}</span>
              </button>
            }
          </div>
          <p class="tip">Avvicinati finché la carta riempie la foto, fuori dalla bustina, su un piano scuro con luce di lato.
            I dettagli sono facoltativi: carta sotto la luce, in controluce per il retro, o un primo piano di un difetto.</p>
          <div class="actions">
            <button mat-raised-button color="primary" [disabled]="!photos()[0] || grading()" (click)="grade()">
              {{ grading() ? 'Stimo…' : 'Stima la condizione' }}
            </button>
            <button mat-button (click)="closeGrading()">Annulla</button>
          </div>
        }
        @if (gradingError()) { <p class="error">{{ gradingError() }}</p> }
      </div>
    }

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
    .grade-open { width: 100%; margin-bottom: 8px; }
    .grading { border: 1px solid #c5cae9; border-radius: 8px; padding: 10px; margin-bottom: 12px; background: #fff; }
    .shots { display: flex; flex-wrap: wrap; gap: 8px; }
    .shot.small { flex: 1 1 40%; height: 84px; }
    .shot.small img { max-height: 52px; }
    .shot { flex: 1 1 40%; height: 120px; border: 1.5px dashed #9e9e9e; border-radius: 8px; background: #fafafa;
            display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 4px;
            color: #666; font: inherit; font-size: 12px; overflow: hidden; }
    .shot.done { border: 2px solid #3f51b5; color: #3f51b5; }
    .shot img { max-height: 84px; border-radius: 4px; }
    .tip { font-size: 12px; color: #666; margin: 8px 0; }
    .actions { display: flex; gap: 8px; align-items: center; }
    .proposal { background: #fff3e0; color: #e65100; border-radius: 8px; padding: 8px 10px; margin-bottom: 6px; }
    .cond { font-size: 20px; font-weight: 500; }
    .note { text-align: right; color: #444; }
    .nm { width: 100%; margin-top: 8px; }
  `]
})
export class MobileCardComponent implements OnInit {
  private mobile = inject(MobileService);
  private gradingService = inject(GradingService);
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

  // --- Stima della condizione (Fase 2) ---
  /**
   * Fronte, retro e due dettagli. Il nome del file dice al backend il ruolo di ogni foto: Near Mint si
   * propone solo se c'è anche il retro, e i dettagli valgono più delle foto normali.
   */
  readonly slots = [
    { name: 'fronte', label: 'Fronte' },
    { name: 'retro', label: 'Retro (facoltativo)' },
    { name: 'dettaglio-1', label: 'Dettaglio' },
    { name: 'dettaglio-2', label: 'Dettaglio' }
  ];

  gradingOpen = signal(false);
  photos = signal<(Blob | null)[]>([null, null, null, null]);
  previews = signal<(string | null)[]>([null, null, null, null]);
  grading = signal(false);
  gradingResult = signal<GradingResult | null>(null);
  gradingError = signal<string | null>(null);
  private pickingSlot = 0;

  openGrading(): void {
    this.closeGrading();
    this.gradingOpen.set(true);
  }

  closeGrading(): void {
    for (const url of this.previews()) {
      if (url) URL.revokeObjectURL(url);
    }
    this.gradingOpen.set(false);
    this.photos.set([null, null, null, null]);
    this.previews.set([null, null, null, null]);
    this.gradingResult.set(null);
    this.gradingError.set(null);
  }

  pick(slot: number, input: HTMLInputElement): void {
    this.pickingSlot = slot;
    input.click();
  }

  async onShot(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    const slot = this.pickingSlot;
    this.gradingError.set(null);
    try {
      // 1.568 px sul lato lungo: il massimo che Claude usa davvero, e per angoli e graffi serve.
      const photo = new File([await shrinkPhoto(file, 1568)], `${this.slots[slot].name}.jpg`, { type: 'image/jpeg' });
      const old = this.previews()[slot];
      if (old) URL.revokeObjectURL(old);
      this.previews.update(p => p.map((u, i) => i === slot ? URL.createObjectURL(photo) : u));
      this.photos.update(p => p.map((b, i) => i === slot ? photo : b));
    } catch {
      this.gradingError.set('Non riesco a leggere la foto. Riprova.');
    }
  }

  grade(): void {
    const photos = this.photos().filter((p): p is Blob => !!p);
    if (photos.length === 0) return;
    this.grading.set(true);
    this.gradingError.set(null);
    this.gradingService.analyzeCardMultiple(photos).subscribe({
      next: r => {
        this.grading.set(false);
        this.gradingResult.set(r);
      },
      error: err => {
        this.grading.set(false);
        this.gradingError.set(errorMessage(err, 'Stima non riuscita. Riprova.'));
      }
    });
  }

  /** La condizione cambia solo qui, col tocco dell'utente; i prezzi si ricalcolano. */
  useGrading(g: GradingResult): void {
    this.useCondition(g.conditionName);
  }

  useCondition(condition: string): void {
    if (CONDITIONS.includes(condition)) {
      this.condition = condition;
      this.load();
    }
    this.closeGrading();
  }

  confidenceLabel(confidence: number): string {
    return confidence >= 0.85 ? 'alta' : confidence >= 0.6 ? 'media' : 'bassa';
  }

  money(value?: number | null): string {
    return value == null ? '—' : `${value.toFixed(2).replace('.', ',')} €`;
  }
}
