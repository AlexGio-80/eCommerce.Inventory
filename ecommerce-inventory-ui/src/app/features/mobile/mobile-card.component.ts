import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { GradingResult, GradingService, gradingNotes } from '../../core/services/grading.service';
import { PendingListingsService } from '../products/services/pending-listings.service';
import { PurchasingService } from '../purchasing/services/purchasing.service';
import { CONDITIONS, CardValuation, LANGUAGES, MobileService, SetCard, errorMessage, normalizeTag, shrinkPhoto } from './mobile.service';

const LAST_TAG_KEY = 'mobile_last_tag';

/**
 * Scheda di valutazione: prezzo delle regole dell'autopricer in evidenza, poi mercato Card Trader,
 * listino Cardmarket e le mie inserzioni. Condizione, lingua e foil ricalcolano tutto.
 * "Stima condizione" (Fase 2) propone una condizione dalle foto: si applica solo col tocco dell'utente.
 * "Aggiungi alla coda" (Fase 3) mette la carta nella stessa coda della maschera "Nuovo Prodotto", con
 * la stima di Claude accanto alla condizione scelta.
 * ‹ ›, swipe e "Vai al numero" passano alle altre carte del set senza un'altra foto: condizione, lingua,
 * foil, Tag e prezzo d'acquisto restano, quantità, prezzo e stima ripartono per la carta nuova.
 */
