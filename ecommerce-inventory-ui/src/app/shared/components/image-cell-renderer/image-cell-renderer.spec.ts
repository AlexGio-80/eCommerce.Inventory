import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ImageCellRenderer } from './image-cell-renderer';

describe('ImageCellRenderer', () => {
  let component: ImageCellRenderer;
  let fixture: ComponentFixture<ImageCellRenderer>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ImageCellRenderer]
    })
    .compileComponents();

    fixture = TestBed.createComponent(ImageCellRenderer);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
