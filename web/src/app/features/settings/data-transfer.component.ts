import { Component, computed, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { TransferService } from '../../core/api/transfer.service';
import { IGNORE_HANDLED_BY_INTERCEPTOR } from '../../core/error.interceptor';
import { NotificationService } from '../../core/notification.service';
import { ImportResultDto } from '../../core/models';

@Component({
  selector: 'app-data-transfer',
  template: `
    <section class="transfer mc-panel" data-settings-panel>
      <div class="mc-eyebrow">DATA TRANSFER</div>
      <h2>匯出／匯入收藏</h2>

      <p class="hint">
        匯出會產生一個含品類、手建品項與圖片的 ZIP。Steam 同步來的品項不在其中，
        另一台機器重跑一次同步即可取得。
      </p>

      <button type="button" (click)="exportArchive()" [disabled]="busy()" data-export>
        {{ exporting() ? '匯出中…' : '匯出封存檔' }}
      </button>

      <hr />

      <label class="transfer__file">
        選擇封存檔
        <input type="file" accept=".zip" (change)="pick($event)" [disabled]="busy()" />
      </label>

      @if (selected(); as file) {
        <p>已選擇：<code>{{ file.name }}</code></p>
        <button type="button" (click)="confirming.set(true)" [disabled]="busy()">匯入…</button>
      }

      @if (confirming()) {
        <div class="mc-panel transfer__danger" role="alertdialog" aria-labelledby="import-warning">
          <h3 id="import-warning">這會覆蓋這台機器上的收藏</h3>
          <ul>
            <li>刪除所有手建品項與其圖片（Steam 同步來的品項會保留）</li>
            <li>刪除所有自訂品類與公開分享連結</li>
            <li>以封存檔的內容重新寫入</li>
          </ul>
          <p>
            系統會在動手前自動備份到伺服器的 <code>data/backups</code>。
            但匯入過程無法回滾，中途失敗會留下不完整的資料，需要用備份還原。
          </p>
          <button type="button" (click)="importArchive()" [disabled]="busy()" data-confirm-import>
            {{ importing() ? '匯入中…' : '確定覆蓋' }}
          </button>
          <button type="button" (click)="confirming.set(false)" [disabled]="busy()">取消</button>
        </div>
      }

      @if (result(); as summary) {
        <div class="mc-panel transfer__result">
          <h3>匯入完成</h3>
          <p>品類 {{ summary.categories }} 個、品項 {{ summary.items }} 筆、圖片 {{ summary.images }} 張。</p>
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
    .transfer__danger { border-color: var(--mc-danger); display: grid; gap: 0.5rem; justify-items: start; }
    .transfer__danger h3 { color: var(--mc-danger); }
    .transfer__result { display: grid; gap: 0.5rem; justify-items: start; }
    ul { margin: 0; padding-left: 1.1rem; display: grid; gap: 0.25rem; font-size: 0.85rem; }
    @media (max-width: 520px) {
      .transfer { margin-block: 1rem; }
    }
  `,
})
export class DataTransferComponent {
  private readonly transfer = inject(TransferService);
  private readonly notifications = inject(NotificationService);

  protected readonly exporting = signal(false);
  protected readonly importing = signal(false);
  protected readonly confirming = signal(false);
  protected readonly selected = signal<File | null>(null);
  protected readonly result = signal<ImportResultDto | null>(null);

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
      .pipe(
        finalize(() => {
          this.importing.set(false);
          this.confirming.set(false);
        }),
      )
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
    anchor.download = `mycollection-${new Date().toISOString().slice(0, 10)}.zip`;
    anchor.click();

    // 立刻 revoke 會讓部分瀏覽器在下載真正開始前就失去這個 URL，隔一個 tick 才安全。
    setTimeout(() => URL.revokeObjectURL(url));
  }
}
