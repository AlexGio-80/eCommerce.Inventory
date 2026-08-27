import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ICellRendererParams } from 'ag-grid-community';

import { ImageCellRendererComponent } from './image-cell-renderer.component';

describe('ImageCellRendererComponent', () => {
  let component: ImageCellRendererComponent;
  let fixture: ComponentFixture<ImageCellRendererComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ImageCellRendererComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(ImageCellRendererComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('mostra l"immagine ricevuta da ag-grid', () => {
    component.agInit({ value: 'https://example.test/card.jpg' } as ICellRendererParams);
    fixture.detectChanges();

    const img = fixture.nativeElement.querySelector('img.grid-image') as HTMLImageElement | null;
    expect(img?.getAttribute('src')).toBe('https://example.test/card.jpg');
  });

  it('senza URL non renderizza nessuna immagine', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('img')).toBeNull();
  });
});
