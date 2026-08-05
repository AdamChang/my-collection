# Provider Account Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把設定頁寫死 Steam 的帳號綁定抽成可重用元件，並加上 PSN 的 NPSSO 綁定入口。

**Architecture:** 新增 `ProviderAccountComponent`，介面與既有的 `ProviderEnrichComponent` 對稱（吃 provider key、可重複實例化）。`SettingsComponent` 交出 Steam 的綁定狀態，改為擺兩個實例。每個面板持有自己的忙碌狀態，頁面級的 `busy` 鎖移除。

**Tech Stack:** Angular 21（standalone component、signal input/output、`@if` 控制流）、Karma + Jasmine、`FormsModule` 的 `[(ngModel)]`。

**Spec:** `docs/superpowers/specs/2026-08-05-provider-account-panel-design.md`

---

## 背景：讀這些再動手

- `web/src/app/features/settings/provider-enrich.component.ts` — **本次要照抄的模式**。注意它怎麼用 `input.required` / `output` / `finalize` / `IGNORE_HANDLED_BY_INTERCEPTOR`。
- `web/src/app/features/settings/settings.component.ts:22-42` — 要被搬走的 Steam 面板模板。
- `web/src/app/features/settings/settings.component.ts:144-237` — 要被搬走的狀態與三個方法。
- `web/src/app/core/api/ingestion.service.ts` — `link/unlink/sync/accounts` 四個方法**都已經吃 provider key，不需要修改**。

**後端與 `core/` 底下的任何檔案都不要改。**

## 檔案結構

| 檔案 | 責任 |
| --- | --- |
| `web/src/app/features/settings/provider-account.component.ts`（新增） | 單一來源的帳號綁定面板：綁定、解綁、觸發同步，持有自己的忙碌狀態 |
| `web/src/app/features/settings/provider-account.component.spec.ts`（新增） | 上者的測試，含從 `settings.component.spec.ts` 遷移過來的案例 |
| `web/src/app/features/settings/settings.component.ts`（修改） | 交出帳號綁定；保留同步紀錄、分享連結、圖片轉移 |
| `web/src/app/features/settings/settings.component.spec.ts`（修改） | 移除已遷移的案例，修正面板數量斷言 |

## 慣例

- 所有測試指令都在 `web/` 目錄下執行。
- 單檔測試：`npm test -- --watch=false --browsers=ChromeHeadless --include='**/<檔名>.spec.ts'`
- 全部測試：`npm test -- --watch=false --browsers=ChromeHeadless`

---

## Task 1: 建立元件並實作綁定（Steam 形狀）

**Files:**

- Create: `web/src/app/features/settings/provider-account.component.ts`
- Create: `web/src/app/features/settings/provider-account.component.spec.ts`

- [ ] **Step 1: 寫失敗的測試**

Create `web/src/app/features/settings/provider-account.component.spec.ts`:

```ts
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { NotificationService } from '../../core/notification.service';
import { ExternalAccountDto } from '../../core/models';
import { ProviderAccountComponent } from './provider-account.component';

describe('ProviderAccountComponent', () => {
  const create = async (
    inputs: Record<string, unknown>,
    ingestion: Partial<IngestionService>,
  ): Promise<ComponentFixture<ProviderAccountComponent>> => {
    await TestBed.configureTestingModule({
      imports: [ProviderAccountComponent],
      providers: [
        {
          provide: IngestionService,
          useValue: { accounts: () => of([]), ...ingestion },
        },
        { provide: NotificationService, useValue: { success: () => undefined } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ProviderAccountComponent);
    for (const [key, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();

    return fixture;
  };

  const steamInputs = {
    provider: 'steam',
    heading: 'Steam 帳號',
    userIdLabel: 'SteamID64',
    secretLabel: 'Web API Key',
  };

  const submit = (fixture: ComponentFixture<ProviderAccountComponent>): void => {
    const form: HTMLFormElement = fixture.nativeElement.querySelector('form');
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  };

  it('sends the typed user id and secret for a provider that needs both', async () => {
    const link = jasmine.createSpy('link').and.returnValue(of({} as ExternalAccountDto));
    const fixture = await create(steamInputs, { link });

    fixture.componentInstance.userId = '76561197960287930';
    fixture.componentInstance.secret = 'STEAM_KEY';
    submit(fixture);

    expect(link).toHaveBeenCalledWith('steam', '76561197960287930', 'STEAM_KEY');
  });

  it('clears the secret after a successful link', async () => {
    const fixture = await create(steamInputs, {
      link: () => of({} as ExternalAccountDto),
      accounts: () => of([]),
    });

    fixture.componentInstance.userId = '7656';
    fixture.componentInstance.secret = 'STEAM_KEY';
    submit(fixture);

    expect(fixture.componentInstance.secret).toBe('');
  });
});
```

