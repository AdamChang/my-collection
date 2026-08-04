import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { CatalogService } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import { IngestionService } from '../../core/api/ingestion.service';
import {
  IGDB_PROVIDER_KEY,
  STEAM_PROVIDER_KEY,
  ProviderService,
} from '../../core/api/provider.service';
import { NotificationService } from '../../core/notification.service';
import { CategoryDto, FetchedMetadataDto, ItemDto, SyncJobDto } from '../../core/models';
import { IgdbSearchDialogComponent } from '../../shared/igdb-search-dialog/igdb-search-dialog.component';
import { ItemDetailComponent } from './item-detail.component';

describe('ItemDetailComponent', () => {
  const schemaCategory: CategoryDto = {
    id: 'figures',
    name: '模型',
    icon: 'robot',
    kind: 'Physical',
    isSystem: true,
    fields: [
      {
        key: 'brand',
        label: '廠商',
        type: 'Text',
        options: null,
        required: false,
        searchable: false,
        showOnCard: false,
      },
    ],
  };

  it('groups a new item into terminal panels', async () => {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => null } } },
        },
        { provide: CategoryService, useValue: { list: () => of([]) } },
        { provide: CatalogService, useValue: {} },
        { provide: IngestionService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => false } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-item-core]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.detail__fetch')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('button[type="submit"]')).toBeTruthy();
  });

  it('keeps one editor form when the selected category has schema fields', async () => {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => null } } },
        },
        { provide: CategoryService, useValue: { list: () => of([schemaCategory]) } },
        { provide: CatalogService, useValue: {} },
        { provide: IngestionService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => false } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();
    fixture.componentInstance.categoryId = schemaCategory.id;
    fixture.componentInstance.onCategoryChanged();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-item-schema]')).toBeTruthy();
    expect(fixture.nativeElement.querySelectorAll('form').length).toBe(1);
  });

  it('keeps the tag input focus indicator visible', async () => {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => null } } },
        },
        { provide: CategoryService, useValue: { list: () => of([]) } },
        { provide: CatalogService, useValue: {} },
        { provide: IngestionService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => false } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();
    const tagInput: HTMLInputElement = fixture.nativeElement.querySelector('app-tag-input input');

    tagInput.focus();

    expect(tagInput.matches(':focus-visible')).toBeTrue();
    expect(getComputedStyle(tagInput).outlineColor).toBe('rgb(32, 231, 255)');
    expect(getComputedStyle(tagInput).outlineStyle).toBe('solid');
  });

  /**
   * 沒有這道鎖，連點三下儲存就是三個 POST、三筆重複品項。
   * 這是正確性問題，不只是觀感。
   */
  it('locks the save button while the create request is in flight', async () => {
    const create = new Subject<unknown>();

    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => null } } },
        },
        { provide: CategoryService, useValue: { list: () => of([schemaCategory]) } },
        { provide: CatalogService, useValue: { create: () => create } },
        { provide: IngestionService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => false } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();
    fixture.componentInstance.categoryId = schemaCategory.id;
    fixture.componentInstance.name = '鋼彈';
    fixture.detectChanges();

    const save: HTMLButtonElement = fixture.nativeElement.querySelector('button[type="submit"]');
    expect(save.disabled).toBeFalse();

    save.click();
    fixture.detectChanges();

    expect(save.disabled).toBeTrue();
    expect(save.textContent).toContain('儲存中');
  });

  it('re-enables the fetch button after the lookup fails', async () => {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => null } } },
        },
        { provide: CategoryService, useValue: { list: () => of([]) } },
        { provide: CatalogService, useValue: {} },
        {
          provide: IngestionService,
          useValue: { fetchByUrl: () => throwError(() => new Error('502')) },
        },
        { provide: NotificationService, useValue: { success: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => false } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();
    fixture.componentInstance.fetchUrl = 'https://example.com/p/1';
    fixture.detectChanges();

    const fetch: HTMLButtonElement = fixture.nativeElement.querySelector('.detail__fetch button');
    fetch.click();
    fixture.detectChanges();

    expect(fetch.disabled).toBeFalse();
    expect(fetch.textContent).toContain('擷取');
  });

  it('stacks URL fetch and acquisition fields in a 390px viewport', async () => {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => null } } },
        },
        { provide: CategoryService, useValue: { list: () => of([schemaCategory]) } },
        { provide: CatalogService, useValue: {} },
        { provide: IngestionService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => false } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();
    fixture.componentInstance.categoryId = schemaCategory.id;
    fixture.componentInstance.onCategoryChanged();
    fixture.detectChanges();

    const frame = document.createElement('iframe');
    frame.style.width = '390px';
    frame.style.height = '844px';
    frame.style.border = '0';
    document.body.append(frame);

    try {
      const frameDocument = frame.contentDocument!;
      const styles = frameDocument.createElement('style');
      styles.textContent = Array.from(document.styleSheets)
        .flatMap((sheet) => Array.from(sheet.cssRules))
        .map((rule) => rule.cssText)
        .join('\n');
      frameDocument.head.append(styles);
      frameDocument.body.append(fixture.nativeElement.cloneNode(true));

      const frameWindow = frame.contentWindow!;
      const fetchPanel = frameDocument.querySelector('.detail__fetch')!;
      const acquisition = frameDocument.querySelector('.detail__acquisition')!;

      expect(frameWindow.getComputedStyle(fetchPanel).display).toBe('grid');
      expect(frameWindow.getComputedStyle(acquisition).gridTemplateColumns.split(' ')).toHaveSize(1);
    } finally {
      frame.remove();
    }
  });

  const igdbCategory: CategoryDto = {
    id: 'physical-games',
    name: '實體遊戲',
    icon: 'gamepad-2',
    kind: 'Physical',
    isSystem: true,
    fields: [
      { key: 'igdbId', label: 'IGDB ID', type: 'Number', options: null, required: false, searchable: false, showOnCard: false },
      { key: 'developer', label: '開發商', type: 'Text', options: null, required: false, searchable: true, showOnCard: false },
    ],
  };

  const witcher: FetchedMetadataDto = {
    provider: 'igdb',
    externalId: '1942',
    name: 'The Witcher 3: Wild Hunt',
    description: 'A story-driven adventure.',
    imageUrl: null,
    attributes: { igdbId: 1942, developer: 'CD Projekt RED', igdbRating: 93.5 },
  };

  async function createNewItemWithIgdb(available: boolean) {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => null } } } },
        { provide: CategoryService, useValue: { list: () => of([igdbCategory]) } },
        { provide: CatalogService, useValue: {} },
        { provide: IngestionService, useValue: { search: () => of([]) } },
        { provide: NotificationService, useValue: { success: () => undefined, error: () => undefined } },
        {
          provide: ProviderService,
          useValue: {
            supports: (key: string, capability: string) =>
              available && key === IGDB_PROVIDER_KEY && capability === 'Search',
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();

    return fixture;
  }

  it('hides the igdb entry point when the provider is not registered', async () => {
    const fixture = await createNewItemWithIgdb(false);

    expect(fixture.nativeElement.querySelector('[data-igdb-open]')).toBeNull();
  });

  /**
   * `@if` 只擋觸發按鈕，不擋對話框——後者刻意無條件渲染。
   * 包進 `@if` 的話內容要等下一次變更偵測才具現化，而 IgdbSearchDialogComponent 內部用的是
   * `viewChild.required`：「翻開關 + 在同一個 handler 裡呼叫 open()」會炸 NG0951。
   * 它在 showModal() 之前不佔任何視覺空間，無條件渲染零成本。
   */
  it('renders the dialog unconditionally even when the entry points are hidden', async () => {
    const fixture = await createNewItemWithIgdb(false);

    expect(fixture.nativeElement.querySelector('dialog')).toBeTruthy();
  });

  /** 品類決定哪些欄位能寫。沒選品類就搜尋，等於不知道要把結果放進哪個 schema。 */
  it('disables the igdb button until a category is chosen', async () => {
    const fixture = await createNewItemWithIgdb(true);

    const button: HTMLButtonElement = fixture.nativeElement.querySelector('[data-igdb-open]');
    expect(button.disabled).toBeTrue();

    fixture.componentInstance.categoryId = igdbCategory.id;
    fixture.componentInstance.onCategoryChanged();
    fixture.detectChanges();

    expect(button.disabled).toBeFalse();
  });

  /** 已經有一個改寫進行中就不該讓使用者再開搜尋——同一筆品項不該有並行的改寫。 */
  it('keeps the igdb button locked while another write is in flight', async () => {
    const fixture = await createNewItemWithIgdb(true);
    fixture.componentInstance.categoryId = igdbCategory.id;
    fixture.componentInstance.onCategoryChanged();
    fixture.componentInstance.fetching.set(true);
    fixture.detectChanges();

    const button: HTMLButtonElement = fixture.nativeElement.querySelector('[data-igdb-open]');

    expect(button.disabled).toBeTrue();
  });

  /**
   * 這是整個功能最容易靜默壞掉的地方。品類沒宣告 igdbRating，
   * 若它跟著送出去，後端 AttributeValidator 直接回 400，而且錯誤訊息與搜尋毫無關聯。
   */
  it('drops attributes the chosen category has not declared', async () => {
    const fixture = await createNewItemWithIgdb(true);
    fixture.componentInstance.categoryId = igdbCategory.id;
    fixture.componentInstance.onCategoryChanged();
    fixture.detectChanges();

    fixture.componentInstance.applyMetadata(witcher, 'prefill');

    expect(Object.keys(fixture.componentInstance.attributes()).sort()).toEqual(['developer', 'igdbId']);
  });

  it('overwrites the name and description in prefill mode', async () => {
    const fixture = await createNewItemWithIgdb(true);
    fixture.componentInstance.categoryId = igdbCategory.id;
    fixture.componentInstance.onCategoryChanged();

    fixture.componentInstance.applyMetadata(witcher, 'prefill');

    expect(fixture.componentInstance.name).toBe('The Witcher 3: Wild Hunt');
    expect(fixture.componentInstance.description).toBe('A story-driven adventure.');
  });

  /** 既有品項的名稱是使用者在庫裡認得的那個，不該被英文原名蓋掉。 */
  it('keeps the name and description untouched in bind mode', async () => {
    const fixture = await createNewItemWithIgdb(true);
    fixture.componentInstance.categoryId = igdbCategory.id;
    fixture.componentInstance.onCategoryChanged();
    fixture.componentInstance.name = '巫師三';
    fixture.componentInstance.description = '我自己寫的心得';

    fixture.componentInstance.applyMetadata(witcher, 'bind');

    expect(fixture.componentInstance.name).toBe('巫師三');
    expect(fixture.componentInstance.description).toBe('我自己寫的心得');
    expect(fixture.componentInstance.attributes()['igdbId']).toBe(1942);
  });

  /**
   * 屬性的政策與 name/description 相反：綁定就是要用外部來源刷新遊戲事實，
   * 既有值該被蓋掉而不是保留。合併方向寫反了不會有任何測試變紅，只會靜默留著舊資料。
   */
  it('lets the fetched attributes overwrite the existing ones in bind mode', async () => {
    const fixture = await createNewItemWithIgdb(true);
    fixture.componentInstance.categoryId = igdbCategory.id;
    fixture.componentInstance.onCategoryChanged();
    fixture.componentInstance.attributes.set({ igdbId: 999, developer: '舊的開發商' });

    fixture.componentInstance.applyMetadata(witcher, 'bind');

    expect(fixture.componentInstance.attributes()['igdbId']).toBe(1942);
    expect(fixture.componentInstance.attributes()['developer']).toBe('CD Projekt RED');
  });

  /** 套用結果要讓使用者看得見。只有送出的 payload 對、畫面卻空白，等於沒套用。 */
  it('shows the applied attributes in the rendered schema form', async () => {
    const fixture = await createNewItemWithIgdb(true);
    fixture.componentInstance.categoryId = igdbCategory.id;
    fixture.componentInstance.onCategoryChanged();
    fixture.detectChanges();

    fixture.componentInstance.applyMetadata(witcher, 'prefill');
    fixture.detectChanges();

    const input: HTMLInputElement = fixture.nativeElement.querySelector('[data-field="igdbId"]');
    expect(input.value).toBe('1942');
  });

  /**
   * 網址擷取與 IGDB 搜尋共用 applyMetadata。這條路徑不是純重構——
   * 舊碼把 metadata.attributes 整個丟掉，現在它也會帶入品類宣告的屬性。
   */
  it('applies url-fetched metadata through the shared path', async () => {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => null } } } },
        { provide: CategoryService, useValue: { list: () => of([igdbCategory]) } },
        { provide: CatalogService, useValue: {} },
        { provide: IngestionService, useValue: { fetchByUrl: () => of(witcher) } },
        { provide: NotificationService, useValue: { success: () => undefined, error: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => false } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();
    fixture.componentInstance.categoryId = igdbCategory.id;
    fixture.componentInstance.onCategoryChanged();
    fixture.componentInstance.fetchUrl = 'https://example.com/p/1';
    fixture.detectChanges();

    fixture.nativeElement.querySelector('.detail__fetch button').click();

    expect(fixture.componentInstance.name).toBe('The Witcher 3: Wild Hunt');
    expect(fixture.componentInstance.description).toBe('A story-driven adventure.');
    expect(Object.keys(fixture.componentInstance.attributes()).sort()).toEqual(['developer', 'igdbId']);
  });

  const steamItem: ItemDto = {
    id: '65b0000000000000000000aa',
    categoryId: 'physical-games',
    name: 'Team Fortress 2',
    description: null,
    images: [],
    tags: [],
    isShowcased: false,
    source: 'Steam',
    externalRef: { provider: 'steam', externalId: '440', url: null, lastSyncedAt: '2026-07-01T00:00:00Z' },
    acquisition: null,
    locationId: null,
    attributes: {},
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: '2026-07-01T00:00:00Z',
  };

  async function createExistingItem(item: ItemDto) {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => item.id } } } },
        { provide: CategoryService, useValue: { list: () => of([igdbCategory]) } },
        { provide: CatalogService, useValue: { get: () => of(item) } },
        { provide: IngestionService, useValue: { search: () => of([]) } },
        { provide: NotificationService, useValue: { success: () => undefined, error: () => undefined } },
        {
          provide: ProviderService,
          useValue: {
            supports: (key: string, capability: string) =>
              key === IGDB_PROVIDER_KEY && capability === 'Search',
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();

    return fixture;
  }

  it('offers a refetch button for a steam-synced item', async () => {
    const fixture = await createExistingItem(steamItem);

    expect(fixture.nativeElement.querySelector('[data-igdb-refetch]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-igdb-bind]')).toBeNull();
  });

  it('offers a bind button for an item with no usable external id', async () => {
    const fixture = await createExistingItem({ ...steamItem, source: 'Manual', externalRef: null });

    expect(fixture.nativeElement.querySelector('[data-igdb-bind]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-igdb-refetch]')).toBeNull();
  });

  /**
   * OpenGraph 品項也有 externalRef，但後端會組出 opengraph:xxx 這種 IGDB 兩個前綴都
   * parse 不了的識別碼，結果是進 failed。把它當成可定址，就是給使用者一顆按了只會失敗的按鈕。
   */
  it('does not treat an opengraph reference as addressable', async () => {
    const fixture = await createExistingItem({
      ...steamItem,
      source: 'OpenGraph',
      externalRef: { provider: 'opengraph', externalId: 'https://shop/x', url: null, lastSyncedAt: '2026-07-01T00:00:00Z' },
    });

    expect(fixture.nativeElement.querySelector('[data-igdb-bind]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-igdb-refetch]')).toBeNull();
  });

  it('treats an item that already carries the marker as addressable', async () => {
    const fixture = await createExistingItem({
      ...steamItem,
      source: 'Manual',
      externalRef: null,
      attributes: { igdbId: 1942 },
    });

    expect(fixture.nativeElement.querySelector('[data-igdb-refetch]')).toBeTruthy();
  });

  /**
   * Steam 商店補完的入口只在 provider 宣告 Enrich 能力時出現，
   * 與 IGDB 的 Search 能力是兩件事——上面那組測試把 supports 限定成 IGDB/Search，
   * 所以這裡必須自己餵一組。
   */
  async function createWithSteamEnrich(item: ItemDto, enrich = () => of(runningJob)) {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => item.id } } } },
        { provide: CategoryService, useValue: { list: () => of([igdbCategory]) } },
        { provide: CatalogService, useValue: { get: () => of(item) } },
        { provide: IngestionService, useValue: { search: () => of([]), enrich } },
        { provide: NotificationService, useValue: { success: () => undefined, error: () => undefined } },
        {
          provide: ProviderService,
          useValue: {
            supports: (key: string, capability: string) =>
              key === STEAM_PROVIDER_KEY && capability === 'Enrich',
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();

    return fixture;
  }

  const runningJob = {
    id: 'j1', provider: 'steam', status: 'Running',
    created: 0, updated: 0, failed: 0, skipped: 0,
    error: null, startedAt: '2026-08-04T03:00:00Z', finishedAt: null,
  };

  it('offers a steam refetch button for a steam-synced item', async () => {
    const fixture = await createWithSteamEnrich(steamItem);

    expect(fixture.nativeElement.querySelector('[data-steam-refetch]')).toBeTruthy();
  });

  /** 手動建檔的實體遊戲靠 IGDB 反查寫進來的 steamAppId 定址。 */
  it('offers a steam refetch button for an item that only carries a steam app id', async () => {
    const fixture = await createWithSteamEnrich({
      ...steamItem,
      source: 'Manual',
      externalRef: null,
      attributes: { steamAppId: 1245620 },
    });

    expect(fixture.nativeElement.querySelector('[data-steam-refetch]')).toBeTruthy();
  });

  it('hides the steam refetch button when the item has no steam identity', async () => {
    const fixture = await createWithSteamEnrich({
      ...steamItem,
      source: 'Manual',
      externalRef: null,
      attributes: {},
    });

    expect(fixture.nativeElement.querySelector('[data-steam-refetch]')).toBeNull();
  });

  it('asks steam to enrich just this item', async () => {
    const calls: unknown[][] = [];
    const fixture = await createWithSteamEnrich(steamItem, (...args: unknown[]) => {
      calls.push(args);
      return of(runningJob);
    });

    fixture.nativeElement.querySelector('[data-steam-refetch]').click();

    expect(calls).toEqual([[STEAM_PROVIDER_KEY, [steamItem.id]]]);
  });

  /**
   * itemId() 來自路由，第一次變更偵測就是真值；item() 卻要等 catalog.get() 回來。
   * 正式環境那是一段真實的非同步視窗，少了 null 檢查整頁在視窗內直接炸。
   */
  it('renders while the item has not loaded yet', async () => {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => steamItem.id } } } },
        { provide: CategoryService, useValue: { list: () => of([igdbCategory]) } },
        { provide: CatalogService, useValue: { get: () => new Subject<ItemDto>() } },
        { provide: IngestionService, useValue: { search: () => of([]) } },
        { provide: NotificationService, useValue: { success: () => undefined, error: () => undefined } },
        {
          provide: ProviderService,
          useValue: {
            supports: (key: string, capability: string) =>
              key === IGDB_PROVIDER_KEY && capability === 'Search',
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-igdb-bind]')).toBeTruthy();
  });

  /** 這兩顆是既有品項專屬的。漏到新增頁的話，bind 那顆不要求先選品類，正好繞過該規則。 */
  it('keeps the existing-item entry points off the new item page', async () => {
    const fixture = await createNewItemWithIgdb(true);

    expect(fixture.nativeElement.querySelector('[data-igdb-refetch]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-igdb-bind]')).toBeNull();
  });

  it('keeps the bind button locked while another write is in flight', async () => {
    const fixture = await createExistingItem({ ...steamItem, source: 'Manual', externalRef: null });
    fixture.componentInstance.saving.set(true);
    fixture.detectChanges();

    const button: HTMLButtonElement = fixture.nativeElement.querySelector('[data-igdb-bind]');

    expect(button.disabled).toBeTrue();
  });

  function enrichJob(outcome: Partial<SyncJobDto>): SyncJobDto {
    return {
      id: 'j1', provider: 'igdb', status: 'Succeeded',
      created: 0, updated: 0, failed: 0, skipped: 0,
      error: null, startedAt: '2026-08-01T03:00:00Z', finishedAt: '2026-08-01T03:00:01Z',
      ...outcome,
    };
  }

  /**
   * 通報通道分開收集：把 error 換成 success 訊息文字不會變，只有通道會變。
   * enrich 的參數也記下來——不給 itemIds 會變成批次補完 50 筆使用者沒選的品項。
   */
  async function createRefetchable(response: Observable<SyncJobDto>) {
    const successes: string[] = [];
    const errors: string[] = [];
    const enrichCalls: unknown[][] = [];
    const gets = { count: 0 };

    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => steamItem.id } } } },
        { provide: CategoryService, useValue: { list: () => of([igdbCategory]) } },
        {
          provide: CatalogService,
          useValue: {
            get: () => {
              gets.count++;
              return of(steamItem);
            },
          },
        },
        {
          provide: IngestionService,
          useValue: {
            search: () => of([]),
            enrich: (...args: unknown[]) => {
              enrichCalls.push(args);
              return response;
            },
          },
        },
        {
          provide: NotificationService,
          useValue: {
            success: (m: string) => successes.push(m),
            error: (m: string) => errors.push(m),
          },
        },
        {
          provide: ProviderService,
          useValue: {
            supports: (key: string, capability: string) =>
              key === IGDB_PROVIDER_KEY && capability === 'Search',
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();

    return { fixture, successes, errors, enrichCalls, gets };
  }

  /** 說「完成」會讓使用者以為資料已更新。查無對應時什麼都沒變。 */
  it('reports a lookup miss instead of claiming success', async () => {
    const h = await createRefetchable(of(enrichJob({ skipped: 1 })));

    h.fixture.nativeElement.querySelector('[data-igdb-refetch]').click();

    expect(h.errors).toEqual(['IGDB 查無對應，未變更任何欄位。']);
    expect(h.successes).toEqual([]);
    // 少了 return 會多打一次 GET，把使用者未儲存的編輯洗掉。
    expect(h.gets.count).toBe(1);
  });

  /**
   * IGDB 限流、憑證過期、服務中斷都會讓後端把整個 chunk 記成 failed，
   * 而 job.Status 仍是 Succeeded——走的是 next 不是 error，errorInterceptor 不會出手。
   * 這種情況該叫使用者稍後重試，不是告訴他已經更新完成。
   */
  it('reports a failed lookup as a failure, not an update', async () => {
    const h = await createRefetchable(of(enrichJob({ failed: 1 })));

    h.fixture.nativeElement.querySelector('[data-igdb-refetch]').click();

    expect(h.errors).toEqual(['IGDB 查詢失敗，請稍後再試。']);
    expect(h.successes).toEqual([]);
    expect(h.gets.count).toBe(1);
  });

  /**
   * 全零是「找到了遊戲，但沒有任何欄位可寫」——後端 MongoItemEnrichWriter.ApplyAsync
   * 在 enrichment 被濾空時直接 return 0（品類沒宣告任何 IGDB key、且品項描述非空）。
   * 條件若只認 skipped，這個組合會掉回「已從 IGDB 更新。」並多打一次 GET。
   */
  it('reports no updated fields instead of claiming success on an empty result', async () => {
    const h = await createRefetchable(of(enrichJob({})));

    h.fixture.nativeElement.querySelector('[data-igdb-refetch]').click();

    expect(h.successes).toEqual([]);
    expect(h.errors).toEqual(['IGDB 查無對應，未變更任何欄位。']);
    expect(h.gets.count).toBe(1);
  });

  it('reports the update and reloads the item when the lookup succeeds', async () => {
    const h = await createRefetchable(of(enrichJob({ updated: 1 })));

    h.fixture.nativeElement.querySelector('[data-igdb-refetch]').click();

    expect(h.successes).toEqual(['已從 IGDB 更新。']);
    expect(h.errors).toEqual([]);
    // 不重載的話畫面停在舊資料，使用者看不到剛抓回來的欄位。
    expect(h.gets.count).toBe(2);
    // 不給 itemIds 就會變成批次補完使用者沒選的品項。
    expect(h.enrichCalls).toEqual([[IGDB_PROVIDER_KEY, [steamItem.id]]]);
  });

  /** 沒有這道鎖，連點三下就是三個 enrich 請求。與儲存那道鎖是同一個理由。 */
  it('keeps the refetch button locked while the enrich request is in flight', async () => {
    const h = await createRefetchable(new Subject<SyncJobDto>());

    const button: HTMLButtonElement = h.fixture.nativeElement.querySelector('[data-igdb-refetch]');
    expect(button.disabled).toBeFalse();

    button.click();
    h.fixture.detectChanges();

    expect(button.disabled).toBeTrue();
    expect(button.textContent).toContain('抓取中');
  });

  /**
   * 按鈕與對話框的接線只有真的按下去才會驗到。把 <app-igdb-search-dialog> 包進 @if
   * 看起來像合理的最佳化，卻會讓 viewChild.required 在使用者按下按鈕的那一刻才炸。
   */
  it('opens the dialog when the bind button is clicked', async () => {
    const fixture = await createExistingItem({ ...steamItem, source: 'Manual', externalRef: null });

    fixture.nativeElement.querySelector('[data-igdb-bind]').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('dialog').open).toBeTrue();
  });

  /** 既有品項走 bind，模板的三元運算若寫死 'prefill'，使用者的品名就會被英文原名蓋掉。 */
  it('binds instead of prefilling when the dialog reports a pick on an existing item', async () => {
    const fixture = await createExistingItem({ ...steamItem, source: 'Manual', externalRef: null });

    const dialog = fixture.debugElement.query(By.directive(IgdbSearchDialogComponent))
      .componentInstance as IgdbSearchDialogComponent;
    dialog.select.emit(witcher);

    expect(fixture.componentInstance.name).toBe('Team Fortress 2');
    expect(fixture.componentInstance.attributes()['igdbId']).toBe(1942);
  });
});
