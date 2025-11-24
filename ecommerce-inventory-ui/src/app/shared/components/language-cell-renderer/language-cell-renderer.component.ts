import { Component } from '@angular/core';
import { ICellRendererAngularComp } from 'ag-grid-angular';
import { ICellRendererParams } from 'ag-grid-community';
import { CommonModule } from '@angular/common';

@Component({
    selector: 'app-language-cell-renderer',
    standalone: true,
    imports: [CommonModule],
    template: `
    <div class="language-cell">
      <span [class]="'fi fi-' + getFlagCode()" [title]="params.value"></span>
      <span class="lang-text" *ngIf="showText">{{ params.value }}</span>
    </div>
  `,
    styles: [`
    .language-cell {
      display: flex;
      align-items: center;
      gap: 8px;
    }
    .fi {
      font-size: 1.2em;
      border-radius: 2px;
      box-shadow: 0 1px 2px rgba(0,0,0,0.2);
    }
    .lang-text {
        font-size: 0.9em;
    }
  `]
})
export class LanguageCellRendererComponent implements ICellRendererAngularComp {
    params!: ICellRendererParams;
    showText = false;

    agInit(params: ICellRendererParams): void {
        this.params = params;
        // Optional: show text if column is wide enough or configured
    }

    refresh(params: ICellRendererParams): boolean {
        this.params = params;
        return true;
    }

    getFlagCode(): string {
        const lang = this.params.value?.toLowerCase() || '';
        // Map language codes to country codes for flag-icons
        const map: { [key: string]: string } = {
            'en': 'us', // Assuming English is US, or could be 'gb'
            'it': 'it',
            'fr': 'fr',
            'de': 'de',
            'es': 'es',
            'pt': 'pt',
            'ja': 'jp',
            'jp': 'jp',
            'ru': 'ru',
            'zh': 'cn',
            'cn': 'cn',
            'ko': 'kr',
            'kr': 'kr',
            'tw': 'tw'
        };
        return map[lang] || lang;
    }
}
