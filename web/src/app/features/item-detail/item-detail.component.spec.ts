import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { CatalogService } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import { IngestionService } from '../../core/api/ingestion.service';
import { IGDB_PROVIDER_KEY, ProviderService } from '../../core/api/provider.service';
import { NotificationService } from '../../core/notification.service';
import { CategoryDto, FetchedMetadataDto } from '../../core/models';
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
});
