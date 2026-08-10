import { Subject, of } from 'rxjs';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_BASE } from '../../core/api-base';
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
  categories: unknown[] = [],
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
      { provide: CategoryService, useValue: { list: () => of(categories) } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(ShowcaseComponent);
  fixture.detectChanges();

  return fixture;
}

/** 切到列表頁籤、把游標停在第 index 張卡片上並等過進場延遲，回傳浮層元素。 */
function hoverCard(fixture: ComponentFixture<ShowcaseComponent>, index = 0): HTMLElement | null {
  fixture.componentRef.setInput('view', 'list');
  fixture.detectChanges();

  const cards = fixture.nativeElement.querySelectorAll('[data-showcase-card]');
  cards[index].dispatchEvent(new MouseEvent('mouseenter'));
  tick(200);
  fixture.detectChanges();

  return fixture.nativeElement.querySelector('[data-preview-overlay]');
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

  it('feeds the collage eight slots on the internal showcase page', async () => {
    // 剛好 8 件：slotCount 為 4 時只會渲染 4 格，所以這條仍然驗得出 slotCount。
    // 刻意不給超過 8 件——CollageSectionComponent 只有在 pool 多於格數時才啟動輪播，
    // 而這條測試不是 fakeAsync，真的起了 setInterval 會讓整個 karma 跑不完。
    const fixture = await createShowcase(
      Array.from({ length: 8 }, (_, i) => item({ id: `i${i}`, effectiveDisplayMode: 'List' })),
    );

    expect(fixture.nativeElement.querySelectorAll('[data-collage-card]').length).toBe(8);
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

  it('opens the preview only after the hover delay elapses', fakeAsync(async () => {
    const fixture = await createShowcase([item({ id: 'a', effectiveDisplayMode: 'List' })]);
    fixture.componentRef.setInput('view', 'list');
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('[data-showcase-card]');
    card.dispatchEvent(new MouseEvent('mouseenter'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-preview-overlay]')).toBeNull();

    tick(200);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-preview-overlay]')).toBeTruthy();

    card.dispatchEvent(new MouseEvent('mouseleave'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-preview-overlay]')).toBeNull();
  }));

  it('cancels a pending preview when the pointer moves on before the delay', fakeAsync(async () => {
    const fixture = await createShowcase([
      item({ id: 'a', effectiveDisplayMode: 'List' }),
      item({ id: 'b', effectiveDisplayMode: 'List' }),
    ]);
    fixture.componentRef.setInput('view', 'list');
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('[data-showcase-card]');
    cards[0].dispatchEvent(new MouseEvent('mouseenter'));
    tick(120);
    cards[0].dispatchEvent(new MouseEvent('mouseleave'));
    cards[1].dispatchEvent(new MouseEvent('mouseenter'));
    tick(200);
    fixture.detectChanges();

    // 只有第二張的預覽，第一張的計時器必須已經被取消。
    expect(fixture.nativeElement.querySelector('[data-preview-overlay]').textContent).toContain('b');
  }));

  it('shows the name, card attributes, and acquisition fields in the preview', fakeAsync(async () => {
    const fixture = await createShowcase(
      [
        item({
          id: '初音未來 1/7 比例模型',
          attributes: { scale: '1/7' },
          acquisition: { acquiredAt: '2026-01-15T00:00:00Z', price: { amount: 12800, currency: 'TWD' }, vendor: null },
          rating: 9,
          storageLocation: '書房 A 櫃第二層',
        }),
      ],
      [{ id: 'c1', fields: [{ key: 'scale', label: '比例', showOnCard: true }] }],
    );

    const text = hoverCard(fixture)?.textContent ?? '';

    expect(text).toContain('初音未來 1/7 比例模型');
    expect(text).toContain('比例');
    expect(text).toContain('1/7');
    expect(text).toContain('2026-01-15');
    expect(text).toContain('12800 TWD');
    expect(text).toContain('書房 A 櫃第二層');
    expect(text).toContain('9 / 10');
  }));

  it('never shows the description in the preview', fakeAsync(async () => {
    const fixture = await createShowcase([
      item({ id: 'a', description: '這段描述不該出現在浮層裡' }),
    ]);

    expect(hoverCard(fixture)?.textContent).not.toContain('這段描述不該出現在浮層裡');
  }));

  it('starts the preview from the already-cached card image', fakeAsync(async () => {
    const fixture = await createShowcase([
      item({
        id: 'a',
        images: [
          { id: 'img1', path: 'o/a/img1-full.webp', cardPath: 'o/a/img1-card.webp', thumbPath: 'o/a/img1-thumb.webp', isPrimary: true, order: 0 },
        ],
      }),
    ]);

    const source = hoverCard(fixture)
      ?.querySelector<HTMLElement>('[data-preview-image]')
      ?.dataset['mediaSource'];

    expect(source).toBe(`${API_BASE}/media/o/a/img1-card.webp`);
  }));

  it('falls back to an initial in the preview when the item has no image', fakeAsync(async () => {
    const fixture = await createShowcase([item({ id: '初音' })]);
    const overlay = hoverCard(fixture);

    expect(overlay?.querySelector('[data-preview-image]')).toBeNull();
    expect(overlay?.querySelector('[data-preview-placeholder]')?.textContent?.trim()).toBe('初');
  }));

  it('does not show the preview outside the list tab', fakeAsync(async () => {
    const fixture = await createShowcase([item({ id: 'a', effectiveDisplayMode: 'Hero' })]);
    fixture.componentRef.setInput('view', 'hero');
    fixture.detectChanges();

    tick(200);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-preview-overlay]')).toBeNull();
  }));

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
