import { Subject, of } from 'rxjs';
import { ComponentFixture, TestBed } from '@angular/core/testing';
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

/** 一次載滿的精選頁，資料同步到齊——頁籤相關的測試都從這裡開始。 */
async function createShowcase(
  items: ReturnType<typeof item>[],
): Promise<ComponentFixture<ShowcaseComponent>> {
  await TestBed.configureTestingModule({
    imports: [ShowcaseComponent],
    providers: [
      provideRouter([]),
      {
        provide: CatalogService,
        useValue: {
          showcase: () => of({ items, total: items.length, page: 1, pageSize: 200 }),
        },
      },
      { provide: CategoryService, useValue: { list: () => of([]) } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(ShowcaseComponent);
  fixture.detectChanges();

  return fixture;
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

  it('defaults to the collage tab and renders only that section', async () => {
    const fixture = await createShowcase([
      item({ id: 'h', effectiveDisplayMode: 'Hero' }),
      item({ id: 'l', effectiveDisplayMode: 'List' }),
    ]);

    expect(fixture.nativeElement.querySelector('[data-collage-section]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-hero-section]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-stats-section]')).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('[data-item-card]').length).toBe(0);
  });

  it('renders the hero section when the view input selects it', async () => {
    const fixture = await createShowcase([
      item({ id: 'h', effectiveDisplayMode: 'Hero' }),
      item({ id: 'l', effectiveDisplayMode: 'List' }),
    ]);
    fixture.componentRef.setInput('view', 'hero');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-hero-section]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-collage-section]')).toBeNull();
  });

  it('falls back to the collage tab for an unknown view value', async () => {
    const fixture = await createShowcase([item({ id: 'a', effectiveDisplayMode: 'List' })]);
    fixture.componentRef.setInput('view', 'not-a-view');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-collage-section]')).toBeTruthy();
  });

  it('falls back to the collage tab when the requested view has no items', async () => {
    // 書籤存了 ?view=hero，之後所有焦點品項都被取消——不能停在一個停用又空白的頁籤上。
    const fixture = await createShowcase([item({ id: 'l', effectiveDisplayMode: 'List' })]);
    fixture.componentRef.setInput('view', 'hero');
    fixture.detectChanges();

    expect(fixture.componentInstance.activeView()).toBe('collage');
    expect(fixture.nativeElement.querySelector('[data-collage-section]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-hero-section]')).toBeNull();
  });

  it('counts hero and stats tabs by display mode and the others by total', async () => {
    const fixture = await createShowcase([
      item({ id: 'h1', effectiveDisplayMode: 'Hero' }),
      item({ id: 's1', effectiveDisplayMode: 'Stats' }),
      item({ id: 's2', effectiveDisplayMode: 'Stats' }),
      item({ id: 'l1', effectiveDisplayMode: 'List' }),
    ]);

    expect(fixture.componentInstance.tabs().map((t) => [t.id, t.count])).toEqual([
      ['collage', 4],
      ['hero', 1],
      ['stats', 2],
      ['list', 4],
    ]);
  });

  it('renders every showcased item in the list tab', async () => {
    const fixture = await createShowcase([
      item({ id: 'h', effectiveDisplayMode: 'Hero' }),
      item({ id: 'l', effectiveDisplayMode: 'List' }),
    ]);
    fixture.componentRef.setInput('view', 'list');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('[data-item-card]').length).toBe(2);
  });

  it('hides the tablist until every item has loaded', async () => {
    // 永不 emit 的 Subject：停在載入中，頁籤列的數字還不是穩定的事實。
    await TestBed.configureTestingModule({
      imports: [ShowcaseComponent],
      providers: [
        provideRouter([]),
        { provide: CatalogService, useValue: { showcase: () => new Subject() } },
        { provide: CategoryService, useValue: { list: () => of([]) } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ShowcaseComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-showcase-tabs]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('載入中');
  });
});
