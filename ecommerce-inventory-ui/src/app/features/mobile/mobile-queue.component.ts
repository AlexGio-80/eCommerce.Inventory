import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { CreatePendingListingDto, PendingListing, PendingListingsService } from '../products/services/pending-listings.service';
import { CONDITIONS, LANGUAGES, MobileService, errorMessage } from './mobile.service';

/** Valori modificabili di una riga aperta. */
interface Draft {
  quantity: number;
  price: number;
  purchasePrice: number;
  condition: string;
  language: string;
  tag: string;
}

/**
 * Coda delle inserzioni non ancora inviate (feature 006, Fase 3): la stessa della maschera "Nuovo Prodotto",
 * quindi si può cominciare dal telefono e finire al PC, o il contrario. "Invia a Card Trader" usa lo stesso
 * invio del PC, con il riprezzo dopo la pubblicazione solo per le carte lontane dal prezzo delle regole.
 */
@Component({
  selector: 'app-mobile-queue',
  standalone: true,
  imports: [FormsModule, DecimalPipe, MatSelectModule, MatInputModule, MatButtonModule, MatIconModule,
    MatProgressSpinnerModule, MatSnackBarModule],
  template: `
    <div class="summary">
      <div><span class="big">{{ cards() }}</span> carte · {{ rows().length }} inserzioni</div>
      <div class="big">{{ total() | number:'1.2-2' }} €</div>
    </div>

    @if (loading()) { <div class="loading"><mat-spinner diameter="28"></mat-spinner></div> }
    @if (error()) { <p class="error">{{ error() }}</p> }
    @if (!loading() && rows().length === 0 && !error()) {
      <p class="muted center">La coda è vuota. Aggiungi le carte dalla scheda di valutazione.</p>
    }

    @for (item of rows(); track item.id) {
      <div class="item" [class.failed]="item.syncError">
        <div class="line" (click)="toggle(item)">
          @if (item.blueprint?.imageUrl) { <img [src]="item.blueprint.imageUrl" alt=""> }
          <div class="info">
            <div class="name">{{ item.blueprint?.name }}
              @if (item.isUpdate) { <span class="badge">Modifica</span> }
            </div>
            <div class="sub">{{ item.blueprint?.expansion?.name }}</div>
            <div class="sub">{{ item.quantity }} × {{ item.condition }} · {{ item.language }}@if (item.isFoil) { · foil }@if (item.tag) { · {{ item.tag }} }</div>
          </div>
          <div class="price">{{ item.sellingPrice | number:'1.2-2' }} €</div>
        </div>
        @if (item.syncError) { <div class="err-line">Invio non riuscito: {{ item.syncError }}</div> }

        @if (openId() === item.id && draft; as d) {
          <div class="edit">
            <div class="fields">
              <mat-form-field appearance="outline">
                <mat-label>Quantità</mat-label>
                <input matInput type="number" inputmode="numeric" min="1" [(ngModel)]="d.quantity">
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Prezzo €</mat-label>
                <input matInput type="number" inputmode="decimal" min="0" step="0.01" [(ngModel)]="d.price">
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Acquisto €</mat-label>
                <input matInput type="number" inputmode="decimal" min="0" step="0.01" [(ngModel)]="d.purchasePrice">
              </mat-form-field>
            </div>
            <div class="fields">
              <mat-form-field appearance="outline">
                <mat-label>Condizione</mat-label>
                <mat-select [(ngModel)]="d.condition">
                  @for (c of conditions; track c) { <mat-option [value]="c">{{ c }}</mat-option> }
                </mat-select>
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Lingua</mat-label>
                <mat-select [(ngModel)]="d.language">
                  @for (l of languagesFor(item); track l) { <mat-option [value]="l">{{ l }}</mat-option> }
                </mat-select>
              </mat-form-field>
            </div>
            <mat-form-field appearance="outline" class="wide">
              <mat-label>Tag</mat-label>
              <input matInput [(ngModel)]="d.tag" autocapitalize="characters">
            </mat-form-field>
            @if (item.gradingConditionCode) {
              <div class="muted">Stima di Claude: {{ item.gradingConditionCode }}</div>
            }
            <div class="actions">
              <button mat-raised-button color="primary" [disabled]="saving() || !(d.quantity >= 1) || !(d.price > 0)" (click)="save(item, d)">Salva</button>
              <button mat-button (click)="openId.set(null)">Chiudi</button>
              <span class="spacer"></span>
              <button mat-button color="warn" (click)="remove(item)"><mat-icon>delete</mat-icon> Elimina</button>
            </div>
          </div>
        }
      </div>
    }

    @if (rows().length > 0) {
      @if (!confirming()) {
        <button mat-raised-button color="primary" class="send" [disabled]="sending() || deleting().size > 0" (click)="confirming.set(true)">
          <mat-icon>cloud_upload</mat-icon> Invia a Card Trader
        </button>
      } @else {
        <div class="confirm">
          <div>Pubblico {{ rows().length }} inserzioni ({{ cards() }} carte) su Card Trader?</div>
          <div class="actions">
            <button mat-raised-button color="primary" [disabled]="sending()" (click)="send()">
              {{ sending() ? 'Invio in corso…' : 'Sì, pubblica' }}
            </button>
            <button mat-button [disabled]="sending()" (click)="confirming.set(false)">Annulla</button>
          </div>
          @if (sending()) { <div class="muted">Card Trader accetta 20 richieste al minuto: con molte carte ci vuole qualche minuto.</div> }
        </div>
      }
    }
  `,
  styles: [`
    .summary { display: flex; justify-content: space-between; align-items: baseline; background: #e8eaf6; color: #283593;
               border-radius: 8px; padding: 10px 12px; margin-bottom: 10px; font-size: 14px; }
    .big { font-size: 20px; font-weight: 500; }
    .loading { display: flex; justify-content: center; padding: 8px; }
    .error { color: #c62828; background: #ffebee; padding: 8px; border-radius: 4px; }
    .muted { color: #888; font-size: 12px; }
    .center { text-align: center; margin-top: 24px; }
    .item { background: #fff; border: 1px solid #e0e0e0; border-radius: 8px; margin-bottom: 8px; overflow: hidden; }
    .item.failed { border-color: #e57373; }
    .line { display: flex; gap: 10px; align-items: center; padding: 8px; }
    .line img { width: 44px; border-radius: 3px; }
    .info { flex: 1; min-width: 0; }
    .name { font-weight: 500; font-size: 14px; }
    .sub { font-size: 12px; color: #666; overflow: hidden; text-overflow: ellipsis; }
    .price { font-weight: 500; white-space: nowrap; }
    .badge { font-size: 10px; background: #fff3e0; color: #e65100; border-radius: 3px; padding: 1px 4px; margin-left: 4px; font-weight: 400; }
    .err-line { color: #c62828; background: #ffebee; font-size: 12px; padding: 4px 8px; }
    .edit { border-top: 1px solid #eee; padding: 8px 8px 4px; }
    .fields { display: flex; gap: 6px; }
    .fields mat-form-field { flex: 1 1 0; min-width: 0; }
    .wide { width: 100%; }
    .actions { display: flex; gap: 8px; align-items: center; }
    .spacer { flex: 1; }
    .send { width: 100%; margin: 8px 0 16px; }
    .confirm { background: #fff; border: 1px solid #3f51b5; border-radius: 8px; padding: 10px; margin: 8px 0 16px; }
    .confirm .actions { margin: 8px 0 4px; }
  `]
})
export class MobileQueueComponent implements OnInit {
  private pendingListings = inject(PendingListingsService);
  private mobile = inject(MobileService);
  private snackBar = inject(MatSnackBar);

