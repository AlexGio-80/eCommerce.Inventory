import { Component } from '@angular/core';
import { ICellRendererAngularComp } from 'ag-grid-angular';
import { ICellRendererParams } from 'ag-grid-community';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';

@Component({
    selector: 'app-foil-cell-renderer',
    standalone: true,
    imports: [CommonModule, MatIconModule],
    template: `
    <div class="foil-cell" *ngIf="isFoil" title="Foil">
      <mat-icon class="foil-icon">auto_awesome</mat-icon>
    </div>
  `,
    styles: [`
    .foil-cell {
      display: flex;
      align-items: center;
      justify-content: center;
      height: 100%;
    }
    .foil-icon {
      font-size: 18px;
      height: 18px;
      width: 18px;
      color: #ffb300; /* Amber/Gold */
      text-shadow: 0 0 2px rgba(255, 179, 0, 0.5);
    }
  `]
})
export class FoilCellRendererComponent implements ICellRendererAngularComp {
    params!: ICellRendererParams;
    isFoil = false;

    agInit(params: ICellRendererParams): void {
        this.params = params;
        this.isFoil = !!params.value;
    }

    refresh(params: ICellRendererParams): boolean {
        this.params = params;
        this.isFoil = !!params.value;
        return true;
    }
}
