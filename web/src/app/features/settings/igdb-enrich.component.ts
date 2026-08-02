import { Component, computed, inject, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { IGDB_PROVIDER_KEY, ProviderService } from '../../core/api/provider.service';
import { IGNORE_HANDLED_BY_INTERCEPTOR } from '../../core/error.interceptor';
import { NotificationService } from '../../core/notification.service';

@Component({
  selector: 'app-igdb-enrich',
  template: `
    @if (available()) {
      <section class="enrich mc-panel" data-settings-panel data-igdb-enrich>
        <div class="mc-eyebrow">METADATA BACKFILL</div>
        <h2>IGDB 補完</h2>

        <p class="hint">
          替 Steam 同步進來、還沒有 IGDB 資料的遊戲補上開發商、發行商、發售日期、類型、平台與評分。
          既有的名稱、標籤、精選狀態與購入資訊都不會被改動。
        </p>
        <p class="hint">
          一次處理最多 50 筆。補過的品項不會再被挑中，所以再按一次就是下一批。
        </p>

        <button type="button" (click)="run()" [disabled]="running()" data-igdb-enrich-run>
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
export class IgdbEnrichComponent {
  private readonly ingestion = inject(IngestionService);
  private readonly providers = inject(ProviderService);
  private readonly notifications = inject(NotificationService);

  /**
   * 成功與失敗都要發：失敗若發生在 job 建立之後，後端同樣會留下一筆紀錄。
   * 更早的失敗（provider 未註冊、驗證 400、401、連線失敗）沒有 job 可看，重載無害。
   */
  readonly completed = output<void>();

  protected readonly running = signal(false);

  protected readonly available = computed(() =>
    this.providers.supports(IGDB_PROVIDER_KEY, 'Search'),
  );

  protected run(): void {
    if (this.running()) {
      return;
    }

    this.running.set(true);
    this.ingestion
      .enrich(IGDB_PROVIDER_KEY)
      .pipe(
        finalize(() => {
          this.running.set(false);
          this.completed.emit();
        }),
      )
      .subscribe({
        next: (job) =>
          this.notifications.success(
            `補完完成：更新 ${job.updated}、略過 ${job.skipped}、失敗 ${job.failed}`,
          ),
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }
}
