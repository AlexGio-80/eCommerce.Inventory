import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ImagePreviewComponent } from './image-preview.component';

describe('ImagePreviewComponent', () => {
  let component: ImagePreviewComponent;
  let fixture: ComponentFixture<ImagePreviewComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ImagePreviewComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(ImagePreviewComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('mostra l"immagine indicata dall"input imageUrl', () => {
    fixture.componentRef.setInput('imageUrl', 'https://example.test/card.jpg');
    fixture.detectChanges();

    const img = fixture.nativeElement.querySelector('img.preview-image') as HTMLImageElement | null;
    expect(img?.getAttribute('src')).toBe('https://example.test/card.jpg');
  });
});
