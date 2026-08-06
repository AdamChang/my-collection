import { of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CatalogService } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import { ShowcaseComponent } from './showcase.component';

describe('ShowcaseComponent', () => {
  it('renders the archive terminal and useful empty state', async () => {
    await TestBed.configureTestingModule({
      imports: [ShowcaseComponent],
      providers: [
        provideRouter([]),
        {
          provide: CatalogService,
          useValue: { showcase: () => of({ items: [], total: 0, page: 1, pageSize: 200 }) },
        },
        { provide: CategoryService, useValue: { list: () => of([]) } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ShowcaseComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-showcase-terminal]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.showcase__empty a')?.getAttribute('href'))
      .toBe('/catalog');
  });

  it('loads the first page with a size large enough to feed the hero/stats/collage sections', async () => {
    const calls: unknown[][] = [];

    await TestBed.configureTestingModule({
      imports: [ShowcaseComponent],
      providers: [
        provideRouter([]),
        {
          provide: CatalogService,
          useValue: {
            showcase: (...args: unknown[]) => {
              calls.push(args);
              return of({ items: [], total: 0, page: 1, pageSize: 200 });
            },
          },
        },
        { provide: CategoryService, useValue: { list: () => of([]) } },
      ],
    }).compileComponents();

    TestBed.createComponent(ShowcaseComponent).detectChanges();

    expect(calls).toEqual([[1, 200]]);
  });

  it('shows the hero and stats sections only for items in the matching display mode', async () => {
    const item = (overrides: Record<string, unknown>) => ({
      id: overrides['id'],
      categoryId: 'c1',
      name: overrides['id'],
      description: null,
      images: [],
      tags: [],
      isShowcased: true,
      source: 'Manual',
      externalRef: null,
      acquisition: null,
      locationId: null,
      attributes: {},
      displayMode: null,
      rating: null,
      storageLocation: null,
      createdAt: '2026-08-01T00:00:00Z',
      updatedAt: '2026-08-01T00:00:00Z',
      ...overrides,
    });

    await TestBed.configureTestingModule({
      imports: [ShowcaseComponent],
      providers: [
        provideRouter([]),
        {
          provide: CatalogService,
          useValue: {
            showcase: () =>
              of({
                items: [
                  item({ id: 'hero-item', effectiveDisplayMode: 'Hero' }),
                  item({ id: 'list-item', effectiveDisplayMode: 'List' }),
                ],
                total: 2,
                page: 1,
                pageSize: 200,
              }),
          },
        },
        { provide: CategoryService, useValue: { list: () => of([]) } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ShowcaseComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-hero-section]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-stats-section]')).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('[data-item-card]').length).toBe(2);
  });
});
