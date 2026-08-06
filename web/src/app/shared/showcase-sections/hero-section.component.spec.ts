import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HeroSectionComponent } from './hero-section.component';
import { ShowcaseDisplayItem } from './showcase-display-item';

function displayItem(overrides: Partial<ShowcaseDisplayItem> = {}): ShowcaseDisplayItem {
  return {
    id: 'i1',
    name: '初音ミク 1/8',
    description: '開封済み',
    imageUrl: null,
    effectiveDisplayMode: 'Hero',
    acquiredAt: '2026-01-01T00:00:00Z',
    price: { amount: 12800, currency: 'TWD' },
    rating: 9,
    storageLocation: 'A櫃-第2層',
    attributes: {},
    cardAttributes: [{ key: 'scale', label: '比例', value: '1/8' }],
    ...overrides,
  };
}

describe('HeroSectionComponent', () => {
  let fixture: ComponentFixture<HeroSectionComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HeroSectionComponent] }).compileComponents();
    fixture = TestBed.createComponent(HeroSectionComponent);
  });

  it('renders nothing when there are no items', () => {
    fixture.componentRef.setInput('items', []);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-hero-section]')).toBeNull();
  });

  it('shows the current item name, fields, and gated info panel', () => {
    fixture.componentRef.setInput('items', [displayItem()]);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('初音ミク 1/8');
    expect(text).toContain('比例');
    expect(text).toContain('1/8');
    expect(text).toContain('A櫃-第2層');
    expect(text).toContain('12800');
    expect(text).toContain('9 / 10');
  });

  it('hides storage location and price rows when they are absent', () => {
    fixture.componentRef.setInput('items', [
      displayItem({ storageLocation: null, price: null, rating: null, acquiredAt: null }),
    ]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-hero-storage-location]')).toBeNull();
  });

  it('does not render rotation dots for a single item', () => {
    fixture.componentRef.setInput('items', [displayItem()]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-hero-dots]')).toBeNull();
  });

  it('rotates through multiple items on an interval and via manual selection', fakeAsync(() => {
    fixture.componentRef.setInput('items', [
      displayItem({ id: 'a', name: 'A' }),
      displayItem({ id: 'b', name: 'B' }),
    ]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('h2').textContent).toBe('A');

    tick(7000);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('h2').textContent).toBe('B');

    const dots: NodeListOf<HTMLButtonElement> = fixture.nativeElement.querySelectorAll('[data-hero-dots] button');
    dots[0].click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('h2').textContent).toBe('A');

    fixture.destroy();
  }));
});
