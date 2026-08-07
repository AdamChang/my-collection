import { Subject, of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { CatalogService, ItemSearchOptions } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import { EMPTY_CATALOG_QUERY } from '../../core/catalog-query';
import { CatalogReturnPointService } from '../../core/catalog-return-point.service';
import { CategoryDto, CategoryFieldDto, ItemDto } from '../../core/models';
import { CatalogComponent } from './catalog.component';

function field(key: string, overrides: Partial<CategoryFieldDto> = {}): CategoryFieldDto {
  return {
    key,
    label: key,
    type: 'Text',
    options: null,
    required: false,
    searchable: true,
    showOnCard: false,
    ...overrides,
  };
}

const gameCategory: CategoryDto = {
  id: 'game',
  name: '實體遊戲',
  icon: 'gamepad-2',
  kind: 'Physical',
  isSystem: true,
  defaultDisplayMode: 'List',
  // region 是給「未設定」測試用的第二個 searchable 欄位——品類沒宣告的 key 會被剪掉，
  // 所以 fixture 必須真的宣告它，否則測到的是剪枝而不是解析。
  fields: [field('platform', { label: '平台', showOnCard: true }), field('region', { label: '區碼' })],
};

interface NavigateOptions {
  categories?: CategoryDto[];
  items?: ItemDto[];
  /** 先擺好一個返回點，模擬「使用者從品項頁回到列表」。 */
  seed?: (returnPoint: CatalogReturnPointService) => void;
  /** 讓每次查詢停在半空中，測試才控制得了回應的先後順序。 */
  defer?: boolean;
}

type SearchResult = { items: ItemDto[]; total: number; page: number; pageSize: number };

/**
 * 篩選條件的真實來源是網址，所以測試也必須從網址進去——直接 createComponent
 * 會繞過整條 query param 解析，驗不到這個功能的核心。
 */
async function navigate(url: string, options: NavigateOptions = {}) {
  const searches: ItemSearchOptions[] = [];
  const responses: Subject<SearchResult>[] = [];
  const items = options.items ?? [];

  TestBed.configureTestingModule({
    providers: [
      provideRouter([{ path: 'catalog', component: CatalogComponent }]),
      {
        provide: CatalogService,
        useValue: {
          tags: () => of([]),
          platforms: () => of(['Switch']),
          search: (searchOptions: ItemSearchOptions) => {
            searches.push(searchOptions);

            if (options.defer) {
              const response = new Subject<SearchResult>();
              responses.push(response);
              return response;
            }

            return of({ items, total: items.length, page: 1, pageSize: 24 });
          },
        },
      },
      { provide: CategoryService, useValue: { list: () => of(options.categories ?? []) } },
    ],
  });

  const returnPoint = TestBed.inject(CatalogReturnPointService);
  options.seed?.(returnPoint);

  const harness = await RouterTestingHarness.create();
  const component = await harness.navigateByUrl(url, CatalogComponent);

  return { harness, component, searches, responses, returnPoint, latest: () => searches.at(-1)! };
}

function page(ids: string[], total = ids.length): SearchResult {
  return { items: ids.map(item), total, page: 1, pageSize: 24 };
}

function item(id: string): ItemDto {
  return {
    id,
    categoryId: 'game',
    name: id,
    description: null,
    images: [],
    tags: [],
    isShowcased: false,
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
  } as unknown as ItemDto;
}

describe('CatalogComponent', () => {
  // 返回點活在 sessionStorage，而它跨測試共用——不清掉的話上一條測試的頁數會漏進下一條。
  beforeEach(() => sessionStorage.clear());
  afterAll(() => sessionStorage.clear());

  it('takes every filter from the url', async () => {
    const { latest } = await navigate(
      '/catalog?search=zelda&categoryId=game&tags=RPG&tags=%E5%B7%B2%E9%80%9A&attr.platform=Switch&missingAttrs=region',
      { categories: [gameCategory] },
    );

    expect(latest().search).toBe('zelda');
    expect(latest().categoryId).toBe('game');
    expect(latest().tags).toEqual(['RPG', '已通']);
    expect(latest().attributes).toEqual({ platform: 'Switch' });
    expect(latest().missingAttributes).toEqual(['region']);
  });

  it('writes every filter change back to the url', async () => {
    const { harness, component } = await navigate('/catalog', { categories: [gameCategory] });
    const router = TestBed.inject(Router);

    component.setAttributeFilter('platform', 'Switch');
    await harness.fixture.whenStable();
    component.toggleTag('RPG');
    await harness.fixture.whenStable();

    expect(router.url).toBe('/catalog?tags=RPG&attr.platform=Switch');
  });

  /** 重複 key 給的是 string[]，單一 key 給的是 string——同一條路要收得住兩種形狀。 */
  it('reads a repeated tag as a list', async () => {
    const { latest } = await navigate('/catalog?tags=RPG&tags=SLG');

    expect(latest().tags).toEqual(['RPG', 'SLG']);
  });

  it('reads a single tag as a one-item list', async () => {
    const { latest } = await navigate('/catalog?tags=RPG');

    expect(latest().tags).toEqual(['RPG']);
  });

  /**
   * attribute 的 key 由品類宣告、可能含 `.`。後端切的是固定長度的 `attr.` 前綴
   * （`kv.Key[5..]`），前端若改用 split('.') 就會在這種 key 上解出不同結果。
   */
  it('keeps a dot inside an attribute key', async () => {
    const { latest } = await navigate('/catalog?attr.disc.region=NTSC-J');

    expect(latest().attributes).toEqual({ 'disc.region': 'NTSC-J' });
  });

  /**
   * 篩選跨頁存活之後就會出現「我打開庫存，東西怎麼這麼少」。桌機看得到左側面板的值，
   * 但 760px 以下面板變成 static 且排在結果上方，捲下去就完全看不到自己設了什麼。
   */
  it('offers no way to clear filters when none are active', async () => {
    const { harness } = await navigate('/catalog');

    expect(harness.routeNativeElement!.querySelector('[data-clear-filters]')).toBeNull();
  });

  it('clears every filter at once, including the 未設定 tick', async () => {
    const { harness } = await navigate('/catalog?search=zelda&tags=RPG&missingAttrs=platform', {
      categories: [gameCategory],
    });
    const router = TestBed.inject(Router);

    const clear: HTMLButtonElement =
      harness.routeNativeElement!.querySelector('[data-clear-filters]')!;
    expect(clear).toBeTruthy();

    clear.click();
    await harness.fixture.whenStable();

    expect(router.url).toBe('/catalog');
  });

  /** 三頁 × 24 = 72 筆，一次請求拿回來——不是三次請求，也不是回到第一頁。 */
  it('comes back to every page that had been loaded', async () => {
    const { latest } = await navigate('/catalog?attr.platform=PS5', {
      seed: (returnPoint) =>
        returnPoint.remember({ ...EMPTY_CATALOG_QUERY, attributes: { platform: 'PS5' } }, 3),
    });

    expect(latest().page).toBe(1);
    expect(latest().pageSize).toBe(72);
  });

  it('starts from the first page when the url is not the list that was remembered', async () => {
    const { latest } = await navigate('/catalog?attr.platform=Switch', {
      seed: (returnPoint) =>
        returnPoint.remember({ ...EMPTY_CATALOG_QUERY, attributes: { platform: 'PS5' } }, 3),
    });

    expect(latest().pageSize).toBe(24);
  });

  it('remembers each extra page as it is loaded', async () => {
    const { component, returnPoint } = await navigate('/catalog');

    component.loadMore();

    expect(returnPoint.resume(EMPTY_CATALOG_QUERY).pages).toBe(2);
  });

  /**
   * 真實捲動在 TestBed 裡驗不到（沒有版面），所以只驗「對正確的那張卡片下了指令」。
   * 捲動本身需要人工確認。
   */
  it('scrolls back to the card that was clicked', async () => {
    const scrollIntoView = spyOn(Element.prototype, 'scrollIntoView');

    const { harness } = await navigate('/catalog', {
      items: [item('i1'), item('i2'), item('i3')],
      seed: (returnPoint) => {
        returnPoint.remember(EMPTY_CATALOG_QUERY, 1);
        returnPoint.rememberAnchor('i2');
      },
    });

    harness.detectChanges();
    await harness.fixture.whenStable();

    const target = scrollIntoView.calls.mostRecent()?.object as HTMLElement;
    expect(target?.getAttribute('data-item-id')).toBe('i2');
  });

  /** 錨點多半是使用者自己剛才的編輯弄不見的——靜靜捲到頂端，不提示。 */
  it('scrolls nowhere when the anchor is no longer in the results', async () => {
    const scrollIntoView = spyOn(Element.prototype, 'scrollIntoView');

    const { harness } = await navigate('/catalog', {
      items: [item('i1'), item('i3')],
      seed: (returnPoint) => {
        returnPoint.remember(EMPTY_CATALOG_QUERY, 1);
        returnPoint.rememberAnchor('i2');
      },
    });

    harness.detectChanges();
    await harness.fixture.whenStable();

    expect(scrollIntoView).not.toHaveBeenCalled();
  });

  /**
   * 搜尋框每一次按鍵都送一次查詢。慢的那一次若後到，畫面會永久停在舊的結果集上
   * ——網址與輸入框都寫著新的關鍵字，沒有任何東西會再去糾正它。
   */
  it('ignores a response that a newer request has already superseded', async () => {
    const { harness, component, responses } = await navigate('/catalog', { defer: true });

    component.search = 'zeld';
    component.applySearch();
    await harness.fixture.whenStable();

    component.search = 'zelda';
    component.applySearch();
    await harness.fixture.whenStable();

    responses.at(-1)!.next(page(['zelda-hit'], 1));
    responses.at(-2)!.next(page(['stale-hit'], 99));

    expect(component.items().map((i) => i.id)).toEqual(['zelda-hit']);
    expect(component.total()).toBe(1);
  });

  /**
   * 0002 立下的規則是「不留下畫面上看不到、卻仍在生效的隱形篩選」。品類把 platform
   * 欄位拿掉之後，舊網址上的 missingAttrs=platform 就變成這種東西：沒有任何控制項
   * 渲染得出來，結果卻是空的，使用者看不出原因。
   */
  it('drops a filter the category no longer declares', async () => {
    const withoutPlatform: CategoryDto = { ...gameCategory, fields: [] };

    const { harness, latest } = await navigate('/catalog?categoryId=game&missingAttrs=platform', {
      categories: [withoutPlatform],
    });
    await harness.fixture.whenStable();

    expect(latest().missingAttributes).toEqual([]);
    expect(TestBed.inject(Router).url).toBe('/catalog?categoryId=game');
  });

  it('renders filters as a control panel and keeps the create action', async () => {
    const { harness } = await navigate('/catalog');

    expect(harness.routeNativeElement!.querySelector('[data-catalog-controls]')).toBeTruthy();
    expect(harness.routeNativeElement!.querySelector('a[href="/items/new"]')).toBeTruthy();
  });

  it('asks for platform-less items and drops any platform value when 未設定 is ticked', async () => {
    const { harness, component, latest } = await navigate('/catalog', { categories: [gameCategory] });

    component.setAttributeFilter('platform', 'Switch');
    await harness.fixture.whenStable();
    component.toggleMissingFilter('platform');
    await harness.fixture.whenStable();

    expect(latest().missingAttributes).toEqual(['platform']);
    expect(latest().attributes?.['platform']).toBeUndefined();
  });

  it('stops asking for platform-less items once the filter is no longer available', async () => {
    const { harness, component, latest } = await navigate('/catalog', { categories: [gameCategory] });

    component.toggleMissingFilter('platform');
    await harness.fixture.whenStable();
    component.onCategoryChange('no-such-category');
    await harness.fixture.whenStable();

    expect(latest().missingAttributes).toEqual([]);
  });
});
