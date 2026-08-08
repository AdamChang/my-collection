import { DatePipe } from '@angular/common';
import { Component, NgZone, computed, effect, inject, input, signal } from '@angular/core';
import { ShowcaseDisplayItem } from './showcase-display-item';
import { AuthenticatedMediaDirective } from '../authenticated-media.directive';

/** 焦點展品模式：單品項輪播，大圖 Ken Burns 微縮放 + 側欄資訊。適合公仔模型／珍藏卡。 */
@Component({
  selector: 'app-hero-section',
  imports: [DatePipe, AuthenticatedMediaDirective],
  template: `
    @if (current(); as item) {
      <section class="hero" data-hero-section>
        <div class="hero__image">
          @if (item.imageUrl; as url) {
            <img [appAuthenticatedMedia]="url" [alt]="item.name" />
          } @else {
            <div class="hero__placeholder" aria-hidden="true">{{ item.name.charAt(0) }}</div>
          }
        </div>

        <aside class="hero__panel">
          <div class="mc-eyebrow">FEATURED / HERO</div>
          <h2>{{ item.name }}</h2>

          @if (item.cardAttributes.length) {
            <dl class="hero__fields" data-hero-fields>
              @for (entry of item.cardAttributes; track entry.key) {
                <dt>{{ entry.label }}</dt>
                <dd>{{ entry.value }}</dd>
              }
            </dl>
          }

          <dl class="hero__meta">
            @if (item.acquiredAt) {
              <dt>入手日期</dt>
              <dd>{{ item.acquiredAt | date: 'yyyy-MM-dd' }}</dd>
            }
            @if (item.price) {
              <dt>入手價格</dt>
              <dd>{{ item.price.amount }} {{ item.price.currency }}</dd>
            }
            @if (item.storageLocation) {
              <dt>存放位置</dt>
              <dd data-hero-storage-location>{{ item.storageLocation }}</dd>
            }
            @if (item.rating) {
              <dt>評分</dt>
              <dd>{{ item.rating }} / 10</dd>
            }
          </dl>

          @if (item.description) {
            <p class="hero__notes">{{ item.description }}</p>
          }

          @if (items().length > 1) {
            <div class="hero__dots" data-hero-dots>
              @for (dot of items(); track dot.id; let i = $index) {
                <button
                  type="button"
                  [class.hero__dot--active]="i === index()"
                  [attr.aria-label]="'第 ' + (i + 1) + ' 件焦點展品'"
                  (click)="select(i)"
                ></button>
              }
            </div>
          }
        </aside>
      </section>
    }
  `,
  styles: `
    .hero { display: grid; grid-template-columns: minmax(0, 1.6fr) minmax(16rem, 1fr); gap: 1.25rem;
            border: 1px solid var(--mc-border); background: var(--mc-surface); box-shadow: var(--mc-shadow);
            clip-path: polygon(0 0, calc(100% - var(--mc-cut)) 0, 100% var(--mc-cut), 100% 100%, 0 100%);
            margin-bottom: 1.5rem; overflow: hidden; }
    .hero__image { position: relative; aspect-ratio: 16 / 10; overflow: hidden; background: var(--mc-surface-raised); }
    .hero__image img { width: 100%; height: 100%; object-fit: cover; animation: hero-kenburns 9s ease-in-out infinite alternate; }
    .hero__placeholder { display: grid; place-items: center; width: 100%; height: 100%; font-size: 3rem; color: var(--mc-cyan); }
    @keyframes hero-kenburns {
      from { transform: scale(1) translate(0, 0); }
      to { transform: scale(1.09) translate(-1%, -1%); }
    }
    .hero__panel { display: grid; align-content: start; gap: 0.75rem; padding: 1.25rem 1.25rem 1.5rem 0; }
    .hero__panel h2 { margin: 0.15rem 0 0; }
    .hero__fields, .hero__meta { display: grid; grid-template-columns: auto 1fr; gap: 0.15rem 0.6rem; margin: 0; font-size: 0.85rem; }
    .hero__fields dt, .hero__meta dt { font-weight: 600; color: var(--mc-text-muted); }
    .hero__fields dd, .hero__meta dd { margin: 0; }
    .hero__notes { margin: 0; color: var(--mc-text-muted); font-size: 0.85rem; white-space: pre-wrap; }
    .hero__dots { display: flex; gap: 0.4rem; margin-top: 0.25rem; }
    .hero__dots button { width: 0.55rem; height: 0.55rem; padding: 0; border-radius: 50%; border: 1px solid var(--mc-border-strong); background: transparent; cursor: pointer; }
    .hero__dots button.hero__dot--active { background: var(--mc-cyan); border-color: var(--mc-cyan); }
    @media (max-width: 52rem) {
      .hero { grid-template-columns: 1fr; }
      .hero__panel { padding: 0 1.25rem 1.25rem; }
    }
  `,
})
export class HeroSectionComponent {
  readonly items = input<ShowcaseDisplayItem[]>([]);
  readonly rotationMs = input(7000);

  readonly index = signal(0);

  private readonly zone = inject(NgZone);

  readonly current = computed(() => {
    const list = this.items();
    return list.length ? list[this.index() % list.length] : null;
  });

  /**
   * 只有多於一件展品時才需要輪播；沒有計時器就不會有 setInterval 一直卡著 NgZone，
   * 也不會讓只有 0～1 件精選品的頁面在 e2e 測試裡卡在 whenStable()。
   * 用 effect 而不是建構子直接排時器，是為了讓計時器的建立時機跟著第一次變更偵測走，
   * 這樣 fakeAsync 測試裡的 tick() 才控制得到它。
   */
  constructor() {
    effect((onCleanup) => {
      if (this.items().length <= 1) {
        return;
      }

      const rotationMs = this.rotationMs();
      const timer = this.zone.runOutsideAngular(() =>
        setInterval(() => this.zone.run(() => this.index.update((i) => i + 1)), rotationMs),
      );
      onCleanup(() => clearInterval(timer));
    });
  }

  select(i: number): void {
    this.index.set(i);
  }
}