@Component({
  selector: 'app-mobile-card',
  standalone: true,
  imports: [FormsModule, DecimalPipe, DatePipe, MatSelectModule, MatSlideToggleModule, MatProgressSpinnerModule, MatButtonModule, MatIconModule,
    MatInputModule, MatAutocompleteModule, MatSnackBarModule],
  template: `
    <div class="swipe" (touchstart)="onTouchStart($event)" (touchend)="onTouchEnd($event)">
    @if (setPosition() >= 0) {
      <div class="setnav">
        <button mat-icon-button [disabled]="setPosition() <= 0" (click)="goRelative(-1)" aria-label="Carta precedente"><mat-icon>chevron_left</mat-icon></button>
        @if (!jumpOpen()) {
          <button class="pos" (click)="openJump()">#{{ setList()[setPosition()].collectorNumber }} · {{ setPosition() + 1 }} di {{ setList().length }}</button>
        } @else {
          <span class="jump">
            <input #jumpInput type="text" inputmode="numeric" placeholder="N° di raccolta" [(ngModel)]="jumpNumber"
                   (keydown.enter)="jump()" autofocus>
            <button mat-button color="primary" (click)="jump()">Vai</button>
            <button mat-icon-button (click)="jumpOpen.set(false)" aria-label="Chiudi"><mat-icon>close</mat-icon></button>
          </span>
        }
        <button mat-icon-button [disabled]="setPosition() >= setList().length - 1" (click)="goRelative(1)" aria-label="Carta successiva"><mat-icon>chevron_right</mat-icon></button>
      </div>
    }
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
    } @else if (setPosition() >= 0) {
      <div class="head"><div class="name">{{ setList()[setPosition()].name }}</div></div>
    }
    </div>

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

      <div class="queue">
        <div class="section first">Aggiungi alla coda <span class="muted">({{ condition }}, {{ language }}@if (isFoil) {, foil })</span></div>
        <div class="qty">
          <span>Quantità</span>
          <button mat-icon-button (click)="quantity = quantity > 1 ? quantity - 1 : 1"><mat-icon>remove</mat-icon></button>
          <span class="n">{{ quantity }}</span>
          <button mat-icon-button (click)="quantity = quantity + 1"><mat-icon>add</mat-icon></button>
        </div>
        <div class="fields">
          <mat-form-field appearance="outline">
            <mat-label>Prezzo di vendita €</mat-label>
            <input matInput type="number" inputmode="decimal" min="0" step="0.01" [(ngModel)]="price" (ngModelChange)="priceManual = true">
            @if (atRulesPrice()) { <mat-hint>Prezzo delle regole</mat-hint> }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Prezzo d'acquisto €</mat-label>
            <input matInput type="number" inputmode="decimal" min="0" step="0.01" [(ngModel)]="purchasePrice">
          </mat-form-field>
        </div>
        <mat-form-field appearance="outline" class="tag">
          <mat-label>Tag</mat-label>
          <input matInput [(ngModel)]="tag" (ngModelChange)="onTagChange()" [matAutocomplete]="tagAuto" autocapitalize="characters">
          <mat-autocomplete #tagAuto="matAutocomplete" (optionSelected)="onTagChange()">
            @for (t of tagOptions(); track t) { <mat-option [value]="t">{{ t }}</mat-option> }
          </mat-autocomplete>
          @if (purchaseCostInfo()) { <mat-hint>{{ purchaseCostInfo() }}</mat-hint> }
        </mat-form-field>
        @if (lastGrading(); as g) {
          <div class="muted">Con la stima di Claude: {{ g.conditionName }}</div>
        }
        <button mat-raised-button color="primary" class="add" [disabled]="adding() || loading() || !((price ?? 0) > 0)" (click)="addToQueue()">
          {{ adding() ? 'Aggiungo…' : 'Aggiungi alla coda' }}
        </button>
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
    .setnav { display: flex; align-items: center; justify-content: space-between; margin: -4px 0 6px;
              background: #fff; border: 1px solid #e0e0e0; border-radius: 8px; }
    .pos { flex: 1; border: none; background: none; font: inherit; font-size: 14px; color: #3f51b5; padding: 8px 0; }
    .jump { flex: 1; display: flex; align-items: center; gap: 4px; }
    .jump input { flex: 1; min-width: 0; font: inherit; font-size: 16px; padding: 6px 8px; border: 1px solid #9fa8da; border-radius: 4px; }
    .queue { border: 1px solid #c5cae9; border-radius: 8px; padding: 2px 10px 10px; margin-bottom: 4px; background: #fff; }
    .section.first { margin-top: 8px; }
    .qty { display: flex; align-items: center; gap: 4px; font-size: 14px; margin-bottom: 4px; }
    .qty .n { min-width: 24px; text-align: center; font-size: 18px; font-weight: 500; }
    .fields { display: flex; gap: 8px; }
    .fields mat-form-field { flex: 1 1 0; min-width: 0; }
    .tag { width: 100%; }
    .add { width: 100%; margin-top: 6px; }
  `]
})
export class MobileCardComponent implements OnInit {
  private mobile = inject(MobileService);
  private gradingService = inject(GradingService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private pendingListings = inject(PendingListingsService);
  private purchasing = inject(PurchasingService);
  private snackBar = inject(MatSnackBar);

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
    this.loadPurchaseCosts();
    this.route.paramMap.subscribe(p => {
      this.blueprintId = Number(p.get('id'));
      this.currentId.set(this.blueprintId);
      this.quantity = 1;
      this.priceManual = false;
      this.lastGrading.set(null);
      this.closeGrading();
      this.jumpOpen.set(false);
      const q = this.route.snapshot.queryParamMap;
      this.language = q.get('language') && LANGUAGES.includes(q.get('language')!) ? q.get('language')! : 'English';
      this.isFoil = q.get('foil') === '1';
      this.valuation.set(null);
      this.error.set(null);
      // Scorrendo veloce si leggono i prezzi solo della carta su cui ci si ferma: ogni scheda costa una
      // lettura del mercato Card Trader, nel limite di 20 al minuto.
      clearTimeout(this.loadTimer);
      if (this.setPosition() >= 0) {
        this.loading.set(true);
        this.loadTimer = setTimeout(() => this.load(), 400);
      } else {
        this.load();
      }
    });
  }

  // --- Scorrimento del set ---
  setList = signal<SetCard[]>([]);
  private setExpansionId: number | null = null;
  private currentId = signal(0);
  setPosition = computed(() => this.setList().findIndex(c => c.blueprintId === this.currentId()));
  jumpOpen = signal(false);
  jumpNumber = '';
  private loadTimer?: ReturnType<typeof setTimeout>;
  private touchX = 0;
  private touchY = 0;

  private loadSet(expansionId: number): void {
    if (this.setExpansionId === expansionId) return;
    this.setExpansionId = expansionId;
    this.mobile.setCards(expansionId).subscribe({
      next: list => this.setList.set(list),
      error: () => {
        this.setExpansionId = null;
        this.setList.set([]);
      }
    });
  }

  goRelative(step: number): void {
    const target = this.setList()[this.setPosition() + step];
    if (target) this.goTo(target.blueprintId);
  }

