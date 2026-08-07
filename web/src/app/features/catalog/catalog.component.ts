import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CatalogService } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
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
        <label>搜尋<input type="search" [(ngModel)]="search" (ngModelChange)="reload()" /></label>

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
            <app-item-card [item]="item" [cardFields]="fieldsFor(item.categoryId)" />
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

  readonly items = signal<ItemDto[]>([]);
  readonly total = signal(0);
  readonly categories = signal<CategoryDto[]>([]);
  readonly allTags = signal<string[]>([]);
  readonly selectedTags = signal<string[]>([]);
  readonly attributeFilters = signal<Record<string, string>>({});

  /** 要求「未設定」的欄位 key。與 attributeFilters 是兩件事：一個選值，一個選「沒有值」。 */
  readonly missingAttributes = signal<string[]>([]);
  readonly categoryId = signal('');
  readonly platformOptions = signal<string[]>([]);

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

  /**
   * 選定單一品類時，依該品類 schema 的 searchable 欄位——不同品類的 schema 無法混用。
   * 選「全部」時，僅在有品類宣告了 platform 欄位時，額外顯示白名單的平台篩選。
   */
  readonly searchableFields = computed<CategoryFieldDto[]>(() => {
    const categoryId = this.categoryId();

    if (categoryId === '') {
      const hasPlatformField = this.categories().some((c) => c.fields.some((f) => f.key === 'platform'));
      return hasPlatformField ? [CatalogComponent.PLATFORM_FILTER_FIELD] : [];
    }

    const category = this.categories().find((c) => c.id === categoryId);
    return category?.fields.filter((f) => f.searchable) ?? [];
  });

  search = '';
  platformDraft = '';

  private page = 1;

  constructor() {
    this.categoryApi.list().subscribe((c) => {
      this.categories.set(c);
      this.syncPlatformOptions();
    });
    this.catalog.tags().subscribe((t) => this.allTags.set(t));
    this.load();
  }

  onCategoryChange(value: string): void {
    this.categoryId.set(value);
    this.reload();
  }

  reload(): void {
    const allowed = new Set(this.searchableFields().map((f) => f.key));
    this.attributeFilters.update((current) =>
      Object.fromEntries(Object.entries(current).filter(([key]) => allowed.has(key))),
    );
    this.missingAttributes.update((keys) => keys.filter((key) => allowed.has(key)));
    this.platformDraft = this.attributeFilters()['platform'] ?? '';
    this.syncPlatformOptions();

    this.page = 1;
    this.items.set([]);
    this.load();
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
    this.load();
  }

  toggleTag(tag: string): void {
    this.selectedTags.update((tags) =>
      tags.includes(tag) ? tags.filter((t) => t !== tag) : [...tags, tag],
    );
    this.reload();
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
    this.attributeFilters.update((current) => ({ ...current, [key]: value }));
    this.reload();
  }

  isMissingFilter(key: string): boolean {
    return this.missingAttributes().includes(key);
  }

  /**
   * 「未設定」與「等於某個值」互斥：兩者同時成立必定零結果，不該讓使用者做得到。
   * 勾選時清掉該欄位的值（reload() 會連帶把 platformDraft 收斂成空字串），
   * 取消勾選時不還原舊值——比照 commitPlatformFilter() 的「拒絕就是拒絕」立場。
   */
  toggleMissingFilter(key: string): void {
    const enabling = !this.isMissingFilter(key);

    this.missingAttributes.update((keys) =>
      enabling ? [...keys, key] : keys.filter((k) => k !== key),
    );

    if (enabling) {
      this.attributeFilters.update((current) => ({ ...current, [key]: '' }));
    }

    this.reload();
  }

  private load(): void {
    this.catalog
      .search({
        search: this.search || undefined,
        categoryId: this.categoryId() || undefined,
        tags: this.selectedTags(),
        page: this.page,
        pageSize: 24,
        attributes: this.attributeFilters(),
        missingAttributes: this.missingAttributes(),
      })
      .subscribe((result) => {
        this.items.update((current) => [...current, ...result.items]);
        this.total.set(result.total);
      });
  }
}
