import { Component, NgZone, computed, effect, inject, input, signal } from '@angular/core';
import { ShowcaseDisplayItem } from './showcase-display-item';

/** 數據統計與成就看板：單品項輪播，背景視覺圖 + 遊玩時數／獎盃完成度。適合數位遊戲。 */
@Component({
  selector: 'app-stats-section',
  template: `
    @if (current(); as item) {
      <section class="stats" data-stats-section [style.background-image]="item.imageUrl ? 'url(' + item.imageUrl + ')' : null">
        <div class="stats__scrim"></div>
        <div class="stats__panel">
          <div class="mc-eyebrow">ACCOMPLISHMENTS / STATS</div>
          <h2>{{ item.name }}</h2>

          @if (playtimeHours(item); as hours) {
            <p class="stats__metric" data-stats-playtime>近期遊玩時數：{{ hours }} 小時</p>
          }

          @if (progress(item); as percent) {
            <div class="stats__progress" role="progressbar" [attr.aria-valuenow]="percent" aria-valuemin="0" aria-valuemax="100" data-stats-progress>
              <div class="stats__progress-bar" [style.width.%]="percent"></div>
            </div>
            <p class="stats__metric">獎盃完成度：{{ percent }}%</p>
          }

          @if (items().length > 1) {
            <div class="stats__dots" data-stats-dots>
              @for (dot of items(); track dot.id; let i = $index) {
                <button
                  type="button"
                  [class.stats__dot--active]="i === index()"
                  [attr.aria-label]="'第 ' + (i + 1) + ' 款遊戲'"
                  (click)="select(i)"
                ></button>
              }
            </div>
          }
        </div>
      </section>
    }
  `,
  styles: `
    .stats { position: relative; margin-bottom: 1.5rem; min-height: 16rem; display: flex; align-items: end;
             border: 1px solid var(--mc-border); box-shadow: var(--mc-shadow); overflow: hidden;
             background-size: cover; background-position: center; background-color: var(--mc-surface-raised);
             clip-path: polygon(0 0, calc(100% - var(--mc-cut)) 0, 100% var(--mc-cut), 100% 100%, 0 100%); }
    .stats__scrim { position: absolute; inset: 0; background: linear-gradient(0deg, rgb(5 7 13 / 88%) 15%, rgb(5 7 13 / 20%) 75%); }
    .stats__panel { position: relative; padding: 1.25rem; display: grid; gap: 0.5rem; color: #fff; width: 100%; }
    .stats__panel h2 { margin: 0.1rem 0; }
    .stats__metric { margin: 0; font-size: 0.9rem; }
    .stats__progress { height: 0.5rem; background: rgb(255 255 255 / 18%); border: 1px solid var(--mc-border-strong); }
    .stats__progress-bar { height: 100%; background: var(--mc-cyan); }
    .stats__dots { display: flex; gap: 0.4rem; margin-top: 0.25rem; }
    .stats__dots button { width: 0.55rem; height: 0.55rem; padding: 0; border-radius: 50%; border: 1px solid rgb(255 255 255 / 45%); background: transparent; cursor: pointer; }
    .stats__dots button.stats__dot--active { background: var(--mc-cyan); border-color: var(--mc-cyan); }
  `,
})
export class StatsSectionComponent {
  readonly items = input<ShowcaseDisplayItem[]>([]);
  readonly rotationMs = input(7000);

  readonly index = signal(0);

  private readonly zone = inject(NgZone);

  readonly current = computed(() => {
    const list = this.items();
    return list.length ? list[this.index() % list.length] : null;
  });

  /** 見 HeroSectionComponent 同名建構子上的說明：effect + runOutsideAngular 兩個理由同時適用。 */
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

  playtimeHours(item: ShowcaseDisplayItem): number | null {
    const minutes = item.attributes['playtimeForever'];
    return typeof minutes === 'number' && minutes > 0 ? Math.round((minutes / 60) * 10) / 10 : null;
  }

  progress(item: ShowcaseDisplayItem): number | null {
    const value = item.attributes['psnProgress'];
    return typeof value === 'number' ? value : null;
  }
}
