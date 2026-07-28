import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ItemImageDto } from '../../core/models';
import { ImageUploaderComponent } from './image-uploader.component';

describe('ImageUploaderComponent', () => {
  let fixture: ComponentFixture<ImageUploaderComponent>;

  const images: ItemImageDto[] = [
    {
      id: 'primary',
      path: 'primary/full.webp',
      cardPath: 'primary/card.webp',
      thumbPath: 'primary/thumb.webp',
      isPrimary: true,
      order: 0,
    },
    {
      id: 'secondary',
      path: 'secondary/full.webp',
      cardPath: 'secondary/card.webp',
      thumbPath: 'secondary/thumb.webp',
      isPrimary: false,
      order: 1,
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ImageUploaderComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(ImageUploaderComponent);
    fixture.componentRef.setInput('images', images);
    fixture.detectChanges();
  });

  it('labels the primary image without relying on border color', () => {
    const primary: HTMLElement = fixture.nativeElement.querySelector(
      '.uploader__item--primary',
    );
    const status: HTMLElement | null = primary.querySelector('[data-primary-status]');

    expect(status?.textContent?.trim()).toBe('主圖');
    expect(status?.getAttribute('aria-label')).toBe('目前主圖');
  });

  it('retains set-primary and remove events', () => {
    const component = fixture.componentInstance;
    const setPrimary = spyOn(component.setPrimary, 'emit');
    const remove = spyOn(component.remove, 'emit');
    const secondary: HTMLElement = fixture.nativeElement.querySelectorAll('.uploader__item')[1];

    secondary.querySelector<HTMLButtonElement>('button')!.click();
    secondary.querySelectorAll<HTMLButtonElement>('button')[1].click();

    expect(setPrimary).toHaveBeenCalledOnceWith('secondary');
    expect(remove).toHaveBeenCalledOnceWith('secondary');
  });
});