- [ ] **Step 2: 執行測試，確認它失敗**

Run: `npm test -- --watch=false --browsers=ChromeHeadless --include='**/provider-account.component.spec.ts'`

Expected: FAIL，錯誤訊息類似 `Cannot find module './provider-account.component'`。

- [ ] **Step 3: 建立元件**

Create `web/src/app/features/settings/provider-account.component.ts`:

```ts
import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, output, signal } from '@angular/core';
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
export class ProviderAccountComponent {
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

  /** 綁定、解綁與同步後都要發：三者都可能讓父層的同步紀錄需要重載。 */
  readonly changed = output<void>();

  protected readonly account = signal<ExternalAccountDto | null>(null);
  protected readonly linking = signal(false);

  /** 只涵蓋本面板自己的動作，不鎖其他來源與頁面上的其他區塊。 */
  protected readonly busy = computed(() => this.linking());

  userId = '';
  secret = '';

  constructor() {
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

  private reload(): void {
    this.ingestion
      .accounts()
      .subscribe((accounts) =>
        this.account.set(accounts.find((a) => a.provider === this.provider()) ?? null),
      );
  }
}
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `npm test -- --watch=false --browsers=ChromeHeadless --include='**/provider-account.component.spec.ts'`

Expected: PASS，2 個測試通過。

- [ ] **Step 5: Commit**

```bash
git add web/src/app/features/settings/provider-account.component.ts web/src/app/features/settings/provider-account.component.spec.ts
git commit -m "feat(web): add provider account panel with link support"
```

---

## Task 2: PSN 形狀——不需要使用者 ID

**Files:**

- Modify: `web/src/app/features/settings/provider-account.component.spec.ts`
- Modify: `web/src/app/features/settings/provider-account.component.ts`（本 Task 預期**不需要**改，見 Step 3）

- [ ] **Step 1: 寫失敗的測試**

在 `provider-account.component.spec.ts` 的 `describe` 內、最後一個 `it` 之後加入：

```ts
  const psnInputs = {
    provider: 'psn',
    heading: 'PSN 帳號',
    requiresUserId: false,
    secretLabel: 'NPSSO',
  };

  it('sends the fixed user id for a provider that has no user id field', async () => {
    const link = jasmine.createSpy('link').and.returnValue(of({} as ExternalAccountDto));
    const fixture = await create(psnInputs, { link });

    fixture.componentInstance.secret = 'NPSSO_VALUE';
    submit(fixture);

    expect(link).toHaveBeenCalledWith('psn', 'me', 'NPSSO_VALUE');
  });

  it('does not render a user id field when the provider has none', async () => {
    const fixture = await create(psnInputs, {});

    const labels = Array.from(
      fixture.nativeElement.querySelectorAll('label') as NodeListOf<HTMLLabelElement>,
    ).map((label) => label.textContent);

    expect(labels.length).toBe(1);
    expect(labels[0]).toContain('NPSSO');
  });

  it('shows the bound state without the literal user id when the provider has none', async () => {
    const account: ExternalAccountDto = {
      provider: 'psn',
      externalUserId: 'me',
      updatedAt: '2026-08-05T02:30:00Z',
    };
    const fixture = await create(psnInputs, { accounts: () => of([account]) });

    const text: string = fixture.nativeElement.textContent;

    expect(text).toContain('已綁定');
    expect(text).not.toContain('me');
  });
