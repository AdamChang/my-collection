import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { API_BASE } from '../../core/api-base';
import { ShareService } from '../../core/api/share.service';
import { PublicShareDto } from '../../core/models';

@Component({
  selector: 'app-public-share',
  template: `
    @if (share(); as data) {
      <main class="public">
        <header>
          <h1>{{ data.ownerDisplayName }} 的收藏</h1>
          <p>{{ data.items.length }} 件</p>
        </header>

        <div class="public__wall">
          @for (item of data.items; track item.id) {
            <article class="public__card">
              @if (imageUrl(item.images); as url) {
                <img [src]="url" [alt]="item.name" loading="lazy" />
              }
              <h2>{{ item.name }}</h2>
              <small>{{ item.categoryName }}</small>
              @if (item.price; as price) {
                <strong>{{ price.amount }} {{ price.currency }}</strong>
              }
            </article>
          }
        </div>
      </main>
    } @else if (notFound()) {
      <main class="public"><p>找不到這個分享連結，可能已被刪除或過期。</p></main>
    }
  `,
  styles: `
    .public { max-width: 72rem; margin: 2rem auto; padding: 0 1rem; }
    .public__wall { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 1rem; }
    .public__card { display: grid; gap: 0.25rem; }
    .public__card img { width: 100%; aspect-ratio: 4 / 3; object-fit: cover; border-radius: 0.5rem; }
    .public__card h2 { font-size: 0.95rem; margin: 0; }
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
