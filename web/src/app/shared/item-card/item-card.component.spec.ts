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

  it('exposes an accessible clickable card contract', () => {
    render(item());

    const card: HTMLAnchorElement = fixture.nativeElement.querySelector('[data-item-card]');
    expect(card).toBeTruthy();
    expect(card.getAttribute('aria-label')).toBe('查看 初音ミク 1/8');
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

  it('renders only the attributes marked showOnCard', () => {
    fixture.componentRef.setInput('item', item({ attributes: { brand: 'GSC', scale: '1/8' } }));
    fixture.componentRef.setInput('cardFields', [
      { key: 'brand', label: '廠商', type: 'Text', options: null, required: false, searchable: true, showOnCard: true },
      { key: 'scale', label: '比例', type: 'Text', options: null, required: false, searchable: false, showOnCard: false },
    ]);
    fixture.detectChanges();

    const text = fixture.nativeElement.querySelector('[data-card-fields]').textContent;
    expect(text).toContain('GSC');
    expect(text).not.toContain('1/8');
  });

  it('renders no attribute row when no field is marked showOnCard', () => {
    fixture.componentRef.setInput('item', item({ attributes: { brand: 'GSC' } }));
    fixture.componentRef.setInput('cardFields', []);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-card-fields]')).toBeNull();
  });

  it('shows playtime instead of PSN progress while retaining unrelated card attributes', () => {
    renderActivityCard({ playtimeForever: 120, psnProgress: 75, platform: 'Steam' });

    const text = fixture.nativeElement.querySelector('[data-card-fields]').textContent;
    expect(text).toContain('遊玩時數（分鐘）');
    expect(text).toContain('120');
    expect(text).not.toContain('PSN 獎盃完成度');
    expect(text).not.toContain('75');
    expect(text).toContain('平台／商店');
    expect(text).toContain('Steam');
  });

  it('shows PSN progress when playtime is absent while retaining unrelated card attributes', () => {
    renderActivityCard({ psnProgress: 75, platform: 'PlayStation' });

    const text = fixture.nativeElement.querySelector('[data-card-fields]').textContent;
    expect(text).not.toContain('遊玩時數（分鐘）');
    expect(text).not.toContain('120');
    expect(text).toContain('PSN 獎盃完成度');
    expect(text).toContain('75');
    expect(text).toContain('平台／商店');
    expect(text).toContain('PlayStation');
  });

  it('treats zero playtime as populated and suppresses PSN progress', () => {
    renderActivityCard({ playtimeForever: 0, psnProgress: 75, platform: 'Steam' });

    const text = fixture.nativeElement.querySelector('[data-card-fields]').textContent;
    expect(text).toContain('遊玩時數（分鐘）');
    expect(text).toContain('0');
    expect(text).not.toContain('PSN 獎盃完成度');
    expect(text).not.toContain('75');
    expect(text).toContain('平台／商店');
    expect(text).toContain('Steam');
  });

  it('hides activity rows when neither activity value is populated while retaining unrelated card attributes', () => {
    renderActivityCard({ platform: 'PlayStation' });

    const text = fixture.nativeElement.querySelector('[data-card-fields]').textContent;
    expect(text).not.toContain('遊玩時數（分鐘）');
    expect(text).not.toContain('PSN 獎盃完成度');
    expect(text).toContain('平台／商店');
    expect(text).toContain('PlayStation');
  });

  function renderActivityCard(attributes: ItemDto['attributes']): void {
    fixture.componentRef.setInput('item', item({ attributes }));
    fixture.componentRef.setInput('cardFields', [
      { key: 'playtimeForever', label: '遊玩時數（分鐘）', type: 'Number', options: null, required: false, searchable: false, showOnCard: true },
      { key: 'psnProgress', label: 'PSN 獎盃完成度', type: 'Number', options: null, required: false, searchable: false, showOnCard: true },
      { key: 'platform', label: '平台／商店', type: 'Text', options: null, required: false, searchable: true, showOnCard: true },
    ]);
    fixture.detectChanges();
  }
});
