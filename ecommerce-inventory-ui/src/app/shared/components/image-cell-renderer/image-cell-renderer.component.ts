import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ICellRendererAngularComp } from 'ag-grid-angular';
import { ICellRendererParams } from 'ag-grid-community';
import { ImagePreviewDirective } from '../../directives/image-preview.directive';

@Component({
    selector: 'app-image-cell-renderer',
    standalone: true,
    imports: [CommonModule, ImagePreviewDirective],
    template: `
    <div class="image-cell" *ngIf="imageUrl">
      <img [src]="imageUrl" 
           [appImagePreview]="imageUrl"
           alt="Card" 
           class="grid-image">
    </div>
  `,
    styles: [`
    .image-cell {
      display: flex;
      align-items: center;
      justify-content: center;
      height: 100%;
    }
    .grid-image {
      height: 30px;
      cursor: pointer;
      border-radius: 2px;
      transition: transform 0.1s;
    }
    .grid-image:hover {
      transform: scale(1.1);
    }
  `]
})
export class ImageCellRendererComponent implements ICellRendererAngularComp {
    imageUrl: string = '';

    agInit(params: ICellRendererParams): void {
        this.imageUrl = params.value;
    }

    refresh(params: ICellRendererParams): boolean {
        this.imageUrl = params.value;
        return true;
    }
}
