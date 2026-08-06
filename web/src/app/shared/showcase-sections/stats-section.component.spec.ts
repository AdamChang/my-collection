import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { StatsSectionComponent } from './stats-section.component';
import { ShowcaseDisplayItem } from './showcase-display-item';

function displayItem(overrides: Partial<ShowcaseDisplayItem> = {}): ShowcaseDisplayItem {
  return {
    id: 'g1',
    name: 'Team Fortress 2',
    description: null,
    imageUrl: 'https://cdn/header.jpg',
    effectiveDisplayMode: 'Stats',
    acquiredAt: null,
    price: null,
    rating: null,
    storageLocation: null,
    attributes: { playtimeForever: 125, psnProgress: 80 },
    cardAttributes: [],
    ...overrides,
  };
}

describe('StatsSectionComponent', () => {
  let fixture: ComponentFixture<StatsSectionComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [StatsSectionComponent] }).compileComponents();
    fixture = TestBed.createComponent(StatsSectionComponent);
  });

  it('renders nothing when there are no items', () => {
    fixture.componentRef.setInput('items', []);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-stats-section]')).toBeNull();
  });

  it('converts playtime minutes to hours and shows the completion percentage', () => {
    fixture.componentRef.setInput('items', [displayItem()]);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('2.1 小時');
    expect(text).toContain('80%');
    expect(fixture.nativeElement.querySelector('[data-stats-progress]').getAttribute('aria-valuenow')).toBe('80');
  });

  it('hides a metric row entirely when its value is absent', () => {
    fixture.componentRef.setInput('items', [displayItem({ attributes: { playtimeForever: 30 } })]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-stats-progress]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('0.5 小時');
  });

  it('rotates to the next game on an interval', fakeAsync(() => {
    fixture.componentRef.setInput('items', [
      displayItem({ id: 'a', name: 'Game A' }),
      displayItem({ id: 'b', name: 'Game B' }),
    ]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('h2').textContent).toBe('Game A');

    tick(7000);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('h2').textContent).toBe('Game B');

    fixture.destroy();
  }));
});
