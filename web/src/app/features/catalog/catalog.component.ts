import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CatalogService } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import { CategoryDto, ItemDto } from '../../core/models';
import { ItemCardComponent } from '../../shared/item-card/item-card.component';

@Component({
  selector: 'app-catalog',
  imports: [FormsModule, ItemCardComponent, RouterLink],
  template: `
    <div class="catalog">
      <aside class="catalog__filters">
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
        <header>
          <span>{{ total() }} 件</span>
          <a routerLink="/items/new">新增品項</a>
        </header>

        <div class="catalog__grid">
          @for (item of items(); track item.id) {
            <app-item-card [item]="item" />
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
    .catalog__grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 1rem; }
    .catalog__tag { display: block; font-size: 0.85rem; }
    @media (max-width: 720px) { .catalog { grid-template-columns: 1fr; } }
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

  search = '';
  categoryId = '';

  private page = 1;

  constructor() {
    this.categoryApi.list().subscribe((c) => this.categories.set(c));
    this.catalog.tags().subscribe((t) => this.allTags.set(t));
    this.load();
  }

  reload(): void {
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

  private load(): void {
    this.catalog
      .search({
        search: this.search || undefined,
        categoryId: this.categoryId || undefined,
        tags: this.selectedTags(),
        page: this.page,
        pageSize: 24,
      })
      .subscribe((result) => {
        this.items.update((current) => [...current, ...result.items]);
        this.total.set(result.total);
      });
  }
}
