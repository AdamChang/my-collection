import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ShowcaseDisplayItem } from '../showcase-sections/showcase-display-item';
import { ItemPreviewOverlayComponent } from './item-preview-overlay.component';

const preview: ShowcaseDisplayItem = {
  id: 'x',
  name: '初音未來 1/7 比例模型',
  description: '這段描述不該出現在浮層裡',
  imageUrl: 'http://localhost/media/x-card.webp',
  effectiveDisplayMode: 'Hero',
  acquiredAt: '2026-01-15T00:00:00Z',
  price: { amount: 12800, currency: 'TWD' },
  rating: 9,
  storageLocation: '書房 A 櫃第二層',
  attributes: {},
  cardAttributes: [{ key: 'scale', label: '比例', value: '1/7' }],
};

async function createOverlay(
  item: ShowcaseDisplayItem | null,
): Promise<ComponentFixture<ItemPreviewOverlayComponent>> {
  await TestBed.configureTestingModule({
    imports: [ItemPreviewOverlayComponent],
  }).compileComponents();

  const fixture = TestBed.createComponent(ItemPreviewOverlayComponent);
  fixture.componentRef.setInput('item', item);
  fixture.detectChanges();

  return fixture;
}

describe('ItemPreviewOverlayComponent', () => {
  it('renders nothing without an item', async () => {
    const fixture = await createOverlay(null);

    expect(fixture.nativeElement.querySelector('[data-preview-overlay]')).toBeNull();
  });

  it('shows the name, card attributes, and acquisition fields', async () => {
    const fixture = await createOverlay(preview);
    const text = fixture.nativeElement.textContent;

    expect(fixture.nativeElement.querySelector('[data-preview-overlay]')).toBeTruthy();
    expect(text).toContain('初音未來 1/7 比例模型');
    expect(text).toContain('比例');
    expect(text).toContain('1/7');
    expect(text).toContain('書房 A 櫃第二層');
    expect(text).toContain('9');
  });

  it('never shows the description', async () => {
    const fixture = await createOverlay(preview);

    expect(fixture.nativeElement.textContent).not.toContain('這段描述不該出現在浮層裡');
  });

  it('starts from the already-cached card image', async () => {
    const fixture = await createOverlay(preview);

    expect(
      fixture.nativeElement.querySelector('[data-preview-image]').getAttribute('src'),
    ).toBe('http://localhost/media/x-card.webp');
  });

  it('falls back to an initial when the item has no image', async () => {
    const fixture = await createOverlay({ ...preview, imageUrl: null });

    expect(fixture.nativeElement.querySelector('[data-preview-image]')).toBeNull();
    expect(
      fixture.nativeElement.querySelector('[data-preview-placeholder]').textContent.trim(),
    ).toBe('初');
  });
});
