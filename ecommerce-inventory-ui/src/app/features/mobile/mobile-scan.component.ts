import { Component, ElementRef, ViewChild, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CardScanResult, MobileService, ScanCandidate, errorMessage, shrinkPhoto } from './mobile.service';

/**
 * Scansiona: il pulsante apre la fotocamera del telefono (non un riquadro nella pagina, così ci sono
 * messa a fuoco e flash di Android), la foto ridotta va al riconoscimento e si sceglie la carta.
 */
@Component({
  selector: 'app-mobile-scan',
  standalone: true,
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <input #photoInput type="file" accept="image/*" capture="environment" hidden (change)="onPhoto($event)">

    <button mat-raised-button color="primary" class="shoot" (click)="photoInput.click()" [disabled]="working()">
      <mat-icon>photo_camera</mat-icon> Fotografa la carta
    </button>
    <p class="hint">Carta intera e dritta, con buona luce: nome e numero di raccolta devono leggersi.</p>

    @if (working()) {
      <div class="working"><mat-spinner diameter="28"></mat-spinner><span>Riconosco la carta…</span></div>
    }

    @if (error()) {
      <p class="error">{{ error() }}</p>
    }

    @if (result(); as r) {
      <div class="read">
        Letto: {{ r.reading.printedName || r.reading.name || '—' }}
        @if (r.reading.setCode) { · {{ r.reading.setCode.toUpperCase() }} }
        @if (r.reading.collectorNumber) { · {{ r.reading.collectorNumber }} }
        @if (r.reading.language) { · {{ r.reading.language }} }
        @if (r.reading.isFoil) { · foil }
        @if (r.reading.notes) { <div class="notes">{{ r.reading.notes }}</div> }
      </div>

      @if (r.candidates.length === 0) {
        <p>Nessuna carta corrispondente nel database.</p>
      } @else {
        <h3>Quale carta è?</h3>
        @for (c of r.candidates; track c.blueprintId) {
          <button class="candidate" [class.exact]="c.exactPrint" (click)="open(c)">
            @if (c.imageUrl) { <img [src]="c.imageUrl" alt=""> }
            <div>
              <div class="name">{{ c.name }}</div>
              @if (c.italianName && c.italianName !== c.name) { <div class="sub">{{ c.italianName }}</div> }
              <div class="sub">{{ c.expansionName }} · #{{ c.collectorNumber || '?' }}@if (c.version) { · {{ c.version }} }</div>
            </div>
          </button>
        }
      }

      <a class="search" [routerLink]="['/m/search']" [queryParams]="{ q: r.reading.name || r.reading.printedName || '' }">
        <mat-icon>search</mat-icon> Non è nessuna di queste: cerca
      </a>
      <p class="usage">Riconoscimenti oggi: {{ r.usedToday }} su {{ r.dailyLimit }}</p>
    }
  `,
  styles: [`
    .shoot { width: 100%; height: 56px; font-size: 16px; }
    .hint { color: #666; font-size: 13px; text-align: center; margin: 8px 0 16px; }
    .working { display: flex; align-items: center; gap: 12px; justify-content: center; color: #555; }
    .error { color: #c62828; background: #ffebee; padding: 8px; border-radius: 4px; }
    .read { font-size: 13px; color: #555; margin: 8px 0; }
    .notes { font-style: italic; margin-top: 4px; }
    h3 { font-size: 15px; font-weight: 500; margin: 12px 0 8px; }
    .candidate { display: flex; gap: 10px; align-items: center; width: 100%; text-align: left; padding: 8px;
                 margin-bottom: 8px; border: 1px solid #ddd; border-radius: 8px; background: #fff; font: inherit; }
    .candidate.exact { border: 2px solid #3f51b5; }
    .candidate img { width: 52px; border-radius: 4px; }
    .name { font-weight: 500; }
    .sub { font-size: 12px; color: #666; }
    .search { display: flex; align-items: center; gap: 4px; color: #3f51b5; text-decoration: none; margin: 8px 0; }
    .usage { font-size: 11px; color: #999; text-align: center; }
  `]
})
export class MobileScanComponent {
  @ViewChild('photoInput') photoInput!: ElementRef<HTMLInputElement>;

  private mobile = inject(MobileService);
  private router = inject(Router);

  working = signal(false);
  error = signal<string | null>(null);
  result = signal<CardScanResult | null>(null);

  async onPhoto(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    this.error.set(null);
    this.result.set(null);
    this.working.set(true);

    let photo: Blob;
    try {
      photo = await shrinkPhoto(file);
    } catch {
      this.working.set(false);
      this.error.set('Non riesco a leggere la foto. Riprova.');
      return;
    }

    this.mobile.recognize(photo).subscribe({
      next: r => {
        this.working.set(false);
        // Una sola stampa certa: si va dritti alla scheda.
        const exact = r.candidates.filter(c => c.exactPrint);
        if (exact.length === 1 && r.candidates.length >= 1 && r.candidates[0] === exact[0]) {
          this.open(exact[0], r);
          return;
        }
        this.result.set(r);
      },
      error: err => {
        this.working.set(false);
        this.error.set(errorMessage(err, 'Riconoscimento non riuscito. Riprova.'));
      }
    });
  }

  open(card: ScanCandidate, scan: CardScanResult | null = this.result()): void {
    const reading = scan?.reading;
    this.router.navigate(['/m/card', card.blueprintId], {
      queryParams: {
        language: reading?.language || 'English',
        foil: reading?.isFoil ? 1 : 0
      }
    });
  }
}
