import { Component, DestroyRef, NgZone, effect, inject, input, signal } from '@angular/core';
import { ShowcaseDisplayItem } from './showcase-display-item';

interface CollageSlot {
  item: ShowcaseDisplayItem;
  tiltDeg: number;
}

/**
 * 照片牆／拼貼牆：固定槽位，拍立得風格＋隨機傾斜，定時把其中一格換成下一張精選照片。
 * 依 ADR-0007，資料來源是全部精選品項，不受展示模式篩選。
 */
@Component({
  selector: 'app-collage-section',
  template: `
    @if (slots().length) {
      <section class="collage" data-collage-section>
        <div class="mc-eyebrow">SNAPSHOTS / COLLAGE</div>
        <div class="collage__wall">
          @for (slot of slots(); track slot.item.id) {
            <figure class="collage__card" [style.transform]="'rotate(' + slot.tiltDeg + 'deg)'" data-collage-card>
              @if (slot.item.imageUrl; as url) {
                <img [src]="url" [alt]="slot.item.name" loading="lazy" />
              } @else {
                <div class="collage__placeholder" aria-hidden="true">{{ slot.item.name.charAt(0) }}</div>
              }
              <figcaption>{{ slot.item.name }}</figcaption>
            </figure>
          }
        </div>
      </section>
    }
  `,
  styles: `
    .collage { margin-bottom: 1.5rem; }
    /* 拼貼牆是預設第一眼（ADR-0009），要有足夠的視覺重量：8 格 × 18rem 在 1920px 下排成 4+4 置中。
       拍立得風格本來就靠大尺寸與陰影取勝，11rem 那種尺寸更像縮圖。 */
    .collage__wall { display: flex; flex-wrap: wrap; justify-content: center; gap: 1.5rem; padding: 1rem 0.5rem; }
    .collage__card { background: #fff; padding: 0.6rem 0.6rem 1.6rem; width: 18rem; box-shadow: 0 8px 18px rgb(5 7 13 / 45%);
                      transition: transform 480ms ease; }
    .collage__card img, .collage__placeholder { width: 100%; aspect-ratio: 1 / 1; object-fit: cover; display: block; }
    .collage__placeholder { display: grid; place-items: center; background: var(--mc-surface-raised); color: var(--mc-cyan); font-size: 2rem; }
    .collage__card figcaption { margin-top: 0.5rem; font: 700 0.75rem/1.3 Consolas, monospace; color: #1a1a1a; text-align: center;
                                 overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    @media (max-width: 520px) {
      .collage__wall { justify-content: center; }
    }
  `,
})
export class CollageSectionComponent {
  readonly items = input<ShowcaseDisplayItem[]>([]);
  readonly slotCount = input(4);
  readonly rotationMs = input(4000);

  readonly slots = signal<CollageSlot[]>([]);

  private readonly zone = inject(NgZone);
  private cursor = 0;
  private timer: ReturnType<typeof setInterval> | undefined;

  constructor() {
    effect(() => {
      const pool = this.items();
      const count = Math.min(this.slotCount(), pool.length);

      this.cursor = count;
      this.slots.set(pool.slice(0, count).map((item) => ({ item, tiltDeg: this.randomTilt() })));

      this.restartTimer(pool, count);
    });

    inject(DestroyRef).onDestroy(() => this.clearTimer());
  }

  private restartTimer(pool: ShowcaseDisplayItem[], visibleCount: number): void {
    this.clearTimer();

    if (pool.length <= visibleCount) {
      return;
    }

    const rotationMs = this.rotationMs();
    this.timer = this.zone.runOutsideAngular(() =>
      setInterval(() => this.zone.run(() => this.replaceRandomSlot(pool)), rotationMs),
    );
  }

  private replaceRandomSlot(pool: ShowcaseDisplayItem[]): void {
    if (pool.length === 0) {
      return;
    }

    const nextItem = pool[this.cursor % pool.length];
    this.cursor += 1;

    this.slots.update((current) => {
      if (current.length === 0) {
        return current;
      }

      const slotIndex = Math.floor(Math.random() * current.length);
      const next = [...current];
      next[slotIndex] = { item: nextItem, tiltDeg: this.randomTilt() };
      return next;
    });
  }

  private randomTilt(): number {
    return Math.round((Math.random() * 10 - 5) * 10) / 10;
  }

  private clearTimer(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = undefined;
    }
  }
}
