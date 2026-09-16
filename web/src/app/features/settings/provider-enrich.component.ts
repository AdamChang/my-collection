import { Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, switchMap } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { ProviderService } from '../../core/api/provider.service';
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
  private readonly destroyRef = inject(DestroyRef);

  readonly provider = input.required<string>();
  readonly heading = input.required<string>();
  readonly description = input.required<string>();

  /**
   * 成功與失敗都要發：失敗若發生在 job 建立之後，後端同樣會留下一筆紀錄。
   * 更早的失敗（provider 未註冊、驗證 400、401、連線失敗）沒有 job 可看，重載無害。
   * 元件銷毀不算——那時設定頁也沒了，沒有表可以重載。
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
        // 雲端部署時 enrich 是背景作業，回應的 job 還是 Running、統計全是 0；
        // 直接報數字會說「更新 0」，使用者會以為沒東西可補。等作業結束再看。
        switchMap((job) => this.ingestion.awaitJob(job)),
        // 最多 50 次反查加上輪詢，是全站最長的操作；Steam 面板還明說「可以離開此頁」。
        // 放在 finalize 之前：銷毀時 finalize 仍會跑（解鎖按鈕無害），但不會走到 next 去 emit。
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.running.set(false)),
      )
      .subscribe({
        next: (job) => {
          // 輪詢逾時：作業仍會在背景完成，不是失敗，但也不能報數字。
          this.notifications.success(
            job.status === 'Running'
              ? '補完仍在背景作業中，稍後重新整理即可在作業紀錄看到結果。'
              : `補完完成：更新 ${job.updated}、略過 ${job.skipped}、失敗 ${job.failed}`,
          );
          this.completed.emit();
        },
        // 錯誤訊息由 interceptor 顯示（原本是 IGNORE_HANDLED_BY_INTERCEPTOR），這裡只負責重載。
        error: () => this.completed.emit(),
      });
  }
}
