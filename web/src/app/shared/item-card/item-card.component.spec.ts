import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ItemDto } from '../../core/models';
import { ItemCardComponent } from './item-card.component';

function item(overrides: Partial<ItemDto> = {}): ItemDto {
  return {
    id: 'i1',
    categoryId: 'c1',
    name: '初音ミク 1/8',
    description: null,
    images: [],
    tags: ['GSC'],
    isShowcased: false,
    source: 'Manual',
    externalRef: null,
    acquisition: null,
    locationId: null,
    attributes: {},
    createdAt: '2026-07-25T03:00:00Z',
    updatedAt: '2026-07-25T03:00:00Z',
    ...overrides,
  };
}

describe('ItemCardComponent', () => {
  let fixture: ComponentFixture<ItemCardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ItemCardComponent],
      providers: [provideRouter([])],
    }).compileComponents();
    fixture = TestBed.createComponent(ItemCardComponent);
  });

  function render(value: ItemDto): void {
    fixture.componentRef.setInput('item', value);
    fixture.detectChanges();
  }

  it('shows the item name', () => {
    render(item());

    expect(fixture.nativeElement.textContent).toContain('初音ミク 1/8');
  });

  it('uses the local card image when present', () => {
    render(item({ images: [{ id: 'x', path: 'p/full.webp', cardPath: 'p/card.webp', thumbPath: 'p/thumb.webp', isPrimary: true, order: 0 }] }));

    const img: HTMLImageElement = fixture.nativeElement.querySelector('img');
    expect(img.getAttribute('src')).toBe('/api/media/p/card.webp');
  });

  it('falls back to the remote header url for synced items without local images', () => {
    render(item({ source: 'Steam', attributes: { headerUrl: 'https://cdn/620.jpg' } }));

    const img: HTMLImageElement = fixture.nativeElement.querySelector('img');
    expect(img.getAttribute('src')).toBe('https://cdn/620.jpg');
  });

  it('renders a placeholder when there is no image at all', () => {
    render(item());

    expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-placeholder]')).toBeTruthy();
  });

  it('marks showcased items', () => {
    render(item({ isShowcased: true }));

    expect(fixture.nativeElement.querySelector('[data-showcased]')).toBeTruthy();
  });
});
