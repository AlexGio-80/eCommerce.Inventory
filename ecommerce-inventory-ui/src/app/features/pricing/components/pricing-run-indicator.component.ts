import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PricingRunMonitorService } from '../services/pricing-run-monitor.service';

/**
 * Segnalatore in barra di stato dell'esecuzione dell'autopricer in corso.
 *
 * Sta nel guscio dell'applicazione e non nella pagina dell'autopricer di proposito: è ciò
 * che rende l'esecuzione davvero in background. Si lancia il ricalcolo, si va altrove a
 * lavorare, e l'avanzamento resta sotto gli occhi; un clic riporta alla pagina.
 */
@Component({
    selector: 'app-pricing-run-indicator',
    standalone: true,
    imports: [CommonModule, MatIconModule, MatButtonModule, MatTooltipModule, MatProgressSpinnerModule],
    template: `
    <button *ngIf="monitor.status() as run"
            mat-button
            class="pricing-run-indicator"
            (click)="openPricingPage()"
            [matTooltip]="tooltip(run.description, run.phase)">
      <mat-spinner diameter="18" class="indicator-spinner"></mat-spinner>
      <span class="indicator-text">
        Autopricer
        <ng-container *ngIf="monitor.progressPercent() as percent; else preparing">
          {{ percent }}% ({{ run.evaluatedCount }}/{{ run.plannedCount }})
        </ng-container>
        <ng-template #preparing>{{ run.phase }}</ng-template>
      </span>
      <mat-icon *ngIf="run.cancellationRequested" class="indicator-stopping">stop_circle</mat-icon>
    </button>
  `,
    styles: [`
    .pricing-run-indicator {
      display: flex;
      align-items: center;
      gap: 8px;
      color: inherit;
    }

    .indicator-spinner ::ng-deep circle {
      stroke: currentColor;
    }

    .indicator-text {
      font-size: 13px;
      white-space: nowrap;
    }

    .indicator-stopping {
      font-size: 18px;
      width: 18px;
      height: 18px;
    }
  `]
})
export class PricingRunIndicatorComponent {
    readonly monitor = inject(PricingRunMonitorService);
    private readonly router = inject(Router);

    openPricingPage(): void {
        this.router.navigate(['/layout/pricing']);
    }

    tooltip(description: string, phase: string): string {
        return `${description} — ${phase}. Clicca per aprire l'autopricer.`;
    }
}
