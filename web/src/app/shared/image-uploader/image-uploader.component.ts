import { Component, input, output, signal } from '@angular/core';
import { API_BASE } from '../../core/api-base';
import { ItemImageDto } from '../../core/models';
import { AuthenticatedMediaDirective } from '../authenticated-media.directive';

@Component({
  selector: 'app-image-uploader',
  imports: [AuthenticatedMediaDirective],
  template: `
    <div class="uploader">
      <div class="uploader__grid">
        @for (image of images(); track image.id) {
          <figure class="uploader__item" [class.uploader__item--primary]="image.isPrimary">
            <img [appAuthenticatedMedia]="mediaUrl(image.cardPath)" alt="" />
            <figcaption>
              @if (image.isPrimary) {
                <span
                  class="uploader__primary-badge mc-badge"
                  data-primary-status
                  aria-label="目前主圖"
                >主圖</span>
              } @else {
                <button type="button" (click)="setPrimary.emit(image.id)">設為主圖</button>
              }
              <button type="button" (click)="remove.emit(image.id)">刪除</button>
            </figcaption>
          </figure>
        }
      </div>

      <label class="uploader__drop">
        <input type="file" accept="image/*" multiple (change)="onSelected($event)" />
        <span>{{ busy() ? '上傳中…' : '選擇或拖放圖片（單張上限 10 MB）' }}</span>
      </label>
    </div>
  `,
  styles: `
    .uploader { display: grid; gap: 0.8rem; }
    .uploader__grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(130px, 1fr)); gap: 0.7rem; }
    .uploader__item { margin: 0; border: 1px solid var(--mc-border); padding: 0.4rem; background: var(--mc-surface); }
    .uploader__item img { width: 100%; aspect-ratio: 1; object-fit: cover; }
    .uploader__item--primary { border-color: var(--mc-warning); }
    .uploader__item figcaption { display: flex; flex-wrap: wrap; align-items: center; gap: 0.4rem; }
    .uploader__primary-badge { border-color: var(--mc-warning); color: var(--mc-warning); font-weight: 700; }
    .uploader__drop { display: grid; place-items: center; min-height: 8rem; border: 1px dashed var(--mc-cyan);
      padding: 1rem; background: var(--mc-cyan-soft); color: var(--mc-cyan); cursor: pointer; }
  `,
})
export class ImageUploaderComponent {
  readonly images = input<ItemImageDto[]>([]);
  readonly busy = signal(false);

  readonly upload = output<File[]>();
  readonly remove = output<string>();
  readonly setPrimary = output<string>();

  mediaUrl(path: string): string {
    return `${API_BASE}/media/${path}`;
  }

  onSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);

    if (files.length > 0) {
      this.upload.emit(files);
    }

    input.value = '';
  }
}
