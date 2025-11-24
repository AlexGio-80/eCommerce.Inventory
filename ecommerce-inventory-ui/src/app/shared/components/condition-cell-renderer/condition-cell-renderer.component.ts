import { Component } from '@angular/core';
import { ICellRendererAngularComp } from 'ag-grid-angular';
import { ICellRendererParams } from 'ag-grid-community';
import { CommonModule } from '@angular/common';

@Component({
    selector: 'app-condition-cell-renderer',
    standalone: true,
    imports: [CommonModule],
    template: `
    <div class="condition-badge" [ngClass]="getConditionClass()" [title]="params.value">
      {{ getShortCondition() }}
    </div>
  `,
    styles: [`
    .condition-badge {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      padding: 2px 6px;
      border-radius: 4px;
      font-weight: bold;
      font-size: 12px;
      color: white;
      min-width: 24px;
      height: 20px;
      text-transform: uppercase;
    }
    .cond-nm { background-color: #4caf50; } /* Green */
    .cond-sp { background-color: #cddc39; color: #333; } /* Lime */
    .cond-mp { background-color: #ff9800; } /* Orange */
    .cond-hp { background-color: #f44336; } /* Red */
    .cond-po { background-color: #9e9e9e; } /* Grey */
    .cond-unknown { background-color: #607d8b; } /* Blue Grey */
  `]
})
export class ConditionCellRendererComponent implements ICellRendererAngularComp {
    params!: ICellRendererParams;

    agInit(params: ICellRendererParams): void {
        this.params = params;
    }

    refresh(params: ICellRendererParams): boolean {
        this.params = params;
        return true;
    }

    getConditionClass(): string {
        const cond = this.params.value?.toLowerCase() || '';
        if (cond.includes('near mint') || cond === 'nm') return 'cond-nm';
        if (cond.includes('slightly played') || cond === 'sp') return 'cond-sp';
        if (cond.includes('moderately played') || cond === 'mp') return 'cond-mp';
        if (cond.includes('heavily played') || cond === 'hp') return 'cond-hp';
        if (cond.includes('poor') || cond === 'po') return 'cond-po';
        return 'cond-unknown';
    }

    getShortCondition(): string {
        const cond = this.params.value || '';
        if (cond.toLowerCase().includes('near mint')) return 'NM';
        if (cond.toLowerCase().includes('slightly played')) return 'SP';
        if (cond.toLowerCase().includes('moderately played')) return 'MP';
        if (cond.toLowerCase().includes('heavily played')) return 'HP';
        if (cond.toLowerCase().includes('poor')) return 'PO';
        return cond.substring(0, 2).toUpperCase();
    }
}
