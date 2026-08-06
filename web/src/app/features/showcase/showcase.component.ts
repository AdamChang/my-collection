import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CatalogService } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import { CategoryDto, CategoryFieldDto, ItemDto } from '../../core/models';
import { ItemCardComponent } from '../../shared/item-card/item-card.component';
import { CollageSectionComponent } from '../../shared/showcase-sections/collage-section.component';
import { HeroSectionComponent } from '../../shared/showcase-sections/hero-section.component';
import { StatsSectionComponent } from '../../shared/showcase-sections/stats-section.component';
import { toShowcaseDisplayItem } from '../../shared/showcase-sections/showcase-display-item';

/** 後端驗證器的單頁上限。抓不完就自動續抓下一頁。 */
const SHOWCASE_PAGE_SIZE = 200;

/**
 * 精選品項的安全上限。續抓的終止條件不能只看 `items.length < total`——
 * 後端若因故謊報 total（大於實際可取得的數量），那個條件會永遠成立而無限發請求。
 */
const MAX_SHOWCASE_ITEMS = 2000;

@Component({
  selector: 'app-showcase',
  imports: [ItemCardComponent, RouterLink, HeroSectionComponent, StatsSectionComponent, CollageSectionComponent],
  template: `
    <header class="showcase__header" data-showcase-terminal>
      <div>
        <div class="mc-eyebrow">CURATED ARCHIVE / ONLINE</div>
        <h1>精選收藏</h1>
        <p class="mc-muted">{{ total() }} 件已編入精選展示</p>
      </div>
      <a class="showcase__all" routerLink="/catalog">OPEN CATALOG →</a>
    </header>

    @if (loading()) {
      <p>載入中…</p>
    } @else if (items().length === 0) {
      <p class="showcase__empty">
        還沒有精選品項。到<a routerLink="/catalog">庫存</a>把喜歡的東西設為精選吧。
      </p>
    } @else {
      <app-hero-section [items]="heroItems()" />
      <app-stats-section [items]="statsItems()" />
      <app-collage-section [items]="displayItems()" [slotCount]="4" />

      <div class="showcase__wall">
        @for (item of items(); track item.id) {
          <app-item-card [item]="item" [cardFields]="cardFieldsFor(item)" />
        }
      </div>
    }
  `,
  styles: `
    .showcase__header { display: flex; justify-content: space-between; align-items: end; gap: 1rem; margin-bottom: 1.5rem; }
    .showcase__header h1, .showcase__header p { margin: 0.35rem 0 0; }
    .showcase__all { font: 700 0.8rem/1.4 Consolas, monospace; letter-spacing: 0.08em; white-space: nowrap; }
    .showcase__wall { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 1rem; }
    .showcase__empty { color: var(--mc-text-muted); }
    @media (max-width: 520px) {
      .showcase__header { align-items: start; flex-direction: column; }
      .showcase__wall { grid-template-columns: 1fr; }
    }
  `,
})
export class ShowcaseComponent {
  private readonly catalog = inject(CatalogService);
  private readonly categoryApi = inject(CategoryService);

  readonly items = signal<ItemDto[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly categories = signal<CategoryDto[]>([]);

  /** 全部精選品項轉成三個展示分區共用的形狀；Collage 直接吃這份，不受展示模式篩選（ADR-0007）。 */
  readonly displayItems = computed(() =>
    this.items().map((item) => toShowcaseDisplayItem(item, this.categories())),
  );

  readonly heroItems = computed(() => this.displayItems().filter((i) => i.effectiveDisplayMode === 'Hero'));
  readonly statsItems = computed(() => this.displayItems().filter((i) => i.effectiveDisplayMode === 'Stats'));

  constructor() {
    this.categoryApi.list().subscribe((categories) => this.categories.set(categories));

    this.loading.set(true);
    this.fetchPage(1);
  }

  cardFieldsFor(item: ItemDto): CategoryFieldDto[] {
    return this.categories().find((c) => c.id === item.categoryId)?.fields ?? [];
  }

  /**
   * 一路抓到全部精選品項都到齊才收掉 loading。
   * 頁籤的數字與啟用狀態必須是穩定的事實（ADR-0009）——分批進來會讓焦點頁籤
   * 先顯示 0 被停用、續抓完又啟用，頁籤列閃動，使用者還可能點到停用的頁籤。
   */
  private fetchPage(page: number): void {
    this.catalog.showcase(page, SHOWCASE_PAGE_SIZE).subscribe({
      next: (result) => {
        this.items.update((current) => [...current, ...result.items]);
        this.total.set(result.total);

        const loaded = this.items().length;
        const hasMore =
          result.items.length > 0 && loaded < result.total && loaded < MAX_SHOWCASE_ITEMS;

        if (hasMore) {
          this.fetchPage(page + 1);
          return;
        }

        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
