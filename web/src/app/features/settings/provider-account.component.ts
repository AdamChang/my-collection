import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { IGNORE_HANDLED_BY_INTERCEPTOR } from '../../core/error.interceptor';
import { NotificationService } from '../../core/notification.service';
import { ExternalAccountDto } from '../../core/models';

/**
 * 單一來源的帳號綁定入口。吃 provider key 而不是綁死單一來源——
 * 兩個來源的表單形狀不同（Steam 要使用者 ID，PSN 的識別碼固定是 'me'），
 * 但綁定、解綁、觸發同步這三段流程完全一樣，複製一份就會開始分岔。
 */
@Component({
  selector: 'app-provider-account',
  imports: [FormsModule, DatePipe],
  template: `
    <section class="account mc-panel" data-settings-panel [attr.data-provider-account]="provider()">
      <div class="mc-eyebrow">ACCOUNT LINK</div>
      <h2>{{ heading() }}</h2>

      @if (account(); as bound) {
        @if (requiresUserId()) {
          <p>已綁定 {{ userIdLabel() }}：<code>{{ bound.externalUserId }}</code></p>
        } @else {
          <p>已綁定（更新於 {{ bound.updatedAt | date: 'yyyy-MM-dd HH:mm' }}）</p>
        }
        <button
          type="button"
          (click)="sync()"
          [disabled]="busy()"
          [attr.data-provider-account-sync]="provider()"
        >
          {{ syncing() ? '同步中…' : '立即同步' }}
        </button>
        <button
          type="button"
          (click)="unlink()"
          [disabled]="busy()"
          [attr.data-provider-account-unlink]="provider()"
        >
          {{ unlinking() ? '解除中…' : '解除綁定' }}
        </button>
      } @else {
        <form (ngSubmit)="link()">
          @if (requiresUserId()) {
            <label>{{ userIdLabel() }}<input [(ngModel)]="userId" name="userId" required /></label>
          }
          <label>{{ secretLabel() }}<input [(ngModel)]="secret" name="secret" type="password" required /></label>
          @if (hint()) {
            <p class="hint">{{ hint() }}</p>
          }
          <button type="submit" [disabled]="busy()">{{ linking() ? '綁定中…' : '綁定' }}</button>
        </form>
      }
    </section>
  `,
  styles: `
    .account { margin-block: 1.5rem; display: grid; gap: 0.75rem; justify-items: start; }
    .account h2 { margin: 0; font-size: 1.1rem; }
    .hint { margin: 0; color: var(--mc-text-muted); font-size: 0.85rem; }
    @media (max-width: 520px) {
      .account { margin-block: 1rem; }
    }
  `,
})
export class ProviderAccountComponent implements OnInit {
  private readonly ingestion = inject(IngestionService);
  private readonly notifications = inject(NotificationService);

  readonly provider = input.required<string>();
  readonly heading = input.required<string>();
  readonly secretLabel = input.required<string>();
  readonly hint = input('');

  /** false 時不渲染使用者 ID 欄位，綁定一律送 fixedUserId。 */
  readonly requiresUserId = input(true);

  /**
   * 刻意不是 input.required——PSN 實例不會傳它。
   * 「requiresUserId 為 true 時要給」是呼叫端的義務，不是型別層的約束。
   */
  readonly userIdLabel = input('');

  readonly fixedUserId = input('me');

  /**
   * 綁定、解綁與同步後都要發。只有同步在失敗時也發——失敗的同步仍會在後端
   * 留下一筆作業紀錄；綁定與解綁是單純的寫入，失敗時沒有東西需要重載。
   */
  readonly changed = output<void>();

  protected readonly account = signal<ExternalAccountDto | null>(null);
  protected readonly linking = signal(false);
  protected readonly unlinking = signal(false);
  protected readonly syncing = signal(false);

  /** 只涵蓋本面板自己的動作，不鎖其他來源與頁面上的其他區塊。 */
  protected readonly busy = computed(() => this.linking() || this.unlinking() || this.syncing());

  userId = '';
  secret = '';

  /**
   * 不能放建構子：這裡讀 provider() 等 required input 時，
   * TestBed／樣板綁定都還沒把值送進來，會丟 NG0950。
   * ngOnInit 保證跑在第一輪 input 綁定之後。
   */
  ngOnInit(): void {
    this.reload();
  }

  protected link(): void {
    if (this.busy()) {
      return;
    }

    this.linking.set(true);
    this.ingestion
      .link(this.provider(), this.requiresUserId() ? this.userId : this.fixedUserId(), this.secret)
      .pipe(finalize(() => this.linking.set(false)))
      .subscribe({
        next: () => {
          this.secret = '';
          this.notifications.success(`已綁定 ${this.heading()}。`);
          this.reload();
          this.changed.emit();
        },
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }

  protected unlink(): void {
    if (this.busy()) {
      return;
    }

    this.unlinking.set(true);
    this.ingestion
      .unlink(this.provider())
      .pipe(finalize(() => this.unlinking.set(false)))
      .subscribe({
        next: () => {
          this.userId = '';
          this.notifications.success('已解除綁定。');
          this.reload();
          this.changed.emit();
        },
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }

  protected sync(): void {
    if (this.busy()) {
      return;
    }

    this.syncing.set(true);
    this.ingestion
      .sync(this.provider())
      .pipe(
        finalize(() => {
          this.syncing.set(false);
          // 失敗的同步也會在後端留下一筆紀錄，兩條路徑都要讓父層重載。
          this.changed.emit();
        }),
      )
      .subscribe({
        next: (job) =>
          this.notifications.success(
            `同步完成：新增 ${job.created}、更新 ${job.updated}、失敗 ${job.failed}`,
          ),
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }

  private reload(): void {
    this.ingestion
      .accounts()
      .subscribe((accounts) =>
        this.account.set(accounts.find((a) => a.provider === this.provider()) ?? null),
      );
  }
}
