import { of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CatalogService } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import { ShowcaseComponent } from './showcase.component';

function item(overrides: Record<string, unknown> = {}) {
  const id = (overrides['id'] as string) ?? 'i1';

  return {
    id,
    categoryId: 'c1',
    name: id,
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
    effectiveDisplayMode: 'List',
    createdAt: '2026-08-01T00:00:00Z',
    updatedAt: '2026-08-01T00:00:00Z',
    ...overrides,
  };
}

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
    expect(fixture.nativeElement.querySelector('.showcase__empty a')?.getAttribute('href')).toBe(
      '/catalog',
    );
  });

  it('keeps fetching until every showcased item is loaded', async () => {
    const calls: unknown[][] = [];
    const page = (pageNumber: number, count: number, total: number) => ({
      items: Array.from({ length: count }, (_, i) => item({ id: `p${pageNumber}-${i}` })),
      total,
      page: pageNumber,
      pageSize: 200,
    });

    await TestBed.configureTestingModule({
      imports: [ShowcaseComponent],
      providers: [
        provideRouter([]),
        {
          provide: CatalogService,
          useValue: {
            showcase: (...args: unknown[]) => {
              calls.push(args);
              return of(calls.length === 1 ? page(1, 200, 250) : page(2, 50, 250));
            },
          },
        },
        { provide: CategoryService, useValue: { list: () => of([]) } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ShowcaseComponent);
    fixture.detectChanges();

    expect(calls).toEqual([
      [1, 200],
      [2, 200],
    ]);
    expect(fixture.componentInstance.items().length).toBe(250);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('stops fetching when a page comes back empty even if total disagrees', async () => {
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

              // total 謊報 9999，但第二頁回空——不能無限抓下去。
              return of({
                items: calls.length === 1 ? [item({ id: 'a' })] : [],
                total: 9999,
                page: calls.length,
                pageSize: 200,
              });
            },
          },
        },
        { provide: CategoryService, useValue: { list: () => of([]) } },
      ],
    }).compileComponents();

    TestBed.createComponent(ShowcaseComponent).detectChanges();

    expect(calls.length).toBe(2);
  });

  it('shows the hero and stats sections only for items in the matching display mode', async () => {
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
