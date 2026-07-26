import { Component, input, output, signal } from '@angular/core';
import { API_BASE } from '../../core/api-base';
import { ItemImageDto } from '../../core/models';

@Component({
  selector: 'app-image-uploader',
  template: `
    <div class="uploader">
      <div class="uploader__grid">
        @for (image of images(); track image.id) {
          <figure class="uploader__item" [class.uploader__item--primary]="image.isPrimary">
            <img [src]="mediaUrl(image.cardPath)" alt="" />
            <figcaption>
              @if (!image.isPrimary) {
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
    .uploader { display: grid; gap: 0.75rem; }
    .uploader__grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(120px, 1fr)); gap: 0.5rem; }
    .uploader__item { margin: 0; display: grid; gap: 0.25rem; }
    .uploader__item img { width: 100%; aspect-ratio: 1; object-fit: cover; border-radius: 0.5rem; }
    .uploader__item--primary img { outline: 2px solid #f1c40f; }
    .uploader__drop { display: grid; place-items: center; padding: 1.5rem; gap: 0.5rem;
                      border: 2px dashed #bdc3c7; border-radius: 0.75rem; cursor: pointer; }
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