```

- [ ] **Step 2: 執行測試，確認前兩個通過、第三個失敗**

Run: `npm test -- --watch=false --browsers=ChromeHeadless --include='**/provider-account.component.spec.ts'`

Expected: FAIL，只有 `shows the bound state without the literal user id` 失敗。
失敗原因是元件目前完全沒有渲染同步／解綁按鈕，`已綁定` 那段其實已經寫好了——
若這個測試意外通過，代表 `not.toContain('me')` 太寬鬆，停下來檢查，不要跳過。

前兩個測試應該直接通過：`link()` 的 `requiresUserId() ? … : fixedUserId()` 與
模板的 `@if (requiresUserId())` 在 Task 1 已經寫好了。

- [ ] **Step 3: 確認無需修改實作**

本 Task 是為 Task 1 已寫好的分支補上覆蓋。若 Step 2 三個測試全過，直接進 Step 5。
若 `shows the bound state…` 失敗，原因會是 `已綁定（更新於 …）` 那段的日期格式
把 `me` 以外的內容渲染錯了——檢查 `DatePipe` 是否列在 `imports` 中。

- [ ] **Step 4: 執行測試，確認通過**

Run: `npm test -- --watch=false --browsers=ChromeHeadless --include='**/provider-account.component.spec.ts'`

Expected: PASS，5 個測試通過。

- [ ] **Step 5: Commit**

```bash
git add web/src/app/features/settings/provider-account.component.spec.ts
git commit -m "test(web): cover the provider account panel without a user id field"
```

---

## Task 3: 同步與解綁

**Files:**

- Modify: `web/src/app/features/settings/provider-account.component.spec.ts`
- Modify: `web/src/app/features/settings/provider-account.component.ts`

- [ ] **Step 1: 寫失敗的測試**

在 `provider-account.component.spec.ts` 的 `describe` 內加入：

```ts
  const boundSteam: ExternalAccountDto = {
    provider: 'steam',
    externalUserId: '76561197960287930',
    updatedAt: '2026-08-05T02:30:00Z',
  };

  it('does not offer sync before an account is linked', async () => {
    const fixture = await create(steamInputs, {});

    expect(fixture.nativeElement.querySelector('[data-provider-account-sync]')).toBeNull();
  });

  it('syncs the provider it was given and reports the counts', async () => {
    const sync = jasmine.createSpy('sync').and.returnValue(
      of({
        id: 'j1', provider: 'steam', status: 'Succeeded',
        created: 3, updated: 4, failed: 0, skipped: 0,
        error: null, startedAt: '', finishedAt: '',
      }),
    );
    const success = jasmine.createSpy('success');

    await TestBed.configureTestingModule({
      imports: [ProviderAccountComponent],
      providers: [
        {
          provide: IngestionService,
          useValue: { accounts: () => of([boundSteam]), sync },
        },
        { provide: NotificationService, useValue: { success } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ProviderAccountComponent);
    for (const [key, value] of Object.entries(steamInputs)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-provider-account-sync]').click();
    fixture.detectChanges();

    expect(sync).toHaveBeenCalledWith('steam');
    expect(success.calls.mostRecent().args[0]).toContain('新增 3');
  });

  it('unlinks the provider it was given', async () => {
    const unlink = jasmine.createSpy('unlink').and.returnValue(of(undefined));
    const fixture = await create(steamInputs, {
      accounts: () => of([boundSteam]),
      unlink,
    });

    fixture.nativeElement.querySelector('[data-provider-account-unlink]').click();
    fixture.detectChanges();

    expect(unlink).toHaveBeenCalledWith('steam');
  });

  it('emits changed after a sync so the parent can reload its job log', async () => {
    const changed = jasmine.createSpy('changed');
    const fixture = await create(steamInputs, {
      accounts: () => of([boundSteam]),
      sync: () => of({
        id: 'j1', provider: 'steam', status: 'Succeeded',
        created: 0, updated: 0, failed: 0, skipped: 0,
        error: null, startedAt: '', finishedAt: '',
      }),
    });
    fixture.componentInstance.changed.subscribe(changed);

    fixture.nativeElement.querySelector('[data-provider-account-sync]').click();
    fixture.detectChanges();

    expect(changed).toHaveBeenCalled();
  });
```

- [ ] **Step 2: 執行測試，確認它失敗**

Run: `npm test -- --watch=false --browsers=ChromeHeadless --include='**/provider-account.component.spec.ts'`

Expected: FAIL。`does not offer sync before an account is linked` 會通過（按鈕根本還不存在），
其餘三個失敗於 `Cannot read properties of null (reading 'click')`。

- [ ] **Step 3: 加上同步與解綁**

在 `provider-account.component.ts` 的模板中，把已綁定分支改成（替換 `@if (account(); as bound) { … }` 內既有的兩個 `@if/@else` 之後、`}` 之前的位置，加入兩個按鈕）：

```html
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
```

在類別中，把 `busy` 改成涵蓋三個狀態，並加入兩個 signal 與兩個方法：

```ts
  protected readonly unlinking = signal(false);
  protected readonly syncing = signal(false);

  /** 只涵蓋本面板自己的動作，不鎖其他來源與頁面上的其他區塊。 */
  protected readonly busy = computed(() => this.linking() || this.unlinking() || this.syncing());
```

```ts
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
```

**注意**：`busy` 的宣告在 Task 1 已存在，這裡是**替換**它，不是新增第二個。

- [ ] **Step 4: 執行測試，確認通過**

Run: `npm test -- --watch=false --browsers=ChromeHeadless --include='**/provider-account.component.spec.ts'`

Expected: PASS，9 個測試通過。

- [ ] **Step 5: Commit**

```bash
git add web/src/app/features/settings/provider-account.component.ts web/src/app/features/settings/provider-account.component.spec.ts
git commit -m "feat(web): add sync and unlink to the provider account panel"
```

---

## Task 4: 每個面板只鎖自己

**Files:**

- Modify: `web/src/app/features/settings/provider-account.component.spec.ts`

- [ ] **Step 1: 寫失敗的測試**

在 `provider-account.component.spec.ts` 的 `describe` 內加入：

```ts
  it('locks its own submit button while the link request is in flight', async () => {
    const link = new Subject<ExternalAccountDto>();
    const fixture = await create(steamInputs, { link: () => link });

    const button: HTMLButtonElement = fixture.nativeElement.querySelector('button[type="submit"]');
    expect(button.disabled).toBeFalse();

    submit(fixture);

    expect(button.disabled).toBeTrue();
    expect(button.textContent).toContain('綁定中');
  });

  it('re-enables its own submit button after the link request fails', async () => {
    const link = new Subject<ExternalAccountDto>();
    const fixture = await create(steamInputs, { link: () => link });

    submit(fixture);
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('button[type="submit"]');
    expect(button.disabled).toBeTrue();

    link.error(new Error('500'));
    fixture.detectChanges();

    expect(button.disabled).toBeFalse();
  });
```

同時把檔案最上方的 rxjs import 改成：

```ts
import { Subject, of } from 'rxjs';
```

- [ ] **Step 2: 執行測試，確認它失敗或通過**

Run: `npm test -- --watch=false --browsers=ChromeHeadless --include='**/provider-account.component.spec.ts'`

Expected: PASS，11 個測試通過。

這兩個測試守的是 Task 1 與 Task 3 已經寫好的行為，**預期直接通過**。
它們存在的理由是「只鎖自己」是本次的行為變更，必須有測試釘住——
沒有它們，日後有人把 `busy` 改回頁面級也不會有東西變紅。

若任一測試失敗，停下來：代表 `finalize` 沒有在錯誤路徑重置 `linking`。

- [ ] **Step 3: Commit**

```bash
git add web/src/app/features/settings/provider-account.component.spec.ts
git commit -m "test(web): pin the per-panel lock scope"
```

---

## Task 5: 接進 SettingsComponent 並遷移既有測試

**Files:**

- Modify: `web/src/app/features/settings/settings.component.ts`
- Modify: `web/src/app/features/settings/settings.component.spec.ts`

- [ ] **Step 1: 移除 settings.component.spec.ts 中已遷移的案例**

刪除 `locks the link button while the request is in flight` 這個 `it`（第 35-64 行，含其上方的
`/** 重複送出的綁定會對同一個 Steam 帳號打出多次寫入。 */` 註解）。
它呼叫的 `fixture.componentInstance.link()` 在本 Task 後不存在，
且等價案例已在 Task 4 的 `locks its own submit button while the link request is in flight` 中覆蓋。

把 `renders account, sync, and sharing terminal panels` 的斷言由 4 改成 5：

```ts
    expect(fixture.nativeElement.querySelectorAll('[data-settings-panel]').length).toBe(5);
```

面板數的組成：Steam 帳號、PSN 帳號、同步紀錄、分享連結、圖片轉移。
兩個 `app-provider-enrich` 在測試中因 `supports: () => false` 而不渲染。

- [ ] **Step 2: 執行測試，確認它失敗**

Run: `npm test -- --watch=false --browsers=ChromeHeadless --include='**/settings.component.spec.ts'`

Expected: FAIL，`renders account, sync, and sharing terminal panels` 報 `Expected 4 to be 5`。

- [ ] **Step 3: 改寫 settings.component.ts**

模板：把第 22-42 行的整個 Steam 帳號 `<section>` 換成兩個元件實例：

```html
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
```

`imports` 陣列加入 `ProviderAccountComponent`，並新增其 import 敘述：

```ts
import { ProviderAccountComponent } from './provider-account.component';
```

```ts
  imports: [FormsModule, DatePipe, ImageTransferComponent, ProviderEnrichComponent, ProviderAccountComponent],
```

類別：刪除以下成員——

- `steamAccount`、`syncing`、`linking`、`unlinking` 四個 signal
- `steamId`、`apiKey` 兩個欄位
- `link()`、`unlink()`、`sync()` 三個方法
- `reloadAccounts()` 私有方法，以及建構式中對它的呼叫
- `ExternalAccountDto` 的 import（已無使用者）
- `IngestionService` 的 `accounts` 已不再被本元件使用，但 `jobs` 仍在用，**保留 inject**

`busy` 改成只涵蓋分享連結：

```ts
  /** 分享連結的兩個動作互相排斥；各來源面板的忙碌狀態由面板自己管。 */
  readonly busy = computed(() => this.creatingShare() || this.removingShareId() !== null);
```

建構式改成：

```ts
  constructor() {
    this.reloadJobs();
    this.reloadShares();
  }
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `npm test -- --watch=false --browsers=ChromeHeadless --include='**/settings.component.spec.ts'`

Expected: PASS，4 個測試通過（原 5 個，刪掉 1 個已遷移的）。

若報 `NG0303` 或找不到 `app-provider-account`，是 `imports` 陣列漏了元件。

- [ ] **Step 5: 加上兩個面板互不干擾的測試**

單一元件的鎖範圍已在 Task 4 釘住，但「PSN 同步中 Steam 仍可操作」只有在
兩個實例並存時才驗證得到。在 `settings.component.spec.ts` 的 `describe` 內加入：

```ts
  it('does not lock one account panel while the other is working', async () => {
    const link = new Subject<unknown>();

    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        {
          provide: IngestionService,
          useValue: { accounts: () => of([]), jobs: () => of([]), link: () => link },
        },
        { provide: ShareService, useValue: { list: () => of([]) } },
        { provide: TransferService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => false } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();

    const panels = fixture.debugElement.queryAll(By.directive(ProviderAccountComponent));
    expect(panels.map((p) => p.componentInstance.provider())).toEqual(['steam', 'psn']);

    const psnForm: HTMLFormElement = panels[1].nativeElement.querySelector('form');
    psnForm.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    const steamSubmit: HTMLButtonElement =
      panels[0].nativeElement.querySelector('button[type="submit"]');
    const psnSubmit: HTMLButtonElement =
      panels[1].nativeElement.querySelector('button[type="submit"]');

    expect(psnSubmit.disabled).toBeTrue();
    expect(steamSubmit.disabled).toBeFalse();
  });
```

檔案上方的 import 需補上（`Subject` 與元件本身；`By` 與 `of` 已存在）：

```ts
import { Subject, of } from 'rxjs';
import { ProviderAccountComponent } from './provider-account.component';
```

- [ ] **Step 6: 執行測試，確認通過**

Run: `npm test -- --watch=false --browsers=ChromeHeadless --include='**/settings.component.spec.ts'`

Expected: PASS，5 個測試通過。

- [ ] **Step 7: Commit**

```bash
git add web/src/app/features/settings/settings.component.ts web/src/app/features/settings/settings.component.spec.ts
git commit -m "feat(web): wire steam and psn account panels into settings"
```

---

## Task 6: 完整驗證

**Files:**

- 僅在發現缺陷時才修改。

- [ ] **Step 1: 跑完整前端測試**

Run（在 `web/`）：`npm test -- --watch=false --browsers=ChromeHeadless`

Expected: 全綠，總數 **157**。組成：

```text
146  現況
 -1  Task 5 刪除已遷移的 'locks the link button while the request is in flight'
+11  Task 1(2) + Task 2(3) + Task 3(4) + Task 4(2)
 +1  Task 5 的 'does not lock one account panel while the other is working'
───
157
```

**若實際數字與 157 不符，先確認差異來源再繼續，不要直接接受。**

- [ ] **Step 2: 跑 production build**

Run（在 `web/`）：`npm run build`

Expected: `Application bundle generation complete.`，EXIT=0。

- [ ] **Step 3: 確認後端未被更動**

Run: `git diff --stat be6d9814..HEAD -- src/ tests/`

Expected: 只有 PSN 整合的既有變更，本次計畫**不得**在 `src/` 或 `tests/` 下新增任何差異。

- [ ] **Step 4: 實機驗收**

啟動 API 與前端，於設定頁確認：

1. 出現「Steam 帳號」與「PSN 帳號」兩塊獨立面板。
2. PSN 面板只有一個 NPSSO 欄位，沒有使用者 ID 欄位。
3. 以真實 NPSSO 綁定成功後，顯示「已綁定（更新於 …）」而**不是**「已綁定：me」。
4. 按 PSN 的「立即同步」時，Steam 面板的按鈕**仍可點擊**。
5. 同步紀錄表出現 `psn` 那一列。
6. 若 NPSSO 已過期，通知列與紀錄表的狀態欄都顯示「NPSSO 已過期，請重新取得」。

第 6 點若無法在當下重現（憑證仍有效），如實記錄為未驗證，**不要宣稱通過**。

- [ ] **Step 5: Commit（僅在有修正時）**

若前四步都沒發現缺陷，不要建立空提交。

---

## 不要做

- 不要為 NPSSO 過期加偵測、旗標或重新綁定引導——牴觸 ADR-0004。
- 不要拆分同步紀錄表格。
- 不要依 `/ingest/providers` 的能力旗標動態產生面板。
- 不要把 PSN 同步改成背景執行。
- 不要修改 `core/api/ingestion.service.ts`、`core/models.ts` 或任何後端檔案。