  private goTo(blueprintId: number): void {
    this.router.navigate(['/m/card', blueprintId], {
      queryParams: { language: this.language, foil: this.isFoil ? 1 : null },
      replaceUrl: true
    });
  }

  openJump(): void {
    this.jumpNumber = '';
    this.jumpOpen.set(true);
  }

  /** "54", "054" e "54/281" sono lo stesso numero; con più stampe dello stesso numero si va alla prima. */
  jump(): void {
    const norm = (n: string) => {
      const t = n.trim().toLowerCase().split('/')[0].replace(/^0+(?=.)/, '');
      return t;
    };
    const wanted = norm(this.jumpNumber);
    if (!wanted) return;
    const target = this.setList().find(c => norm(c.collectorNumber) === wanted);
    if (target) {
      this.jumpOpen.set(false);
      this.goTo(target.blueprintId);
    } else {
      this.snackBar.open(`Nessuna carta n° ${this.jumpNumber.trim()} in questo set`, 'OK', { duration: 3000 });
    }
  }

  onTouchStart(e: TouchEvent): void {
    this.touchX = e.changedTouches[0].clientX;
    this.touchY = e.changedTouches[0].clientY;
  }

  /** Swipe orizzontale deciso sulla parte alta: a sinistra la carta successiva, a destra la precedente. */
  onTouchEnd(e: TouchEvent): void {
    if (this.jumpOpen()) return;
    const dx = e.changedTouches[0].clientX - this.touchX;
    const dy = e.changedTouches[0].clientY - this.touchY;
    if (Math.abs(dx) < 60 || Math.abs(dy) > Math.abs(dx) / 2) return;
    this.goRelative(dx < 0 ? 1 : -1);
  }

