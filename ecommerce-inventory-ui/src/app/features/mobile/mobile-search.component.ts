import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { Subject, debounceTime, distinctUntilChanged, switchMap, of, catchError } from 'rxjs';
import { Blueprint } from '../../core/models/blueprint';
import { MobileService } from './mobile.service';

/** Cerca: la scheda di valutazione partendo dal nome (anche italiano) o dal numero di raccolta, senza IA. */
@Component({
  selector: 'app-mobile-search',
  standalone: true,
  imports: [FormsModule, MatFormFieldModule, MatInputModule, MatIconModule],
  template: `
    <mat-form-field appearance="outline" class="field">
      <mat-label>Nome, nome italiano o numero</mat-label>
      <input matInput type="search" [(ngModel)]="text" (ngModelChange)="terms.next($event)" autocomplete="off">
      <mat-icon matSuffix>search</mat-icon>
    </mat-form-field>

    @if (searching()) { <p class="muted">Cerco…</p> }
    @if (!searching() && text.trim().length >= 3 && results().length === 0) { <p class="muted">Nessuna carta trovata.</p> }

    @for (b of results(); track b.id) {
      <button class="row" (click)="open(b)">
        @if (b.imageUrl) { <img [src]="b.imageUrl" alt=""> }
        <div>
          <div class="name">{{ b.name }}@if (b.italianName && b.italianName !== b.name) { <span class="muted"> ({{ b.italianName }})</span> }</div>
          <div class="sub">{{ b.expansion?.name }} · #{{ number(b) || '?' }}@if (b.version) { · {{ b.version }} }</div>
        </div>
      </button>
    }
  `,
  styles: [`
    .field { width: 100%; }
    .muted { color: #888; font-size: 13px; }
    .row { display: flex; gap: 10px; align-items: center; width: 100%; text-align: left; padding: 6px 4px;
           border: none; border-bottom: 1px solid #e0e0e0; background: transparent; font: inherit; }
    .row img { width: 40px; border-radius: 3px; }
    .name { font-weight: 500; }
    .sub { font-size: 12px; color: #666; }
  `]
})
export class MobileSearchComponent implements OnInit {
  private mobile = inject(MobileService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  text = '';
  terms = new Subject<string>();
  results = signal<Blueprint[]>([]);
  searching = signal(false);

  ngOnInit(): void {
    this.terms.pipe(
      debounceTime(350),
      distinctUntilChanged(),
      switchMap(t => {
        if (t.trim().length < 3) {
          this.searching.set(false);
          return of([] as Blueprint[]);
        }
        this.searching.set(true);
        return this.mobile.search(t.trim()).pipe(catchError(() => of([] as Blueprint[])));
      })
    ).subscribe(list => {
      this.searching.set(false);
      this.results.set(list);
    });

    const q = this.route.snapshot.queryParamMap.get('q');
    if (q) {
      this.text = q;
      this.terms.next(q);
    }
  }

  number(b: Blueprint): string | null {
    try {
      return b.fixedProperties ? JSON.parse(b.fixedProperties).collector_number ?? null : null;
    } catch {
      return null;
    }
  }

  open(b: Blueprint): void {
    this.router.navigate(['/m/card', b.id]);
  }
}
