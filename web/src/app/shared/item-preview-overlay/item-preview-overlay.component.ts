import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { ShowcaseDisplayItem } from '../showcase-sections/showcase-display-item';

interface PreviewField {
  label: string;
  value: string;
}

/**
 * 滑鼠停在精選列表卡片上時，畫面正中央浮出的大圖預覽。
 *
 * 這個元件只負責「拿到東西就畫出來」——**進場延遲不在這裡**，那屬於「何時該顯示」，
 * 是呼叫端的職責。這個切分讓延遲邏輯與渲染邏輯可以各自測試。
 *
 * 整體 `pointer-events: none`：滑鼠永遠不會「進入」浮層，所以不會出現預覽卡住、
 * 或浮層擋住底下卡片點擊的問題。
 */
@Component({
  selector: 'app-item-preview-overlay',
  template: `
    @if (item(); as preview) {
      <div class="preview" data-preview-overlay aria-hidden="true">
        <div class="preview__scrim"></div>

        <figure class="preview__frame">
          @if (imageUrl(); as url) {
            <div class="preview__backdrop" [style.background-image]="'url(' + url + ')'"></div>
            <img class="preview__image" [src]="url" [alt]="preview.name" data-preview-image />
          } @else {
            <div class="preview__placeholder" data-preview-placeholder>
              {{ preview.name.charAt(0) }}
            </div>
          }

          <figcaption class="preview__panel">
            <h2 class="preview__name">{{ preview.name }}</h2>

            @if (fields().length) {
              <dl class="preview__fields" data-preview-fields>
                @for (field of fields(); track field.label) {
                  <div class="preview__field">
                    <dt>{{ field.label }}</dt>
                    <dd>{{ field.value }}</dd>
                  </div>
                }
              </dl>
            }
          </figcaption>
        </figure>
      </div>
    }
  `,
  styles: `
    /* hover 是桌機專屬的視覺增強。觸控裝置點進詳細頁本來就看得到更完整的內容，
       而浮層 pointer-events: none 意味著它本來就不能互動。 */
    .preview { display: none; }

    @media (hover: hover) {
      .preview { display: block; position: fixed; inset: 0; z-index: 40; pointer-events: none;
                 animation: preview-fade 150ms ease-out; }
      .preview__scrim { position: absolute; inset: 0; background: rgb(5 7 13 / 72%); }
      .preview__frame { position: absolute; top: 50%; left: 50%; translate: -50% -50%; margin: 0;
                        width: min(58rem, 92vw); aspect-ratio: 16 / 10; max-height: 84vh; overflow: hidden;
                        border: 1px solid var(--mc-border-strong); box-shadow: var(--mc-shadow);
                        background: var(--mc-surface-raised);
                        clip-path: polygon(0 0, calc(100% - var(--mc-cut)) 0, 100% var(--mc-cut), 100% 100%, 0 100%); }
      /* 完整圖片不裁切（contain），直式的公仔卡牌兩側的空白用同一張圖模糊放大填掉，
         框才不必隨圖片比例伸縮——收藏裡直式品項與寬扁的 Steam header 比例差極遠。 */
      .preview__backdrop { position: absolute; inset: 0; background-size: cover; background-position: center;
                           filter: blur(2.5rem) saturate(1.3); transform: scale(1.2); opacity: 0.55; }
      .preview__image { position: relative; width: 100%; height: 100%; object-fit: contain; display: block; }
      .preview__placeholder { display: grid; place-items: center; width: 100%; height: 100%;
                              font-size: 5rem; color: var(--mc-cyan); }
      .preview__panel { position: absolute; inset: auto 0 0; padding: 3rem 1.25rem 1rem; color: #fff;
                        background: linear-gradient(0deg, rgb(5 7 13 / 92%) 45%, transparent); }
      .preview__name { margin: 0 0 0.5rem; font-size: 1.05rem; }
      .preview__fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
                         gap: 0.25rem 1rem; margin: 0; font-size: 0.8rem; }
      .preview__field { display: flex; gap: 0.4rem; }
      .preview__field dt { font-weight: 600; color: var(--mc-cyan); white-space: nowrap; }
      .preview__field dd { margin: 0; }
    }

    @keyframes preview-fade {
      from { opacity: 0; }
      to { opacity: 1; }
    }
  `,
})
export class ItemPreviewOverlayComponent {
  readonly item = input<ShowcaseDisplayItem | null>(null);

  /**
   * 原圖（full，長邊 1600px）。呼叫端在延遲期間就開始預載，載完才傳進來；
   * 還沒好就先用列表已經快取的 card 圖（480px），不讓浮層空一拍。
   */
  readonly fullImageUrl = input<string | null>(null);

  readonly imageUrl = computed(() => this.fullImageUrl() ?? this.item()?.imageUrl ?? null);

  /**
   * 沿用 Hero 面板那組欄位，但**不含描述**——描述是長文，會把圖片壓掉一大半。
   * 不設欄位數上限：這些是使用者自己在品類設定裡勾 showOnCard 的，程式再砍一次
   * 等於讓那個勾選失效。欄位多時交給 auto-fit 網格自動換欄。
   */
  readonly fields = computed<PreviewField[]>(() => {
    const preview = this.item();

    if (!preview) {
      return [];
    }

    const fields: PreviewField[] = preview.cardAttributes.map((entry) => ({
      label: entry.label,
      value: String(entry.value),
    }));

    if (preview.acquiredAt) {
      fields.push({ label: '入手日期', value: this.date(preview.acquiredAt) });
    }

    if (preview.price) {
      fields.push({
        label: '入手價格',
        value: `${preview.price.amount} ${preview.price.currency}`,
      });
    }

    if (preview.storageLocation) {
      fields.push({ label: '存放位置', value: preview.storageLocation });
    }

    if (preview.rating) {
      fields.push({ label: '評分', value: `${preview.rating} / 10` });
    }

    return fields;
  });

  private readonly datePipe = new DatePipe('en-US');

  private date(value: string): string {
    return this.datePipe.transform(value, 'yyyy-MM-dd') ?? value;
  }
}