  load(): void {
    const id = this.blueprintId;
    this.loading.set(true);
    this.error.set(null);
    this.mobile.valuate(this.blueprintId, this.condition, this.language, this.isFoil).subscribe({
      next: v => {
        // Una risposta arrivata tardi, per la carta o i filtri di prima, non deve coprire quella attuale.
        if (v.card.blueprintId !== this.blueprintId || v.condition !== this.condition
          || v.language !== this.language || v.isFoil !== this.isFoil) return;
        this.valuation.set(v);
        this.loading.set(false);
        this.loadSet(v.card.expansionId);
        // Il prezzo segue quello delle regole finché non lo si cambia a mano.
        if (!this.priceManual) this.price = v.rulesPrice?.suggestedPrice ?? null;
      },
      error: err => {
        if (id !== this.blueprintId) return;
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
        this.lastGrading.set(r);
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

  // --- Aggiungi alla coda (Fase 3) ---
  quantity = 1;
  price: number | null = null;
  priceManual = false;
  purchasePrice = 0;
  /** Ultimo Tag usato dal telefono: di solito si caricano di fila le carte della stessa apertura. */
  tag = localStorage.getItem(LAST_TAG_KEY) ?? '';
  adding = signal(false);
  /** Ultima stima fatta su questa carta: resta anche dopo "Usa questa condizione", e va con l'inserzione. */
  lastGrading = signal<GradingResult | null>(null);
  purchaseCostInfo = signal<string | null>(null);
  private tagText = signal(this.tag);
  private registryTags = signal<string[]>([]);
  /** Costi per carta del registro acquisti, per Tag normalizzato (un acquisto può condividere il Tag con altri). */
  private purchaseCosts = new Map<string, { cost: number; product: string }[]>();
  /** Ultimo prezzo d'acquisto messo dal registro: si sostituisce cambiando Tag, uno scritto a mano no. */
  private autoFilledPrice: number | null = null;

  tagOptions = computed(() => {
    const text = normalizeTag(this.tagText()).replace('#', '');
    return this.registryTags().filter(t => t.includes(text)).slice(0, 8);
  });

  /** Il prezzo è quello delle regole per la condizione, la lingua e il foil che si stanno inviando. */
  atRulesPrice(): boolean {
    const v = this.valuation();
    return !!v && !this.loading() && v.condition === this.condition && v.language === this.language && v.isFoil === this.isFoil
      && v.rulesPrice?.suggestedPrice != null && v.rulesPrice.suggestedPrice === this.price;
  }

  private loadPurchaseCosts(): void {
    this.purchasing.getPurchases().subscribe({
      next: purchases => {
        this.purchaseCosts.clear();
        const tags = new Set<string>();
        for (const p of purchases) {
          const key = normalizeTag(p.tag);
          if (!key) continue;
          tags.add(key);
          if (p.effectiveCostPerCard == null) continue;
          const list = this.purchaseCosts.get(key) ?? [];
          list.push({ cost: p.effectiveCostPerCard, product: p.productName });
          this.purchaseCosts.set(key, list);
        }
        // I Tag finiscono con la data AAAAMMGG: in ordine inverso le aperture recenti vengono prima.
        this.registryTags.set([...tags].sort().reverse());
        this.applyPurchaseCost();
      },
      error: () => this.purchaseCosts.clear()
    });
  }

  onTagChange(): void {
    this.tagText.set(this.tag);
    this.applyPurchaseCost();
  }

  /** Stessa regola della maschera "Nuovo Prodotto": mai su un prezzo scritto a mano, e non se il Tag ha più costi. */
  private applyPurchaseCost(): void {
    const matches = this.purchaseCosts.get(normalizeTag(this.tag)) ?? [];
    const costs = [...new Set(matches.map(m => m.cost))];
    const current = Number(this.purchasePrice) || 0;
    const replaceable = current === 0 || (this.autoFilledPrice != null && current === this.autoFilledPrice);

    if (matches.length === 0) {
      // Il prezzo messo per il Tag di prima non vale per questo: meglio uno 0 evidente che un costo sbagliato.
      if (this.autoFilledPrice != null && current === this.autoFilledPrice) this.purchasePrice = 0;
      this.autoFilledPrice = null;
      this.purchaseCostInfo.set(null);
      return;
    }
    if (costs.length > 1) {
      this.purchaseCostInfo.set(`Più acquisti con questo Tag a costi diversi (${costs.map(c => c.toFixed(2)).join(' / ')}): scegli tu`);
      return;
    }
    if (!replaceable) {
      this.purchaseCostInfo.set(current === costs[0] ? `Dal registro acquisti: ${matches[0].product}` : `Registro acquisti: ${costs[0].toFixed(2)} € per carta`);
      return;
    }
    this.purchasePrice = costs[0];
    this.autoFilledPrice = costs[0];
    this.purchaseCostInfo.set(`Dal registro acquisti: ${matches[0].product}`);
  }

  addToQueue(): void {
    const price = this.price;
    if (price == null || !(price > 0)) return;
    const g = this.lastGrading();
    const tag = this.tag.trim();
    this.adding.set(true);
    this.pendingListings.createPendingListing({
      blueprintId: this.blueprintId,
      quantity: this.quantity,
      price,
      // Solo se è il prezzo delle regole per i filtri che si inviano: allora niente riprezzo dopo la pubblicazione.
      suggestedPrice: this.atRulesPrice() ? price : undefined,
      purchasePrice: Number(this.purchasePrice) || 0,
      condition: this.condition,
      language: this.language,
      isFoil: this.isFoil,
      isSigned: false,
      tag: tag || undefined,
      gradingScore: g?.overallGrade,
      gradingConditionCode: g?.conditionCode,
      gradingCentering: g?.centering,
      gradingCorners: g?.corners,
      gradingEdges: g?.edges,
      gradingSurface: g?.surface,
      gradingConfidence: g?.confidence,
      gradingImagesCount: g?.imagesAnalyzed,
      gradingNotes: gradingNotes(g)
    }).subscribe({
      next: () => {
        this.adding.set(false);
        if (tag) localStorage.setItem(LAST_TAG_KEY, tag); else localStorage.removeItem(LAST_TAG_KEY);
        // La stima riguarda la copia appena aggiunta, non la prossima.
        this.lastGrading.set(null);
        this.quantity = 1;
        this.mobile.refreshQueueCount().subscribe(n =>
          this.snackBar.open(n != null ? `Aggiunta alla coda (${n} in coda)` : 'Aggiunta alla coda', 'OK', { duration: 3000 }));
      },
      error: err => {
        this.adding.set(false);
        this.snackBar.open(errorMessage(err, 'Non riesco ad aggiungerla alla coda. Riprova.'), 'OK', { duration: 5000 });
      }
    });
  }

  confidenceLabel(confidence: number): string {
    return confidence >= 0.85 ? 'alta' : confidence >= 0.6 ? 'media' : 'bassa';
  }

  money(value?: number | null): string {
    return value == null ? '—' : `${value.toFixed(2).replace('.', ',')} €`;
  }
}
