import { Component, computed, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { TransferService } from '../../core/api/transfer.service';
import { IGNORE_HANDLED_BY_INTERCEPTOR } from '../../core/error.interceptor';
import { NotificationService } from '../../core/notification.service';
import { ImageImportResultDto } from '../../core/models';

@Component({
  selector: 'app-image-transfer',
  template: `
    <section class="transfer mc-panel" data-settings-panel>
      <div class="mc-eyebrow">IMAGE TRANSFER</div>
      <h2>匯出／匯入圖片</h2>

      <p class="hint">
        收藏資料存在共用的資料庫，每台機器看到的本來就是同一份；只有圖片存在各自的本地儲存區，
        需要手動搬一次。匯出會產生一個 ZIP，內含所有圖片的原圖、卡片圖與縮圖。
      </p>

      <button type="button" (click)="exportArchive()" [disabled]="busy()" data-export>
        {{ exporting() ? '匯出中…' : '匯出圖片封存檔' }}
      </button>

      <hr />

      <label class="transfer__file">
        選擇圖片封存檔
        <input type="file" accept=".zip" (change)="pick($event)" [disabled]="busy()" />
      </label>

      @if (selected(); as file) {
        <p>已選擇：<code>{{ file.name }}</code></p>
        <p class="hint">
          匯入只會補上這台機器還沒有的圖檔，不會覆蓋既有檔案，也不會改動任何收藏資料。
        </p>
        <button type="button" (click)="importArchive()" [disabled]="busy()" data-import>
          {{ importing() ? '匯入中…' : '開始匯入' }}
        </button>
      }

      @if (result(); as summary) {
        <div class="mc-panel transfer__result">
          <h3>匯入完成</h3>
          <p>寫入 {{ summary.written }} 個圖檔，略過 {{ summary.skipped }} 個（這台機器上已經有了）。</p>
          @if (summary.warnings.length) {
            <ul>
              @for (warning of summary.warnings; track warning) {
                <li>{{ warning }}</li>
              }
            </ul>
          }
        </div>
      }
    </section>
  `,
  styles: `
    .transfer { margin-block: 1.5rem; display: grid; gap: 0.75rem; justify-items: start; }
    .transfer h2 { margin: 0; font-size: 1.1rem; }
    .transfer h3 { margin: 0 0 0.5rem; font-size: 1rem; }
    .hint { color: var(--mc-text-muted); font-size: 0.85rem; }
    hr { width: 100%; margin: 0; border: 0; border-top: 1px solid var(--mc-border); }
    .transfer__file { display: grid; gap: 0.35rem; justify-items: start; }
    .transfer__result { display: grid; gap: 0.5rem; justify-items: start; }
    ul { margin: 0; padding-left: 1.1rem; display: grid; gap: 0.25rem; font-size: 0.85rem; }
    @media (max-width: 520px) {
      .transfer { margin-block: 1rem; }
    }
  `,
})
export class ImageTransferComponent {
  private readonly transfer = inject(TransferService);
  private readonly notifications = inject(NotificationService);

  protected readonly exporting = signal(false);
  protected readonly importing = signal(false);
  protected readonly selected = signal<File | null>(null);
  protected readonly result = signal<ImageImportResultDto | null>(null);

  protected readonly busy = computed(() => this.exporting() || this.importing());

  protected pick(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selected.set(input.files?.[0] ?? null);
    this.result.set(null);
  }

  protected exportArchive(): void {
    if (this.busy()) {
      return;
    }

    this.exporting.set(true);

    this.transfer
      .export()
      .pipe(finalize(() => this.exporting.set(false)))
      .subscribe({
        next: (blob) => this.download(blob),
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }

  protected importArchive(): void {
    const file = this.selected();

    if (!file || this.busy()) {
      return;
    }

    this.importing.set(true);

    this.transfer
      .import(file)
      .pipe(finalize(() => this.importing.set(false)))
      .subscribe({
        next: (summary) => {
          this.result.set(summary);
          this.selected.set(null);
          this.notifications.success('匯入完成');
        },
        // 失敗時刻意保留已選檔案，使用者修好封存檔後可直接重試。
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }

  private download(blob: Blob): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');

    anchor.href = url;
    anchor.download = `mycollection-images-${new Date().toISOString().slice(0, 10)}.zip`;
    anchor.click();

    // 立刻 revoke 會讓部分瀏覽器在下載真正開始前就失去這個 URL，隔一個 tick 才安全。
    setTimeout(() => URL.revokeObjectURL(url));
  }
}