  conditions = CONDITIONS;

  items = signal<PendingListing[]>([]);
  /** Righe tolte con "Elimina" in attesa che scada "Annulla": non si vedono e non si possono inviare. */
  deleting = signal(new Set<number>());
  rows = computed(() => this.items().filter(i => !this.deleting().has(i.id)));
  cards = computed(() => this.rows().reduce((n, i) => n + i.quantity, 0));
  total = computed(() => this.rows().reduce((t, i) => t + i.quantity * i.sellingPrice, 0));

  loading = signal(false);
  error = signal<string | null>(null);
  openId = signal<number | null>(null);
  draft: Draft | null = null;
  saving = signal(false);
  confirming = signal(false);
  sending = signal(false);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.pendingListings.getPendingListings(1, 500, false).subscribe({
      next: r => {
        this.items.set(r?.data?.items ?? []);
        this.loading.set(false);
        this.mobile.queueCount.set(r?.data?.totalCount ?? null);
      },
      error: err => {
        this.loading.set(false);
        this.error.set(errorMessage(err, 'Non riesco a leggere la coda. Riprova.'));
      }
    });
  }

  toggle(item: PendingListing): void {
    if (this.openId() === item.id) {
      this.openId.set(null);
      return;
    }
    this.draft = {
      quantity: item.quantity,
      price: item.sellingPrice,
      purchasePrice: item.purchasePrice,
      condition: item.condition,
      language: item.language,
      tag: item.tag ?? ''
    };
    this.openId.set(item.id);
  }

  /** Una lingua scritta dal PC fuori dall'elenco del telefono resta selezionabile. */
  languagesFor(item: PendingListing): string[] {
    return LANGUAGES.includes(item.language) ? LANGUAGES : [item.language, ...LANGUAGES];
  }

  save(item: PendingListing, d: Draft): void {
    // Il prezzo delle regole valeva per condizione e lingua di prima: con altre non dice più niente.
    const sameFilters = d.condition === item.condition && d.language === item.language;
    const dto: CreatePendingListingDto = {
      blueprintId: item.blueprintId,
      quantity: d.quantity,
      price: d.price,
      suggestedPrice: sameFilters ? item.suggestedPrice ?? undefined : undefined,
      purchasePrice: Number(d.purchasePrice) || 0,
      condition: d.condition,
      language: d.language,
      isFoil: item.isFoil,
      isSigned: item.isSigned,
      description: item.description,
      tag: d.tag.trim() || undefined
    };
    this.saving.set(true);
    this.pendingListings.updatePendingListing(item.id, dto).subscribe({
      next: () => {
        this.saving.set(false);
        this.openId.set(null);
        this.load();
      },
      error: err => {
        this.saving.set(false);
        this.snackBar.open(errorMessage(err, 'Modifica non salvata. Riprova.'), 'OK', { duration: 5000 });
      }
    });
  }

  /** La riga sparisce subito; si cancella davvero solo se non si tocca "Annulla" entro qualche secondo. */
  remove(item: PendingListing): void {
    this.openId.set(null);
    this.confirming.set(false);
    this.deleting.update(s => new Set(s).add(item.id));
    const ref = this.snackBar.open(`${item.blueprint?.name ?? 'Inserzione'} tolta dalla coda`, 'Annulla', { duration: 4000 });
    ref.afterDismissed().subscribe(({ dismissedByAction }) => {
      const done = () => this.deleting.update(s => { const n = new Set(s); n.delete(item.id); return n; });
      if (dismissedByAction) {
        done();
        return;
      }
      this.pendingListings.deletePendingListing(item.id).subscribe({
        next: () => {
          this.items.update(list => list.filter(i => i.id !== item.id));
          done();
          this.mobile.refreshQueueCount().subscribe();
        },
        error: err => {
          done();
          this.snackBar.open(errorMessage(err, 'Non riesco a eliminarla. Riprova.'), 'OK', { duration: 5000 });
        }
      });
    });
  }

  send(): void {
    this.sending.set(true);
    this.pendingListings.syncPendingListings().subscribe({
      next: r => {
        this.sending.set(false);
        this.confirming.set(false);
        const res = r?.data;
        const text = res
          ? res.errors > 0
            ? `Pubblicate ${res.success} su ${res.total}: ${res.errors} non riuscite, restano in coda`
            : `Pubblicate ${res.success} inserzioni su Card Trader`
          : 'Invio completato';
        this.snackBar.open(text, 'OK', { duration: 6000 });
        this.load();
      },
      error: err => {
        this.sending.set(false);
        this.snackBar.open(errorMessage(err, 'Invio non riuscito. Controlla la coda e riprova.'), 'OK', { duration: 6000 });
        this.load();
      }
    });
  }
}
