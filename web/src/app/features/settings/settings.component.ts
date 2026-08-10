import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { ShareService } from '../../core/api/share.service';
import { IGNORE_HANDLED_BY_INTERCEPTOR } from '../../core/error.interceptor';
import { NotificationService } from '../../core/notification.service';
import { ShareLinkDto, SyncJobDto } from '../../core/models';
import { ImageTransferComponent } from './image-transfer.component';
import { ProviderAccountComponent } from './provider-account.component';
import { ProviderEnrichComponent } from './provider-enrich.component';

@Component({
  selector: 'app-settings',
  imports: [FormsModule, DatePipe, ImageTransferComponent, ProviderEnrichComponent, ProviderAccountComponent],
  template: `
    <header class="settings__header">
      <div class="mc-eyebrow">CONNECTIONS / CONTROL DECK</div>
      <h1>設定</h1>
    </header>

    <app-provider-account
      provider="steam"
      heading="Steam 帳號"
      userIdLabel="SteamID64"
      secretLabel="Web API Key"
      hint="個人資料需設為公開，否則 Steam 回傳空清單。"
      (changed)="reloadJobs()"
    />

    <app-provider-account
      provider="psn"
      heading="PSN 帳號"
      [requiresUserId]="false"
      secretLabel="NPSSO"
      hint="登入 playstation.com 後，於同一瀏覽器開啟 ca.account.sony.com/api/v1/ssocookie，取回應中的 64 字元字串。約兩個月過期，需重新取得。"
      (changed)="reloadJobs()"
    />

    <section class="settings__panel mc-panel" data-settings-panel>
      <div class="mc-eyebrow">SYNC TELEMETRY</div>
      <h2>同步紀錄</h2>
      <div class="settings__table-scroll">
        <table>
          <thead>
            <tr><th>時間</th><th>來源</th><th>狀態</th><th>新增</th><th>更新</th><th>略過</th><th>失敗</th></tr>
          </thead>
          <tbody>
            @for (job of jobs(); track job.id) {
              <tr>
                <td>{{ job.startedAt | date: 'yyyy-MM-dd HH:mm' }}</td>
                <td>{{ job.provider }}</td>
                <td>
                  <span
                    class="sync-status"
                    [class.sync-status--ok]="job.status === 'Succeeded'"
                    [class.sync-status--error]="job.status === 'Failed'"
                    [attr.aria-label]="job.error ? job.status + ': ' + job.error : null"
                  >{{ job.status }}</span>
                  @if (job.error) {
                    <span class="sync-status__detail">{{ job.error }}</span>
                  }
                  @if (job.status === 'Failed') {
                    <button type="button" (click)="retry(job.id)" [disabled]="retryingJobId() !== null">
                      {{ retryingJobId() === job.id ? '重排中…' : '重新執行' }}
                    </button>
                  }
                </td>
                <td>{{ job.created }}</td>
                <td>{{ job.updated }}</td>
                <td>{{ job.skipped }}</td>
                <td>{{ job.failed }}</td>
              </tr>
            } @empty {
              <tr><td colspan="7">尚無同步紀錄。</td></tr>
            }
          </tbody>
        </table>
      </div>
    </section>

    <app-provider-enrich
      provider="igdb"
      heading="IGDB 補完"
      description="替 Steam 同步進來、還沒有 IGDB 資料的遊戲補上開發商、發行商、發售日期、類型、平台與評分。既有的標籤、精選狀態與購入資訊都不會被改動。"
      (completed)="reloadJobs()"
    />

    <app-provider-enrich
      provider="steam"
      heading="Steam 繁體中文補完"
      description="向 Steam 商店取得繁體中文的品名、簡介與類型，覆蓋既有的英文內容。沒有官方繁中版的遊戲會維持原文。作業在背景進行，可以離開此頁。"
      (completed)="reloadJobs()"
    />

    <section class="settings__panel mc-panel" data-settings-panel>
      <div class="mc-eyebrow">PUBLIC ACCESS</div>
      <h2>分享連結</h2>

      <label class="settings__inline">
        <input type="checkbox" [(ngModel)]="includePrice" name="includePrice" />
        包含購入價格（預設不含，也控制購買日期是否顯示）
      </label>
      <label class="settings__inline">
        <input type="checkbox" [(ngModel)]="includeRating" name="includeRating" />
        包含收藏評分（預設不含）
      </label>
      <label class="settings__inline">
        照片牆槽位數量
        <input type="number" min="1" max="10" [(ngModel)]="collageSlotCount" name="collageSlotCount" />
      </label>
      <button type="button" (click)="createShare()" [disabled]="busy()" data-create-share>
        {{ creatingShare() ? '建立中…' : '建立分享連結' }}
      </button>

      <ul>
        @for (share of shares(); track share.id) {
          <li>
            <a [href]="'/p/' + share.slug" target="_blank" rel="noopener">/p/{{ share.slug }}</a>
            <span>{{ share.scope }}</span>
            @if (share.includePrice) { <span>含價格</span> }
            @if (share.includeRating) { <span>含評分</span> }
            <span>照片牆 {{ share.collageSlotCount }} 格</span>
            <button type="button" (click)="removeShare(share.id)" [disabled]="busy()">
              {{ removingShareId() === share.id ? '刪除中…' : '刪除' }}
            </button>
          </li>
        }
      </ul>
    </section>

    <app-image-transfer />
  `,
  styles: `
    .settings__header { margin: 0 0 1.5rem; }
    .settings__header h1 { margin: 0.35rem 0 0; }
    .settings__panel { margin-block: 1.5rem; display: grid; gap: 0.75rem; justify-items: start; }
    .settings__panel h2 { margin: 0; font-size: 1.1rem; }
    .settings__table-scroll { width: 100%; overflow-x: auto; }
    table { border-collapse: collapse; width: 100%; min-width: 42rem; }
    th, td { border-bottom: 1px solid var(--mc-border); padding: 0.5rem; text-align: left; vertical-align: top; }
    .hint { color: var(--mc-text-muted); font-size: 0.85rem; }
    .settings__inline { display: flex; gap: 0.5rem; align-items: center; }
    .sync-status { display: inline-flex; border: 1px solid var(--mc-border-strong); padding: 0.15rem 0.4rem; font: 700 0.72rem/1.4 Consolas, monospace; }
    .sync-status--ok { border-color: var(--mc-success); color: var(--mc-success); }
    .sync-status--error { border-color: var(--mc-danger); color: var(--mc-danger); }
    .sync-status__detail { display: block; max-width: 20rem; margin-top: 0.35rem; color: var(--mc-text-muted); font-size: 0.8rem; overflow-wrap: anywhere; }
    ul { width: 100%; margin: 0; padding: 0; list-style: none; }
    li { display: flex; flex-wrap: wrap; align-items: center; gap: 0.65rem; padding: 0.65rem 0; border-bottom: 1px solid var(--mc-border); }
    @media (max-width: 520px) {
      .settings__panel { margin-block: 1rem; }
    }
  `,
})
export class SettingsComponent {
  private readonly ingestion = inject(IngestionService);
  private readonly shareApi = inject(ShareService);
  private readonly notifications = inject(NotificationService);

