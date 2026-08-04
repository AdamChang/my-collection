import { Component, computed, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { ProviderService } from '../../core/api/provider.service';
import { IGNORE_HANDLED_BY_INTERCEPTOR } from '../../core/error.interceptor';
import { NotificationService } from '../../core/notification.service';

/**
 * 批次補完的入口。吃 provider key 而不是綁死單一來源——
 * 出現第二個來源（Steam 商店的繁體中文補完）時複製一份，
 * 載入狀態、錯誤處理與 job 重載的邏輯就會開始分岔。
 */
@Component({
  selector: 'app-provider-enrich',
  template: `
    @if (available()) {
      <section class="enrich mc-panel" data-settings-panel [attr.data-provider-enrich]="provider()">
        <div class="mc-eyebrow">METADATA BACKFILL</div>
        <h2>{{ heading() }}</h2>

        <p class="hint">{{ description() }}</p>
        <p class="hint">
          一次處理最多 50 筆。補過的品項不會再被挑中，所以再按一次就是下一批。
        </p>

        <button
          type="button"
          (click)="run()"
          [disabled]="running()"
          [attr.data-provider-enrich-run]="provider()"
        >
          {{ running() ? '補完中…' : '批次補完' }}
        </button>
      </section>
    }
  `,
  styles: `
    .enrich { margin-block: 1.5rem; display: grid; gap: 0.75rem; justify-items: start; }
    .enrich h2 { margin: 0; font-size: 1.1rem; }
    .hint { margin: 0; color: var(--mc-text-muted); font-size: 0.85rem; }
    @media (max-width: 520px) {
      .enrich { margin-block: 1rem; }
    }
  `,
})
export class ProviderEnrichComponent {
  private readonly ingestion = inject(IngestionService);
  private readonly providers = inject(ProviderService);
  private readonly notifications = inject(NotificationService);

  readonly provider = input.required<string>();
  readonly heading = input.required<string>();
  readonly description = input.required<string>();

  /**
   * 成功與失敗都要發：失敗若發生在 job 建立之後，後端同樣會留下一筆紀錄。
   * 更早的失敗（provider 未註冊、驗證 400、401、連線失敗）沒有 job 可看，重載無害。
   */
  readonly completed = output<void>();

  protected readonly running = signal(false);

  protected readonly available = computed(() => this.providers.supports(this.provider(), 'Enrich'));

  protected run(): void {
    if (this.running()) {
      return;
    }

    this.running.set(true);
    this.ingestion
      .enrich(this.provider())
      .pipe(
        finalize(() => {
          this.running.set(false);
          this.completed.emit();
        }),
      )
      .subscribe({
        next: (job) =>
          this.notifications.success(
            // 背景執行的 provider 回應時工作還沒開始，統計數字全是 0，
            // 報「完成：更新 0」會誤導使用者以為沒東西可補。
            job.status === 'Running'
              ? '補完已排入背景作業，進度請看下方作業紀錄。'
              : `補完完成：更新 ${job.updated}、略過 ${job.skipped}、失敗 ${job.failed}`,
          ),
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }
}
