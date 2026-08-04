import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { API_BASE } from '../../core/api-base';
import { CategoryFieldDto, ItemDto } from '../../core/models';

@Component({
  selector: 'app-item-card',
  imports: [RouterLink],
  template: `
    <a class="card" data-item-card [attr.aria-label]="'查看 ' + item().name" [routerLink]="['/items', item().id]">
      @if (imageUrl(); as url) {
        <img [src]="url" [alt]="item().name" loading="lazy" />
      } @else {
        <div class="card__placeholder" data-placeholder>{{ item().name.charAt(0) }}</div>
      }

      <div class="card__body">
        <h3 class="card__title">{{ item().name }}</h3>
        @if (item().isShowcased) {
          <span class="card__badge" data-showcased>精選</span>
        }
        @if (cardAttributes().length) {
          <dl class="card__fields" data-card-fields>
            @for (entry of cardAttributes(); track entry.key) {
              <dt>{{ entry.label }}</dt>
              <dd>{{ entry.value }}</dd>
            }
          </dl>
        }
        @if (item().tags.length) {
          <ul class="card__tags">
            @for (tag of item().tags; track tag) {
              <li>{{ tag }}</li>
            }
          </ul>
        }
      </div>
    </a>
  `,
  styles: `
    .card { display: block; position: relative; overflow: hidden; border: 1px solid var(--mc-border); background: var(--mc-surface);
            box-shadow: var(--mc-shadow); color: var(--mc-text); text-decoration: none; transition: transform 160ms ease, border-color 160ms ease;
            clip-path: polygon(0 0, calc(100% - var(--mc-cut)) 0, 100% var(--mc-cut), 100% 100%, 0 100%); }
    .card:hover { border-color: var(--mc-cyan); transform: translateY(-3px); }
    .card::after { content: ''; position: absolute; inset: 0 0 auto; height: 42%; pointer-events: none;
                   background: linear-gradient(180deg, transparent 55%, rgb(5 7 13 / 48%)); }
    .card img { width: 100%; display: block; aspect-ratio: 4 / 3; object-fit: cover; }
    .card__placeholder { display: grid; place-items: center; aspect-ratio: 4 / 3;
                         background: var(--mc-surface-raised); font-size: 2rem; color: var(--mc-cyan); }
    .card__body { padding: 0.75rem; display: grid; gap: 0.4rem; }
    .card__title { font-size: 0.95rem; margin: 0; }
    .card__badge { justify-self: start; font-size: 0.7rem; padding: 0.1rem 0.4rem;
                   border: 1px solid var(--mc-warning); color: var(--mc-warning); }
    .card__tags { display: flex; flex-wrap: wrap; gap: 0.25rem; list-style: none; margin: 0; padding: 0; }
    .card__tags li { font-size: 0.7rem; padding: 0.1rem 0.4rem; border: 1px solid var(--mc-border); color: var(--mc-text-muted); }
    .card__fields { display: grid; grid-template-columns: auto 1fr; gap: 0 0.4rem;
                    margin: 0; font-size: 0.75rem; color: var(--mc-text-muted); }
    .card__fields dt { font-weight: 600; }
    .card__fields dd { margin: 0; }
  `,
})
export class ItemCardComponent {
  readonly item = input.required<ItemDto>();

  /** 所屬品類的 fields。只有 showOnCard 的欄位會出現在卡片上。 */
  readonly cardFields = input<CategoryFieldDto[]>([]);

  readonly cardAttributes = computed(() => {
    const attributes = this.cardFields()
      .filter((f) => f.showOnCard)
      .map((f) => ({ key: f.key, label: f.label, value: this.item().attributes[f.key] }))
      .filter((entry) => this.isPopulated(entry.value));

    return attributes.some((entry) => entry.key === 'playtimeForever')
      ? attributes.filter((entry) => entry.key !== 'psnProgress')
      : attributes;
  });

  private isPopulated(value: unknown): boolean {
    return value !== null && value !== undefined && value !== '';
  }

  /**
   * 本地圖片優先；同步進來但尚未被設為 Showcase 的品項還沒下載圖片，
   * 直接引用 provider CDN。
   */
  readonly imageUrl = computed(() => {
    const value = this.item();

    const primary = value.images.find((i) => i.isPrimary) ?? value.images[0];
    if (primary) {
      return `${API_BASE}/media/${primary.cardPath}`;
    }

    for (const key of ['headerUrl', 'iconUrl']) {
      const candidate = value.attributes[key];
      if (typeof candidate === 'string' && candidate.startsWith('http')) {
        return candidate;
      }
    }

    return null;
  });
}
