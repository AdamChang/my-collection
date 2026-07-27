import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CatalogService } from '../../core/api/catalog.service';
import { ItemDto } from '../../core/models';
import { ItemCardComponent } from '../../shared/item-card/item-card.component';

@Component({
  selector: 'app-showcase',
  imports: [ItemCardComponent, RouterLink],
  template: `
    <header class="showcase__header">
      <h1>精選收藏</h1>
      <a routerLink="/catalog">看全部庫存 →</a>
    </header>

    @if (loading()) {
      <p>載入中…</p>
    } @else if (items().length === 0) {
      <p class="showcase__empty">
        還沒有精選品項。到<a routerLink="/catalog">庫存</a>把喜歡的東西設為精選吧。
      </p>
    } @else {
      <div class="showcase__wall">
        @for (item of items(); track item.id) {
          <app-item-card [item]="item" />
        }
      </div>

      @if (items().length < total()) {
        <button type="button" (click)="loadMore()" [disabled]="loading()">載入更多</button>
      }
    }
  `,
  styles: `
    .showcase__header { display: flex; justify-content: space-between; align-items: baseline; }
    .showcase__wall { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 1rem; }
    .showcase__empty { color: #7f8c8d; }
  `,
})
export class ShowcaseComponent {
  private readonly catalog = inject(CatalogService);

  readonly items = signal<ItemDto[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);

  private page = 1;

  constructor() {
    this.load();
  }

  loadMore(): void {
    this.page += 1;
    this.load();
  }

  private load(): void {
    this.loading.set(true);

    this.catalog.showcase(this.page, 24).subscribe({
      next: (result) => {
        this.items.update((current) => [...current, ...result.items]);
        this.total.set(result.total);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
