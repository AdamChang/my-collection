import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { API_BASE } from '../../core/api-base';
import { ShareService } from '../../core/api/share.service';
import { PublicShareDto } from '../../core/models';

@Component({
  selector: 'app-public-share',
  template: `
    @if (share(); as data) {
      <main class="public" data-public-terminal>
        <header class="public__header">
          <div>
            <div class="mc-eyebrow">PUBLIC ARCHIVE / {{ data.scope }}</div>
            <h1>{{ data.ownerDisplayName }} 的收藏</h1>
            <p class="mc-muted">{{ data.items.length }} 件</p>
          </div>
        </header>

        <div class="public__wall">
          @for (item of data.items; track item.id) {
            <article class="public__card">
              @if (imageUrl(item.images); as url) {
                <img [src]="url" [alt]="item.name" loading="lazy" />
              } @else {
                <div class="public__placeholder" aria-hidden="true">{{ item.name.charAt(0) }}</div>
              }
              <div class="public__card-body">
                <h2>{{ item.name }}</h2>
                <small>{{ item.categoryName }}</small>
                @if (item.price; as price) {
                  <strong>{{ price.amount }} {{ price.currency }}</strong>
                }
              </div>
            </article>
          }
        </div>
      </main>
    } @else if (notFound()) {
      <main class="public public--error mc-panel">
        <div class="mc-eyebrow">ERROR / SHARE UNAVAILABLE</div>
        <h1>分享連結無法使用</h1>
        <p>找不到這個分享連結，可能已被刪除或過期。</p>
      </main>
    }
  `,
  styles: `
    .public { max-width: 72rem; margin: 2rem auto; padding: 0 1rem; }
    .public__header { display: flex; justify-content: space-between; gap: 1rem; margin-bottom: 1.5rem; }
    .public__header h1, .public__header p { margin: 0.35rem 0 0; }
    .public__wall { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 1rem; }
    .public__card { overflow: hidden; border: 1px solid var(--mc-border); background: var(--mc-surface); box-shadow: var(--mc-shadow);
                    clip-path: polygon(0 0, calc(100% - var(--mc-cut)) 0, 100% var(--mc-cut), 100% 100%, 0 100%); }
    .public__card img, .public__placeholder { width: 100%; display: block; aspect-ratio: 4 / 3; object-fit: cover; }
    .public__placeholder { display: grid; place-items: center; background: var(--mc-surface-raised); color: var(--mc-cyan); font-size: 2rem; }
    .public__card-body { display: grid; gap: 0.25rem; padding: 0.75rem; }
    .public__card h2 { font-size: 0.95rem; margin: 0; }
    .public__card small { color: var(--mc-text-muted); }
    .public__card strong { color: var(--mc-warning); font-size: 0.85rem; }
    .public--error { max-width: 42rem; padding: clamp(1rem, 3vw, 1.5rem); }
    .public--error h1 { margin: 0.4rem 0; }
    .public--error p { margin: 0; color: var(--mc-text-muted); }
    @media (max-width: 520px) {
      .public { margin-block: 1rem; }
      .public__wall { grid-template-columns: 1fr; }
    }
  `,
})
export class PublicShareComponent {
  private readonly api = inject(ShareService);
  private readonly route = inject(ActivatedRoute);

  readonly share = signal<PublicShareDto | null>(null);
  readonly notFound = signal(false);

  constructor() {
    const slug = this.route.snapshot.paramMap.get('slug')!;

    this.api.getPublic(slug).subscribe({
      next: (data) => this.share.set(data),
      error: () => this.notFound.set(true),
    });
  }

  imageUrl(images: PublicShareDto['items'][number]['images']): string | null {
    const primary = images.find((i) => i.isPrimary) ?? images[0];
    return primary ? `${API_BASE}/media/${primary.cardPath}` : null;
  }
}
