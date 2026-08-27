import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ImagePreviewDirective } from './image-preview.directive';

@Component({
  standalone: true,
  imports: [ImagePreviewDirective],
  template: `<img [appImagePreview]="url" alt="Card" />`
})
class HostComponent {
  url: string | undefined = 'https://example.test/card.jpg';
}

describe('ImagePreviewDirective', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;
  let img: HTMLImageElement;

  /** La preview viene spostata sotto <body> per non essere tagliata dagli overflow della griglia. */
  const previewInBody = () => document.body.querySelector('app-image-preview');

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
    img = fixture.nativeElement.querySelector('img');
  });

  afterEach(() => {
    previewInBody()?.remove();
  });

  it('should create an instance', () => {
    expect(fixture.debugElement.children[0].injector.get(ImagePreviewDirective)).toBeTruthy();
  });

  it('apre la preview sotto body al passaggio del mouse', () => {
    img.dispatchEvent(new MouseEvent('mouseenter'));
    fixture.detectChanges();

    const preview = previewInBody();
    expect(preview).toBeTruthy();
    expect(preview?.parentElement).toBe(document.body);
    expect(preview?.querySelector('img')?.getAttribute('src'))
      .toBe('https://example.test/card.jpg');
  });

  it('chiude la preview quando il mouse esce', () => {
    img.dispatchEvent(new MouseEvent('mouseenter'));
    fixture.detectChanges();
    img.dispatchEvent(new MouseEvent('mouseleave'));
    fixture.detectChanges();

    expect(previewInBody()).toBeNull();
  });

  it('senza URL non apre nessuna preview', () => {
    host.url = undefined;
    fixture.detectChanges();

    img.dispatchEvent(new MouseEvent('mouseenter'));
    fixture.detectChanges();

    expect(previewInBody()).toBeNull();
  });
});