  readonly jobs = signal<SyncJobDto[]>([]);
  readonly shares = signal<ShareLinkDto[]>([]);
  readonly creatingShare = signal(false);
  readonly removingShareId = signal<string | null>(null);
  readonly retryingJobId = signal<string | null>(null);

  /** 分享連結的兩個動作互相排斥；各來源面板的忙碌狀態由面板自己管。 */
  readonly busy = computed(() => this.creatingShare() || this.removingShareId() !== null);

  includePrice = false;
  includeRating = false;
  collageSlotCount = 4;

  constructor() {
    this.reloadJobs();
    this.reloadShares();
  }

  createShare(): void {
    if (this.busy()) {
      return;
    }

    this.creatingShare.set(true);
    this.shareApi
      .create({
        scope: 'Showcase',
        includeCategoryIds: [],
        includePrice: this.includePrice,
        includeRating: this.includeRating,
        collageSlotCount: this.collageSlotCount,
        expiresAt: null,
      })
      .pipe(finalize(() => this.creatingShare.set(false)))
      .subscribe({
        next: () => {
          this.notifications.success('已建立分享連結。');
          this.reloadShares();
        },
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }

  removeShare(id: string): void {
    if (this.busy()) {
      return;
    }

    this.removingShareId.set(id);
    this.shareApi
      .remove(id)
      .pipe(finalize(() => this.removingShareId.set(null)))
      .subscribe({
        next: () => this.reloadShares(),
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }

  protected reloadJobs(): void {
    this.ingestion.jobs().subscribe((jobs) => this.jobs.set(jobs));
  }

  protected retry(jobId: string): void {
    if (this.retryingJobId() !== null) {
      return;
    }

    this.retryingJobId.set(jobId);
    this.ingestion.retry(jobId)
      .pipe(finalize(() => this.retryingJobId.set(null)))
      .subscribe({
        next: () => {
          this.notifications.success('失敗作業已重新排入佇列。');
          this.reloadJobs();
        },
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }

  private reloadShares(): void {
    this.shareApi.list().subscribe((shares) => this.shares.set(shares));
  }
}
