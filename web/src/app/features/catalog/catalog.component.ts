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
          <select [(ngModel)]="categoryId" (ngModelChange)="reload()">
            <option value="">全部</option>
            @for (category of categories(); track category.id) {
              <option [value]="category.id">{{ category.name }}</option>
            }
          </select>
        </label>

        @for (field of searchableFields(); track field.key) {
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

  /** 只有選定品類時才有屬性篩選——不同品類的 schema 無法混用。 */
  readonly searchableFields = computed<CategoryFieldDto[]>(() => {
    const category = this.categories().find((c) => c.id === this.categoryId);
    return category?.fields.filter((f) => f.searchable) ?? [];
  });

  search = '';
  categoryId = '';

  private page = 1;

  constructor() {
    this.categoryApi.list().subscribe((c) => this.categories.set(c));
    this.catalog.tags().subscribe((t) => this.allTags.set(t));
    this.load();
  }

  reload(): void {
    const allowed = new Set(this.searchableFields().map((f) => f.key));
    this.attributeFilters.update((current) =>
      Object.fromEntries(Object.entries(current).filter(([key]) => allowed.has(key))),
    );

    this.page = 1;
    this.items.set([]);
    this.load();
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

  private load(): void {
    this.catalog
      .search({
        search: this.search || undefined,
        categoryId: this.categoryId || undefined,
        tags: this.selectedTags(),
        page: this.page,
        pageSize: 24,
        attributes: this.attributeFilters(),
      })
      .subscribe((result) => {
        this.items.update((current) => [...current, ...result.items]);
        this.total.set(result.total);
      });
  }
}
