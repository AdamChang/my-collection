import { Component, computed, input, output, signal } from '@angular/core';

const MIN = 1;
const MAX = 10;

/** 5 顆星、每半顆 1 分，對外仍是後端既有的 1–10 整數，null 代表未評分。 */
@Component({
  selector: 'app-rating-bar',
  template: `
    <div class="rating">
      <div
        class="rating__stars"
        role="slider"
        tabindex="0"
        aria-label="評分"
        aria-valuemin="1"
        aria-valuemax="10"
        [attr.aria-valuenow]="rating()"
        [attr.aria-valuetext]="valueText()"
        (keydown)="onKeydown($event)"
        (mouseleave)="hovered.set(null)"
      >
        @for (star of stars; track star) {
          <span class="star" aria-hidden="true">
            <span class="star__base">{{ glyph }}</span>
            <span class="star__fill" data-rating-fill [style.width.%]="fillPercent(star)">
              {{ glyph }}
            </span>
            <span
              class="star__half star__half--left"
              data-rating-half
              (click)="pick(star * 2 - 1)"
              (mouseenter)="hovered.set(star * 2 - 1)"
            ></span>
            <span
              class="star__half star__half--right"
              data-rating-half
              (click)="pick(star * 2)"
              (mouseenter)="hovered.set(star * 2)"
            ></span>
          </span>
        }
      </div>

      <span class="rating__value">{{ label() }}</span>

      @if (rating() !== null) {
        <button type="button" class="rating__clear" data-rating-clear (click)="pick(null)">
          清除
        </button>
      }
    </div>
  `,
  styles: `
    .rating { display: flex; align-items: center; gap: 0.75rem; }
    .rating__stars { display: inline-flex; font-size: 2.5rem; line-height: 1; cursor: pointer;
      font-variant-emoji: text; }
    .rating__stars:focus-visible { outline: 2px solid var(--mc-cyan); outline-offset: 4px; }
    .star { position: relative; display: inline-block; }
    .star__base { color: var(--mc-border-strong); }
    .star__fill { position: absolute; top: 0; left: 0; height: 100%; overflow: hidden;
      color: var(--mc-star); }
    .star__half { position: absolute; top: 0; bottom: 0; width: 50%; }
    .star__half--left { left: 0; }
    .star__half--right { right: 0; }
    .rating__value { color: var(--mc-text-muted); font-variant-numeric: tabular-nums; }
    .rating__clear { min-height: 44px; border: 1px solid var(--mc-border); background: transparent;
      color: var(--mc-text-muted); padding: 0 0.75rem; }
  `,
})
export class RatingBarComponent {
  readonly rating = input<number | null>(null);
  readonly ratingChange = output<number | null>();

  /** U+FE0E 逼字型走文字外觀，否則部分平台會把 ★ 渲染成彩色 emoji，填色與裁切全失效。 */
  readonly glyph = '★︎';
  readonly stars = [1, 2, 3, 4, 5];

  protected readonly hovered = signal<number | null>(null);

  private readonly shown = computed(() => this.hovered() ?? this.rating());

  protected readonly label = computed(() => {
    const value = this.shown();
    return value === null ? '未評分' : `${value} / ${MAX}`;
  });

  /** 唸出來的是實際值而非 hover 預覽——螢幕閱讀器使用者不會有 hover。 */
  protected readonly valueText = computed(() => {
    const value = this.rating();
    return value === null ? '未評分' : `${value} 分，滿分 ${MAX} 分`;
  });

  protected fillPercent(star: number): number {
    const filled = (this.shown() ?? 0) - (star - 1) * 2;
    return Math.min(Math.max(filled, 0), 2) * 50;
  }

  protected pick(value: number | null): void {
    this.hovered.set(null);
    this.ratingChange.emit(value);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const next = this.nextValue(event.key);

    if (next === undefined) {
      return;
    }

    event.preventDefault();

    if (next !== this.rating()) {
      this.ratingChange.emit(next);
    }
  }

  /** 回傳 undefined 代表這顆鍵不歸我們管，要讓它照常冒泡。 */
  private nextValue(key: string): number | null | undefined {
    const current = this.rating();

    switch (key) {
      case 'ArrowRight':
      case 'ArrowUp':
        return current === null ? MIN : Math.min(current + 1, MAX);
      case 'ArrowLeft':
      case 'ArrowDown':
        return current === null ? MIN : Math.max(current - 1, MIN);
      case 'Home':
        return MIN;
      case 'End':
        return MAX;
      case 'Delete':
      case 'Backspace':
        return null;
      default:
        return undefined;
    }
  }
}
