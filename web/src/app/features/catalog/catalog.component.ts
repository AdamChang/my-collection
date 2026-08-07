import { Component, ElementRef, Injector, afterNextRender, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CatalogService } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import {
  CatalogQuery,
  EMPTY_CATALOG_QUERY,
  isEmptyCatalogQuery,
  parseCatalogQuery,
  toCatalogQueryParams,
} from '../../core/catalog-query';
import { CatalogReturnPointService } from '../../core/catalog-return-point.service';
import { CategoryDto, CategoryFieldDto, ItemDto } from '../../core/models';
import { ItemCardComponent } from '../../shared/item-card/item-card.component';

@Component({
  selector: 'app-catalog',
  imports: [FormsModule, ItemCardComponent, RouterLink],
  template: `
    <div class="catalog">
      <aside class="catalog__filters mc-panel" data-catalog-controls>
        <div class="mc-eyebrow">FILTER MATRIX</div>
        <h2>篩選控制台</h2>

        @if (hasActiveFilters()) {
          <button type="button" class="catalog__clear" data-clear-filters (click)="clearFilters()">
            清除全部篩選
          </button>
        }

        <label>搜尋<input type="search" [(ngModel)]="search" (ngModelChange)="applySearch()" /></label>

        <label>
          品類
          <select [ngModel]="categoryId()" (ngModelChange)="onCategoryChange($event)">
            <option value="">全部</option>
            @for (category of categories(); track category.id) {
              <option [value]="category.id">{{ category.name }}</option>
            }
          </select>
        </label>

        @for (field of searchableFields(); track field.key) {
          @if (field.key === 'platform') {
            <!-- label 不可巢狀，所以這一格用 div 包住「選值」與「選沒有值」兩個控制項。 -->
            <div class="catalog__filter">
              <label>
                {{ field.label }}
                <input type="text"
                       list="attr_platform_options"
                       [(ngModel)]="platformDraft"
                       (change)="commitPlatformFilter()"
                       [disabled]="isMissingFilter('platform')"
                       name="attr_platform" />
                <datalist id="attr_platform_options">
                  @for (option of platformOptions(); track option) {
                    <option [value]="option"></option>
                  }
                </datalist>
              </label>
              <label class="catalog__missing">
                <input type="checkbox"
                       [checked]="isMissingFilter('platform')"
                       (change)="toggleMissingFilter('platform')" />
                未設定
              </label>
            </div>
          } @else {
            <label>
              {{ field.label }}
              @if (field.type === 'Select') {
                <select [ngModel]="filterValue(field.key)"
                        (ngModelChange)="setAttributeFilter(field.key, $event)"
                        [name]="'attr_' + field.key">
                  <option value="">全部</option>
                  @for (option of field.options ?? []; track option) {
                    <option [value]="option">{{ option }}</option>
                  }
                </select>
              } @else {
                <input type="text"
                       [ngModel]="filterValue(field.key)"
                       (ngModelChange)="setAttributeFilter(field.key, $event)"
                       [name]="'attr_' + field.key" />
              }
            </label>
          }
        }

        <fieldset>
          <legend>標籤</legend>
          @for (tag of allTags(); track tag) {
            <label class="catalog__tag">
              <input type="checkbox" [checked]="selectedTags().includes(tag)" (change)="toggleTag(tag)" />
              {{ tag }}
            </label>
          }
        </fieldset>
      </aside>

      <section class="catalog__results">
        <header class="catalog__results-header">
          <div>
            <div class="mc-eyebrow">CATALOG / QUERY RESULTS</div>
            <h1>收藏目錄</h1>
            <span>{{ total() }} 件</span>
          </div>
          <a routerLink="/items/new">新增品項</a>
        </header>

        <div class="catalog__grid">
          @for (item of items(); track item.id) {
            <app-item-card [item]="item"
                           [cardFields]="fieldsFor(item.categoryId)"
                           (click)="rememberAnchor(item.id)" />
          }
        </div>

        @if (items().length < total()) {
          <button type="button" (click)="loadMore()">載入更多</button>
        }
      </section>
    </div>
  `,
  styles: `
    .catalog { display: grid; grid-template-columns: 16rem 1fr; gap: 1.5rem; align-items: start; }
    .catalog__filters { display: grid; gap: 0.75rem; position: sticky; top: 1rem; }
    .catalog__filters h2, .catalog__results-header h1 { margin: 0; }
    .catalog__clear { justify-self: start; }
    .catalog__results-header { display: flex; justify-content: space-between; align-items: end; gap: 1rem; margin-bottom: 1rem; }
    .catalog__results-header span { display: block; margin-top: 0.35rem; color: var(--mc-text-muted); }
    .catalog__grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 1rem; }
    .catalog__filter { display: grid; gap: 0.35rem; }
    .catalog__missing { display: flex; align-items: center; gap: 0.35rem; font-size: 0.85rem; }
    .catalog__tag { display: block; font-size: 0.85rem; }
    @media (max-width: 760px) {
      .catalog { grid-template-columns: 1fr; }
      .catalog__filters { position: static; }
    }
  `,
})
export class CatalogComponent {
  private readonly catalog = inject(CatalogService);
  private readonly categoryApi = inject(CategoryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly returnPoint = inject(CatalogReturnPointService);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly injector = inject(Injector);

  private static readonly PAGE_SIZE = 24;

  readonly items = signal<ItemDto[]>([]);
  readonly total = signal(0);
  readonly categories = signal<CategoryDto[]>([]);
  readonly allTags = signal<string[]>([]);
  readonly platformOptions = signal<string[]>([]);

  /** 篩選條件的真實來源是網址；這個 signal 只是它在元件裡的投影。 */
  readonly query = signal<CatalogQuery>(EMPTY_CATALOG_QUERY);

  readonly categoryId = computed(() => this.query().categoryId);
  readonly selectedTags = computed(() => this.query().tags);
  readonly attributeFilters = computed(() => this.query().attributes);

  /** 要求「未設定」的欄位 key。與 attributeFilters 是兩件事：一個選值，一個選「沒有值」。 */
  readonly missingAttributes = computed(() => this.query().missingAttributes);

  readonly hasActiveFilters = computed(() => !isEmptyCatalogQuery(this.query()));

  /** 「全部」下唯一允許跨品類出現的欄位——見 docs/adr/0006。不是通用機制，加其他欄位需另外決策。 */
  private static readonly PLATFORM_FILTER_FIELD: CategoryFieldDto = {
    key: 'platform',
    label: '平台',
    type: 'Text',
    options: null,
    required: false,
    searchable: true,
    showOnCard: false,
  };

  readonly searchableFields = computed<CategoryFieldDto[]>(() =>
    this.searchableFieldsFor(this.categoryId()),
  );

  search = '';
  platformDraft = '';

  private page = 1;

  /**
   * 每次查詢的序號。搜尋框每按一次鍵就送一次查詢，慢的那一次若後到，
   * 畫面會永久停在舊的結果集上——網址與輸入框都寫著新的關鍵字，
   * 而沒有任何東西會再去糾正它。只有最新的那一次有資格寫回畫面。
   */
  private latestRequest = 0;

  constructor() {
    this.categoryApi.list().subscribe((c) => {
      this.categories.set(c);
      this.syncPlatformOptions();
      this.pruneUnavailableFilters();
    });
    this.catalog.tags().subscribe((t) => this.allTags.set(t));

    this.route.queryParams
      .pipe(takeUntilDestroyed())
      .subscribe((params) => this.applyQuery(parseCatalogQuery(params)));
  }

  /**
   * 網址變了就重來一次：套用條件、取用返回點、重新查詢。
   * 這裡刻意不剪枝條件——品類清單是非同步載入的，第一次進頁面時它還是空的，
   * 照著剪會把網址上帶進來的條件全部剪掉。剪枝是使用者換品類的後果，不是解析網址的後果。
   */
  private applyQuery(query: CatalogQuery): void {
    this.query.set(query);
    this.search = query.search;
    this.platformDraft = query.attributes['platform'] ?? '';
    this.syncPlatformOptions();

    const resumed = this.returnPoint.resume(query);
    this.page = resumed.pages;

    this.items.set([]);
    this.fetch(1, CatalogComponent.PAGE_SIZE * resumed.pages, false, resumed.anchorItemId);
    this.returnPoint.remember(query, resumed.pages);
    this.pruneUnavailableFilters();
  }

  /**
   * 剪掉目前品類 schema 不宣告的條件。0002 立下的規則是「不留下畫面上看不到、
   * 卻仍在生效的隱形篩選」——品類把某個欄位拿掉之後，舊網址上的條件就會變成
   * 這種東西：沒有任何控制項渲染得出來，結果卻是空的，使用者看不出原因。
   *
   * 品類清單尚未到齊時什麼都不做：那時 schema 是「還不知道」，不是「沒宣告」，
   * 照著剪會把網址上帶進來的條件全部剪掉。
   */
  private pruneUnavailableFilters(): void {
    if (this.categories().length === 0) {
      return;
    }

    const current = this.query();
    const allowed = new Set(this.searchableFields().map((f) => f.key));
    const attributes = Object.fromEntries(
      Object.entries(current.attributes).filter(([key]) => allowed.has(key)),
    );
    const missingAttributes = current.missingAttributes.filter((key) => allowed.has(key));

    const unchanged =
      Object.keys(attributes).length === Object.keys(current.attributes).length &&
      missingAttributes.length === current.missingAttributes.length;

    if (!unchanged) {
      this.navigateTo({ ...current, attributes, missingAttributes });
    }
  }

  private navigateTo(query: CatalogQuery): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: toCatalogQueryParams(query),
      replaceUrl: true,
    });
  }

  applySearch(): void {
    this.navigateTo({ ...this.query(), search: this.search });
  }

  /**
   * 換品類時剪掉新 schema 不宣告的條件——不同品類的 schema 無法混用，
   * 留著會變成一個畫面上看不到、卻仍在生效的隱形篩選。
   */
  onCategoryChange(categoryId: string): void {
    const allowed = new Set(this.searchableFieldsFor(categoryId).map((f) => f.key));
    const current = this.query();

    this.navigateTo({
      ...current,
      categoryId,
      attributes: Object.fromEntries(
        Object.entries(current.attributes).filter(([key]) => allowed.has(key)),
      ),
      missingAttributes: current.missingAttributes.filter((key) => allowed.has(key)),
    });
  }

  clearFilters(): void {
    this.navigateTo(EMPTY_CATALOG_QUERY);
  }

  /**
   * 選定單一品類時，依該品類 schema 的 searchable 欄位——不同品類的 schema 無法混用。
   * 選「全部」時，僅在有品類宣告了 platform 欄位時，額外顯示白名單的平台篩選。
   */
  private searchableFieldsFor(categoryId: string): CategoryFieldDto[] {
    if (categoryId === '') {
      const hasPlatformField = this.categories().some((c) => c.fields.some((f) => f.key === 'platform'));
      return hasPlatformField ? [CatalogComponent.PLATFORM_FILTER_FIELD] : [];
    }

    const category = this.categories().find((c) => c.id === categoryId);
    return category?.fields.filter((f) => f.searchable) ?? [];
  }

  /** 平台相異值只在「平台篩選有出現」時才需要，且範圍要跟著目前的品類選擇走。 */
  private syncPlatformOptions(): void {
    if (this.searchableFields().some((f) => f.key === 'platform')) {
      this.catalog.platforms(this.categoryId() || undefined).subscribe((platforms) => this.platformOptions.set(platforms));
    } else {
      this.platformOptions.set([]);
    }
  }

  /** combobox 限制只能送出既有相異值之一；清單外的文字視為未完成輸入，直接還原。 */
  commitPlatformFilter(): void {
    const value = this.platformDraft.trim();
    if (value && !this.platformOptions().includes(value)) {
      this.platformDraft = this.filterValue('platform');
      return;
    }
    this.setAttributeFilter('platform', value);
  }

  loadMore(): void {
    this.page += 1;
    this.fetch(this.page, CatalogComponent.PAGE_SIZE, true);
    this.returnPoint.remember(this.query(), this.page);
  }

  /** 點進品項前記下錨點，回到列表時才捲得回這張卡片。 */
  rememberAnchor(itemId: string): void {
    this.returnPoint.rememberAnchor(itemId);
  }

  toggleTag(tag: string): void {
    const tags = this.selectedTags();

    this.navigateTo({
      ...this.query(),
      tags: tags.includes(tag) ? tags.filter((t) => t !== tag) : [...tags, tag],
    });
  }

  fieldsFor(categoryId: string): CategoryFieldDto[] {
    return this.categories().find((c) => c.id === categoryId)?.fields ?? [];
  }

  /**
   * 未設定的 key 在執行期是 undefined，但 Record<string, string> 的索引型別是 string
   * ——tsconfig 沒開 noUncheckedIndexedAccess，型別在說謊。
   *
   * 把 `?? ''` 直接寫在 template 會觸發 NG8102 並「建議」移除它；照做的話 [ngModel]
   * 會收到 undefined，輸入框變成不受控。所以在這裡收斂，讓 template 拿到的一定是 string。
   */
  filterValue(key: string): string {
    return this.attributeFilters()[key] ?? '';
  }

  setAttributeFilter(key: string, value: string): void {
    const attributes = { ...this.query().attributes };

    if (value) {
      attributes[key] = value;
    } else {
      delete attributes[key];
    }

    this.navigateTo({ ...this.query(), attributes });
  }

  isMissingFilter(key: string): boolean {
    return this.missingAttributes().includes(key);
  }

  /**
   * 「未設定」與「等於某個值」互斥：兩者同時成立必定零結果，不該讓使用者做得到。
   * 勾選時清掉該欄位的值，取消勾選時不還原舊值——比照 commitPlatformFilter() 的
   * 「拒絕就是拒絕」立場。
   */
  toggleMissingFilter(key: string): void {
    const enabling = !this.isMissingFilter(key);
    const current = this.query();
    const attributes = { ...current.attributes };

    if (enabling) {
      delete attributes[key];
    }

    this.navigateTo({
      ...current,
      attributes,
      missingAttributes: enabling
        ? [...current.missingAttributes, key]
        : current.missingAttributes.filter((k) => k !== key),
    });
  }

  private fetch(page: number, pageSize: number, append: boolean, anchorItemId: string | null = null): void {
    const query = this.query();
    const request = ++this.latestRequest;

    this.catalog
      .search({
        search: query.search || undefined,
        categoryId: query.categoryId || undefined,
        tags: query.tags,
        page,
        pageSize,
        attributes: query.attributes,
        missingAttributes: query.missingAttributes,
      })
      .subscribe((result) => {
        if (request !== this.latestRequest) {
          return;
        }

        this.items.update((current) => (append ? [...current, ...result.items] : result.items));
        this.total.set(result.total);

        if (anchorItemId) {
          this.scrollToAnchor(anchorItemId);
        }
      });
  }

  /**
   * 錨點已不在結果中就靜靜捲到頂端——它多半是使用者自己剛才的編輯造成的，
   * 而要斷定「是不是因為那次編輯」得比對前後兩份結果集，成本不成比例。
   *
   * 只對準卡片元素，不還原像素位移：網格是 auto-fill，寬度一變位置就沒有意義。
   */
  private scrollToAnchor(itemId: string): void {
    afterNextRender(
      () => {
        const element = (this.host.nativeElement as HTMLElement).querySelector(
          `[data-item-id="${CSS.escape(itemId)}"]`,
        );

        element?.scrollIntoView({ block: 'center' });
      },
      { injector: this.injector },
    );
  }
}
