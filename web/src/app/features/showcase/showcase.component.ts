import { Component, DestroyRef, NgZone, computed, inject, input, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { API_BASE } from '../../core/api-base';
import { CatalogService } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import { CategoryDto, CategoryFieldDto, ItemDto } from '../../core/models';
import { ItemCardComponent } from '../../shared/item-card/item-card.component';
import { ItemPreviewOverlayComponent } from '../../shared/item-preview-overlay/item-preview-overlay.component';
import { ShowcaseDisplayItem } from '../../shared/showcase-sections/showcase-display-item';
import { CollageSectionComponent } from '../../shared/showcase-sections/collage-section.component';
import { HeroSectionComponent } from '../../shared/showcase-sections/hero-section.component';
import { StatsSectionComponent } from '../../shared/showcase-sections/stats-section.component';
import { toShowcaseDisplayItem } from '../../shared/showcase-sections/showcase-display-item';
import { ShowcaseTab, ShowcaseTabsComponent } from '../../shared/showcase-tabs/showcase-tabs.component';
import {
  DEFAULT_SHOWCASE_VIEW,
  ShowcaseView,
  parseShowcaseView,
} from '../../shared/showcase-tabs/showcase-view';

/** 後端驗證器的單頁上限。抓不完就自動續抓下一頁。 */
const SHOWCASE_PAGE_SIZE = 200;

/**
 * 精選品項的安全上限。續抓的終止條件不能只看 `items.length < total`——
 * 後端若因故謊報 total（大於實際可取得的數量），那個條件會永遠成立而無限發請求。
 */
const MAX_SHOWCASE_ITEMS = 2000;

/** 滑鼠停留多久才浮出預覽。太短會讓滑過整排卡片時瘋狂閃爍，太長則感覺遲鈍。 */
const HOVER_PREVIEW_DELAY_MS = 200;

@Component({
  selector: 'app-showcase',
  imports: [
    ItemCardComponent,
    RouterLink,
    HeroSectionComponent,
    StatsSectionComponent,
    CollageSectionComponent,
    ShowcaseTabsComponent,
    ItemPreviewOverlayComponent,
  ],
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
      <app-showcase-tabs
        [tabs]="tabs()"
        [active]="activeView()"
        (activeChange)="selectView($event)"
      />

      @switch (activeView()) {
        @case ('collage') {
          <div role="tabpanel" id="showcase-panel-collage" aria-labelledby="showcase-tab-collage">
            <app-collage-section [items]="displayItems()" [slotCount]="8" />
          </div>
        }
        @case ('hero') {
          <div role="tabpanel" id="showcase-panel-hero" aria-labelledby="showcase-tab-hero">
            <app-hero-section [items]="heroItems()" />
          </div>
        }
        @case ('stats') {
          <div role="tabpanel" id="showcase-panel-stats" aria-labelledby="showcase-tab-stats">
            <app-stats-section [items]="statsItems()" />
          </div>
        }
        @case ('list') {
          <div
            class="showcase__wall"
            role="tabpanel"
            id="showcase-panel-list"
            aria-labelledby="showcase-tab-list"
          >
            @for (item of items(); track item.id) {
              <div
                class="showcase__card"
                data-showcase-card
                (mouseenter)="onCardEnter(item)"
                (mouseleave)="onCardLeave()"
              >
                <app-item-card [item]="item" [cardFields]="cardFieldsFor(item)" />
              </div>
            }
          </div>

          <app-item-preview-overlay [item]="hovered()" [fullImageUrl]="hoveredFullUrl()" />
        }
      }
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
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /** `?view=` query param，靠 app.config.ts 的 withComponentInputBinding() 直接綁進來。 */
  readonly view = input<string>();

  /**
   * 除了正規化無效值，還要擋掉「合法但空」的頁籤：書籤存了 `?view=hero`、之後所有焦點
   * 品項都被取消，就會停在一個停用又空白的頁籤上。這種情況一律退回拼貼牆。
   */
  readonly activeView = computed<ShowcaseView>(() => {
    const requested = parseShowcaseView(this.view());
    const tab = this.tabs().find((t) => t.id === requested);

    return tab && tab.count > 0 ? requested : DEFAULT_SHOWCASE_VIEW;
  });

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

  /**
   * 頁籤是篩選器不是版型選擇器（ADR-0009）：焦點／成就依展示模式篩，
   * 拼貼牆與列表都是全部精選品項。
   */
  readonly tabs = computed<ShowcaseTab[]>(() => {
    const all = this.displayItems().length;

    return [
      { id: 'collage', label: '拼貼牆', count: all },
      { id: 'hero', label: '焦點展品', count: this.heroItems().length },
      { id: 'stats', label: '遊戲成就', count: this.statsItems().length },
      { id: 'list', label: '列表', count: all },
    ];
  });

  readonly hovered = signal<ShowcaseDisplayItem | null>(null);
  readonly hoveredFullUrl = signal<string | null>(null);

  private readonly zone = inject(NgZone);
  private hoverTimer: ReturnType<typeof setTimeout> | undefined;
  private pendingId: string | null = null;

  constructor() {
    this.categoryApi.list().subscribe((categories) => this.categories.set(categories));

    this.loading.set(true);
    this.fetchPage(1);

    inject(DestroyRef).onDestroy(() => this.clearHoverTimer());
  }

  /**
   * 滑鼠停在列表卡片上 200ms 才浮出預覽。計時器比照 Hero/Stats 用 runOutsideAngular 建立、
   * DestroyRef 清除，避免卡住 ApplicationRef.whenStable()。
   *
   * 游標在延遲結束前移到別張卡片時，前一張的計時器必須被取消，否則會閃出錯誤的品項。
   */
  onCardEnter(item: ItemDto): void {
    this.clearHoverTimer();
    this.pendingId = item.id;
    this.hoveredFullUrl.set(null);

    this.preloadFullImage(item);

    const display = this.displayItems().find((d) => d.id === item.id) ?? null;

    this.hoverTimer = this.zone.runOutsideAngular(() =>
      setTimeout(
        () => this.zone.run(() => this.hovered.set(display)),
        HOVER_PREVIEW_DELAY_MS,
      ),
    );
  }

  onCardLeave(): void {
    this.clearHoverTimer();
    this.pendingId = null;
    this.hovered.set(null);
    this.hoveredFullUrl.set(null);
  }

  /**
   * 延遲期間就開始載原圖（full，1600px），載完才換掉列表已快取的 card 圖（480px）。
   * 載入是非同步的，回來時游標可能早就移到別張卡片了，所以要比對 pendingId。
   */
  private preloadFullImage(item: ItemDto): void {
    const primary = item.images.find((i) => i.isPrimary) ?? item.images[0];

    if (!primary) {
      return; // 只有 CDN 網址的同步品項沒有 full 版本，直接用 card 圖那層。
    }

    const url = `${API_BASE}/media/${primary.path}`;
    const image = new Image();

    image.onload = () => {
      if (this.pendingId === item.id) {
        this.zone.run(() => this.hoveredFullUrl.set(url));
      }
    };

    image.src = url;
  }

  private clearHoverTimer(): void {
    if (this.hoverTimer) {
      clearTimeout(this.hoverTimer);
      this.hoverTimer = undefined;
    }
  }

  /** 頁籤狀態放在網址上，可分享、可書籤、重新整理保留。replaceUrl 避免切頁籤在瀏覽記錄裡堆成一長串。 */
  selectView(view: ShowcaseView): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { view },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
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
