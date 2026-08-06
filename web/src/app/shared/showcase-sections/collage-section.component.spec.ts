import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { CollageSectionComponent } from './collage-section.component';
import { ShowcaseDisplayItem } from './showcase-display-item';

function displayItem(id: string): ShowcaseDisplayItem {
  return {
    id,
    name: id,
    description: null,
    imageUrl: null,
    effectiveDisplayMode: 'List',
    acquiredAt: null,
    price: null,
    rating: null,
    storageLocation: null,
    attributes: {},
    cardAttributes: [],
  };
}

describe('CollageSectionComponent', () => {
  let fixture: ComponentFixture<CollageSectionComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CollageSectionComponent] }).compileComponents();
    fixture = TestBed.createComponent(CollageSectionComponent);
  });

  it('renders nothing when there are no items', () => {
    fixture.componentRef.setInput('items', []);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-collage-section]')).toBeNull();
  });

  it('caps the visible slots at slotCount even with more items available', () => {
    fixture.componentRef.setInput('items', [displayItem('a'), displayItem('b'), displayItem('c')]);
    fixture.componentRef.setInput('slotCount', 2);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('[data-collage-card]').length).toBe(2);
  });

  it('shows every item when there are fewer than slotCount', () => {
    fixture.componentRef.setInput('items', [displayItem('a')]);
    fixture.componentRef.setInput('slotCount', 4);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('[data-collage-card]').length).toBe(1);
  });

  it('swaps a slot for a not-yet-shown item on an interval when more photos exist than slots', fakeAsync(() => {
    fixture.componentRef.setInput('items', [displayItem('a'), displayItem('b'), displayItem('c')]);
    fixture.componentRef.setInput('slotCount', 2);
    fixture.detectChanges();

    const namesBefore = Array.from(
      fixture.nativeElement.querySelectorAll('[data-collage-card] figcaption') as NodeListOf<HTMLElement>,
    ).map((el) => el.textContent);
    expect(namesBefore).toEqual(['a', 'b']);

    tick(4000);
    fixture.detectChanges();

    const namesAfter = Array.from(
      fixture.nativeElement.querySelectorAll('[data-collage-card] figcaption') as NodeListOf<HTMLElement>,
    ).map((el) => el.textContent);
    expect(namesAfter).toContain('c');
    expect(fixture.nativeElement.querySelectorAll('[data-collage-card]').length).toBe(2);

    fixture.destroy();
  }));

  it('does not schedule a swap when there are no extra photos beyond the visible slots', fakeAsync(() => {
    fixture.componentRef.setInput('items', [displayItem('a'), displayItem('b')]);
    fixture.componentRef.setInput('slotCount', 4);
    fixture.detectChanges();

    tick(4000);
    fixture.detectChanges();

    const names = Array.from(
      fixture.nativeElement.querySelectorAll('[data-collage-card] figcaption') as NodeListOf<HTMLElement>,
    ).map((el) => el.textContent);
    expect(names).toEqual(['a', 'b']);

    fixture.destroy();
  }));
});
