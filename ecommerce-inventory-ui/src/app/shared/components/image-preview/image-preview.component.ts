import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
    selector: 'app-image-preview',
    standalone: true,
    imports: [CommonModule],
    template: `
    <div class="preview-container">
      <img [src]="imageUrl" alt="Card Preview" class="preview-image">
    </div>
  `,
    styles: [`
    .preview-container {
      position: fixed;
      z-index: 10000;
      background: white;
      padding: 8px;
      border-radius: 8px;
      box-shadow: 0 4px 20px rgba(0,0,0,0.3);
      pointer-events: none;
      animation: fadeIn 0.2s ease-out;
      border: 1px solid #ddd;
    }

    .preview-image {
      max-width: 350px; /* Large size as requested */
      max-height: 500px;
      display: block;
      border-radius: 4px;
    }

    @keyframes fadeIn {
      from { opacity: 0; transform: scale(0.95); }
      to { opacity: 1; transform: scale(1); }
    }
  `]
})
export class ImagePreviewComponent {
    @Input() imageUrl: string = '';
}
