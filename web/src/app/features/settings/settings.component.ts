import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IngestionService } from '../../core/api/ingestion.service';
import { ShareService } from '../../core/api/share.service';
import { NotificationService } from '../../core/notification.service';
import { ExternalAccountDto, ShareLinkDto, SyncJobDto } from '../../core/models';

@Component({
  selector: 'app-settings',
  imports: [FormsModule, DatePipe],
  template: `
    <header class="settings__header">
      <div class="mc-eyebrow">CONNECTIONS / CONTROL DECK</div>
      <h1>設定</h1>
    </header>

    <section class="settings__panel mc-panel" data-settings-panel>
      <div class="mc-eyebrow">ACCOUNT LINK</div>
      <h2>Steam 帳號</h2>

      @if (steamAccount(); as account) {
        <p>已綁定 SteamID64：<code>{{ account.externalUserId }}</code></p>
        <button type="button" (click)="sync()" [disabled]="syncing()">
          {{ syncing() ? '同步中…' : '立即同步' }}
        </button>
        <button type="button" (click)="unlink()">解除綁定</button>
      } @else {
        <form (ngSubmit)="link()">
          <label>SteamID64<input [(ngModel)]="steamId" name="steamId" required /></label>
          <label>Web API Key<input [(ngModel)]="apiKey" name="apiKey" type="password" required /></label>
          <p class="hint">個人資料需設為公開，否則 Steam 回傳空清單。</p>
          <button type="submit">綁定</button>
        </form>
      }
    </section>

    <section class="settings__panel mc-panel" data-settings-panel>
      <div class="mc-eyebrow">SYNC TELEMETRY</div>
      <h2>同步紀錄</h2>
      <div class="settings__table-scroll">
        <table>
          <thead>
            <tr><th>時間</th><th>來源</th><th>狀態</th><th>新增</th><th>更新</th><th>失敗</th></tr>
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
                </td>
                <td>{{ job.created }}</td>
                <td>{{ job.updated }}</td>
                <td>{{ job.failed }}</td>
              </tr>
            } @empty {
              <tr><td colspan="6">尚無同步紀錄。</td></tr>
            }
          </tbody>
        </table>
      </div>
    </section>

    <section class="settings__panel mc-panel" data-settings-panel>
      <div class="mc-eyebrow">PUBLIC ACCESS</div>
      <h2>分享連結</h2>

      <label class="settings__inline">
        <input type="checkbox" [(ngModel)]="includePrice" name="includePrice" />
        包含購入價格（預設不含）
      </label>
      <button type="button" (click)="createShare()">建立分享連結</button>

      <ul>
        @for (share of shares(); track share.id) {
          <li>
            <a [href]="'/p/' + share.slug" target="_blank" rel="noopener">/p/{{ share.slug }}</a>
            <span>{{ share.scope }}</span>
            @if (share.includePrice) { <span>含價格</span> }
            <button type="button" (click)="removeShare(share.id)">刪除</button>
          </li>
        }
      </ul>
    </section>
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

  readonly steamAccount = signal<ExternalAccountDto | null>(null);
  readonly jobs = signal<SyncJobDto[]>([]);
  readonly shares = signal<ShareLinkDto[]>([]);
  readonly syncing = signal(false);

  steamId = '';
  apiKey = '';
  includePrice = false;

  constructor() {
    this.reloadAccounts();
    this.reloadJobs();
    this.reloadShares();
  }

  link(): void {
    this.ingestion.link('steam', this.steamId, this.apiKey).subscribe(() => {
      this.apiKey = '';
      this.notifications.success('已綁定 Steam 帳號。');
      this.reloadAccounts();
    });
  }

  unlink(): void {
    this.ingestion.unlink('steam').subscribe(() => {
      this.notifications.success('已解除綁定。');
      this.reloadAccounts();
    });
  }

  sync(): void {
    this.syncing.set(true);

    this.ingestion.sync('steam').subscribe({
      next: (job) => {
        this.notifications.success(`同步完成：新增 ${job.created}、更新 ${job.updated}、失敗 ${job.failed}`);
        this.syncing.set(false);
        this.reloadJobs();
      },
      error: () => {
        this.syncing.set(false);
        this.reloadJobs();
      },
    });
  }

  createShare(): void {
    this.shareApi
      .create({ scope: 'Showcase', includeCategoryIds: [], includePrice: this.includePrice, expiresAt: null })
      .subscribe(() => {
        this.notifications.success('已建立分享連結。');
        this.reloadShares();
      });
  }

  removeShare(id: string): void {
    this.shareApi.remove(id).subscribe(() => this.reloadShares());
  }

  private reloadAccounts(): void {
    this.ingestion.accounts().subscribe((accounts) =>
      this.steamAccount.set(accounts.find((a) => a.provider === 'steam') ?? null),
    );
  }

  private reloadJobs(): void {
    this.ingestion.jobs().subscribe((jobs) => this.jobs.set(jobs));
  }

  private reloadShares(): void {
    this.shareApi.list().subscribe((shares) => this.shares.set(shares));
  }
}
