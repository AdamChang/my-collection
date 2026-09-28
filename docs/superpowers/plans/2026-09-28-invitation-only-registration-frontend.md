# 邀請制註冊（前端）實作計畫

> For agentic workers: REQUIRED SUB-SKILL — 執行時使用 superpowers:test-driven-development + superpowers:subagent-driven-development。每個 Task 嚴格照 Step 1→6 執行，Step 2 不可省。

**Goal**：前端配合已完成的後端邀請制：新增 `/register` 並從 `?code=` 帶入邀請碼、登入頁移除註冊切換、`AuthService.register` 帶上 `invitationCode`、403 在頁內顯示繁中通用訊息（不顯示 toast），並補齊維運文件。

**Architecture**：`errorInterceptor` 新增 `SUPPRESS_ERROR_TOAST`（`HttpContextToken`，值是「這個錯誤要不要跳過 toast」的 predicate）。`AuthService.register(request)` 對 403 設定跳過；新的 `RegisterComponent` 捕捉 403 並顯示 `role="alert"` 訊息。`LoginComponent` 只剩登入，另有一個連到 `/register` 的連結。

**Tech Stack**：Angular 20.3（standalone、signals、template-driven forms）、Jasmine 5 + Karma（ChromeHeadless）。

- 設計文件：`docs/superpowers/specs/2026-09-28-invitation-only-registration-frontend-design.md`
- 後端設計與計畫：`docs/superpowers/specs/2026-09-28-invitation-only-registration-design.md`、`docs/superpowers/plans/2026-09-28-invitation-only-registration-backend.md`
- ADR：`docs/adr/0013-invitation-only-issued-by-maintenance-command.md`

## 執行前必讀

### 環境

- 路徑：`F:\VibeCode\MyCollection`（前端在 `web/`）
- 分支：`feat/invitation-registration`（**不是 master**；與後端同一個分支，因為前後端必須一起部署）
- 全部測試（在 `web/` 內執行，輸出一律導向 log 檔，不要接 pipe）：

  ```bash
  export CHROME_BIN="C:\Program Files\Google\Chrome\Application\chrome.exe"
  npm test -- --watch=false --browsers=ChromeHeadless > ../.tmp/karma.log 2>&1
  ```

  沒設 `CHROME_BIN` 時 karma 會無聲空轉。karma 印出 `TOTAL: N SUCCESS` 之後，程序可能不會自己結束：以 log 最後一行判斷完成，再手動收掉。根目錄 `.gitignore` 已忽略 `*.log`。
- 單檔測試：`npm test -- --watch=false --browsers=ChromeHeadless --include='**/<name>.spec.ts' > ../.tmp/karma-one.log 2>&1`
- build：`npx ng build`（在 `web/` 內）

### 基準線（2026-09-28 在 `feb6149` 親自執行）

- 前端：**281 SUCCESS**，0 failed
- `ng build`：0 warnings
- 後端（本計畫不動，僅供對照）：670 passed

任何時候數字低於基準線就是弄壞了東西。

### 絕對不要碰的檔案

- 後端 `src/**`、`tests/**`：後端已完成，本計畫只改 `web/**` 和文件。
- `web/angular.json` 裡 Angular CLI 可能自動寫入的 analytics 設定。
- 每個 Task 一律用明確路徑 `git add <path>`，**禁止 `git add .`／`git add -A`**。

### 慣例

- 註解與 commit message 用繁體中文，識別字用英文。註解寫「為什麼」。
- API 呼叫只放在 service（`AuthService` 是既有的例外位置 `core/auth.service.ts`），URL 以 `API_BASE` 組成；型別放 `core/models.ts`，與後端 record 手動同步。
- 元件用 standalone + signals；表單沿用 `LoginComponent` 的 template-driven 寫法（`FormsModule` + 一般欄位）。
- 可點擊目標在 390px 寬時至少 44px 高，沿用 `login.component.spec.ts` 的 iframe 量測寫法。
- TDD 順序：先寫失敗測試，確認它以**正確的理由**失敗，再寫最小實作。一個 Task 一個 commit。
- Karma 是整包編譯：任何一個 spec 編譯失敗，整輪都會失敗。Step 2 的「失敗」通常是編譯錯誤，要讀錯誤訊息，確認是**預期中缺少的符號**，而不是打錯字。

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `web/src/app/core/error.interceptor.spec.ts` | `errorInterceptor` 預設行為與 `SUPPRESS_ERROR_TOAST` |
| `web/src/app/features/auth/register.component.ts` | `/register` 頁：碼、顯示名稱、email、密碼；403 頁內訊息 |
| `web/src/app/features/auth/register.component.spec.ts` | 含路由註冊的測試 |

### 修改

| 檔案 | 改動 |
|---|---|
| `web/src/app/core/error.interceptor.ts` | 新增並套用 `SUPPRESS_ERROR_TOAST` |
| `web/src/app/features/auth/login.component.ts`／`.spec.ts` | 移除註冊模式；`.login__toggle` 按鈕改成 `.login__link` 連結 |
| `web/src/app/core/models.ts` | 新增 `RegisterRequest` |
| `web/src/app/core/auth.service.ts`／`.spec.ts` | `register(request: RegisterRequest)`，設定 `SUPPRESS_ERROR_TOAST` |
| `web/src/app/app.routes.ts` | 新增 `register` 路由（在 `**` 之前） |
| `AGENTS.md` | 前端慣例補上 `SUPPRESS_ERROR_TOAST` 例外 |
| `docs/architecture/16-module-web.md` | 路由表：`/login` 備註、新增 `/register`；interceptor 段落補上例外 |
| `docs/architecture/90-tech-debt.md` | I2 標為完全解決 |
| `docs/deployment/production-operations.md` | 新增「發邀請」一節 |
| `docs/superpowers/plans/2026-09-28-invitation-only-registration-backend.md` | 開頭「前端計畫：尚未撰寫」改成連到本計畫 |

### Task 相依順序

```
Task 0（commit 設計文件與計畫）
  │
Task 1 SUPPRESS_ERROR_TOAST ──────┐
  │                               │
Task 2 登入頁移除註冊模式          │     ← 1、2 互相獨立，可並行
  │                               │
  └──────────┬────────────────────┘
          Task 3 RegisterRequest + AuthService.register（依賴 1；依賴 2 是因為會移除舊的 3 參數呼叫端）
             │
          Task 4 RegisterComponent + 路由
             │
          Task 5 文件

Checkpoint A：Task 2 之後（UI 暫時沒有註冊入口，但測試全綠。此時註冊本來就已經壞了：後端要求邀請碼）
Checkpoint B：Task 4 之後（功能完成，可以部署）
Checkpoint C：Task 5 之後（完成）
```

## Task 0：commit 設計文件與計畫

**Files:** Create: `docs/superpowers/specs/2026-09-28-invitation-only-registration-frontend-design.md`、`docs/superpowers/plans/2026-09-28-invitation-only-registration-frontend.md`；Modify: `docs/superpowers/plans/2026-09-28-invitation-only-registration-backend.md`

- [ ] 確認目前在 `feat/invitation-registration`，`git status` 只有這三個檔案。
- [ ] 後端計畫開頭的 `- 前端計畫：尚未撰寫（見「後續」）` 改成 `- 前端計畫：`docs/superpowers/plans/2026-09-28-invitation-only-registration-frontend.md``。
- [ ] Commit：

```
git add docs/superpowers/specs/2026-09-28-invitation-only-registration-frontend-design.md docs/superpowers/plans/2026-09-28-invitation-only-registration-frontend.md docs/superpowers/plans/2026-09-28-invitation-only-registration-backend.md
```

```
docs(plans): 新增邀請制註冊的前端設計與實作計畫

403 改由註冊頁內顯示繁中訊息，errorInterceptor 以 HttpContextToken 按狀態碼跳過 toast。
登入頁保留「有邀請碼？」連結。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Task 1：`SUPPRESS_ERROR_TOAST`

**Files:** Create: `web/src/app/core/error.interceptor.spec.ts`；Modify: `web/src/app/core/error.interceptor.ts`

`errorInterceptor` 目前沒有任何測試，所以第一條測試是**特性測試**：鎖住「預設會 toast ProblemDetails 的 detail」，避免加上例外機制時不小心改到預設行為。第二、三條才是新行為。容易錯的地方是：predicate 只能決定要不要 toast，錯誤**一定要照樣往外拋**，否則呼叫端的 `await` 會以為成功。

- [ ] **Step 1：寫失敗測試** `web/src/app/core/error.interceptor.spec.ts`

```ts
import { TestBed } from '@angular/core/testing';
import {
  HttpClient,
  HttpContext,
  HttpErrorResponse,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { NotificationService } from './notification.service';
import { SUPPRESS_ERROR_TOAST, errorInterceptor } from './error.interceptor';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let notifications: jasmine.SpyObj<NotificationService>;

  beforeEach(() => {
    notifications = jasmine.createSpyObj<NotificationService>('NotificationService', ['error']);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: NotificationService, useValue: notifications },
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  /** 送出請求並以指定狀態碼失敗，回傳被拋出的錯誤。firstValueFrom 會立即訂閱，所以 expectOne 找得到請求。 */
  function failWith(status: number, context = new HttpContext()): Promise<unknown> {
    const outcome = firstValueFrom(http.post('/api/auth/register', {}, { context })).then(
      () => fail('expected the request to fail'),
      (error: unknown) => error,
    );
    controller
      .expectOne('/api/auth/register')
      .flush({ detail: 'problem detail' }, { status, statusText: 'Error' });
    return outcome;
  }

  const suppress403 = () =>
    new HttpContext().set(SUPPRESS_ERROR_TOAST, (error) => error.status === 403);

  it('toasts the ProblemDetails detail by default', async () => {
    await failWith(403);

    expect(notifications.error).toHaveBeenCalledOnceWith('problem detail');
  });

  it('skips the toast but still rethrows when the request suppresses that status', async () => {
    const error = await failWith(403, suppress403());

    expect(notifications.error).not.toHaveBeenCalled();
    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect((error as HttpErrorResponse).status).toBe(403);
  });

  it('still toasts statuses the predicate does not claim', async () => {
    await failWith(400, suppress403());

    expect(notifications.error).toHaveBeenCalledOnceWith('problem detail');
  });
});
```

- [ ] **Step 2：跑測試確認失敗**：預期整輪編譯失敗，錯誤是 `Module '"./error.interceptor"' has no exported member 'SUPPRESS_ERROR_TOAST'`。如果是別的錯誤（例如 import 路徑打錯），先修正測試。
- [ ] **Step 3：最小實作** `error.interceptor.ts`

```diff
-import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
+import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
@@
+/**
+ * 讓單一請求宣告「這類錯誤由頁面自己說明」，跳過 toast。錯誤仍會往外拋。
+ * 用 predicate 而不是 boolean：頁面通常只想接手特定狀態碼（例如註冊的 403），
+ * 其餘錯誤（400、409、連線失敗）仍該由這裡統一顯示。
+ */
+export const SUPPRESS_ERROR_TOAST = new HttpContextToken<(error: HttpErrorResponse) => boolean>(
+  () => false,
+);
+
 export const errorInterceptor: HttpInterceptorFn = (request, next) => {
   const notifications = inject(NotificationService);
   const isRefresh = request.url.includes('/auth/refresh');
+  const isSuppressed = request.context.get(SUPPRESS_ERROR_TOAST);
 
   return next(request).pipe(
     catchError((error: unknown) => {
-      if (error instanceof HttpErrorResponse && error.status !== 401 && !isRefresh) {
+      if (
+        error instanceof HttpErrorResponse &&
+        error.status !== 401 &&
+        !isRefresh &&
+        !isSuppressed(error)
+      ) {
         notifications.error(describe(error));
       }
```

- [ ] **Step 4：跑單檔測試**（`--include='**/error.interceptor.spec.ts'`）：Expected `TOTAL: 3 SUCCESS`
- [ ] **Step 5：跑全部測試**：Expected `TOTAL: 284 SUCCESS`
- [ ] **Step 6：Commit**

```
git add web/src/app/core/error.interceptor.ts web/src/app/core/error.interceptor.spec.ts
```

```
feat(web): errorInterceptor 支援以 SUPPRESS_ERROR_TOAST 按狀態碼跳過 toast

讓頁面接手特定錯誤的說明（下一步是註冊的 403），其餘錯誤仍統一由 toast 顯示，錯誤照樣往外拋。
同時補上 errorInterceptor 的第一批測試。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Task 2：登入頁移除註冊模式

**Files:** Modify: `web/src/app/features/auth/login.component.ts`、`web/src/app/features/auth/login.component.spec.ts`

後端已經要求邀請碼，登入頁的註冊模式現在只會拿到 400，留著只會誤導。這個 Task 先把它拿掉，並把切換按鈕換成指向 `/register` 的連結；`/register` 本身在 Task 4 才出現，這段期間連結會被 `**` 導回首頁，而未登入時首頁會再被 guard 導回登入頁，不會壞。

容易錯的地方：44px 測試靠 CSS class 找目標，class 從 `.login__toggle` 改成 `.login__link` 時，樣式和測試選擇器要一起改；`<a>` 預設是 inline，必須保留 `display: inline-flex` 與 `min-block-size`，否則高度會掉到 44px 以下。

- [ ] **Step 1：寫失敗測試** `login.component.spec.ts`

第一條測試整條換掉（`AuthService` 的 stub 也拿掉 `register`，確認登入頁不再依賴它）：

```ts
  it('renders the login form only and links to registration', async () => {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } },
        { provide: AuthService, useValue: { login: () => Promise.resolve() } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('.login__terminal')).toBeTruthy();
    expect(element.querySelector('input[name="displayName"]')).toBeNull();
    expect(element.querySelector('a.login__link')?.getAttribute('href')).toBe('/register');
  });
```

第二條測試（44px）只改兩處：`AuthService` stub 改成 `{ login: () => Promise.resolve() }`，選擇器改成：

```diff
       const targets = frameDocument.querySelectorAll<HTMLElement>(
-        'button[type="submit"], .login__toggle',
+        'button[type="submit"], .login__link',
       );
```

- [ ] **Step 2：跑測試確認失敗**：這次會編譯成功，失敗在斷言：第一條 `Expected null to be '/register'`（連結還不存在），第二條 `Expected 1 to be 2`（只找到 submit 按鈕）。
- [ ] **Step 3：最小實作** `login.component.ts`

```diff
 import { Component, inject, signal } from '@angular/core';
 import { FormsModule } from '@angular/forms';
-import { ActivatedRoute, Router } from '@angular/router';
+import { ActivatedRoute, Router, RouterLink } from '@angular/router';
 import { AuthService } from '../../core/auth.service';
 
 @Component({
   selector: 'app-login',
-  imports: [FormsModule],
+  imports: [FormsModule, RouterLink],
@@
         <form (ngSubmit)="submit()">
-          @if (mode() === 'register') {
-            <label>顯示名稱<input name="displayName" [(ngModel)]="displayName" required /></label>
-          }
           <label>Email<input name="email" type="email" [(ngModel)]="email" required /></label>
           <label>密碼<input name="password" type="password" [(ngModel)]="password" required minlength="8" /></label>
 
           <button type="submit" [disabled]="busy()">
-            {{ busy() ? '連線中…' : mode() === 'login' ? '登入系統' : '建立帳號' }}
+            {{ busy() ? '連線中…' : '登入系統' }}
           </button>
         </form>
 
-        <button type="button" class="login__toggle" (click)="toggle()">
-          {{ mode() === 'login' ? '還沒有帳號？註冊' : '已經有帳號？登入' }}
-        </button>
+        <!-- 註冊採邀請制（ADR-0013），被邀請者可能只拿到碼、沒拿到連結，所以保留入口 -->
+        <a class="login__link" routerLink="/register">有邀請碼？前往註冊</a>
@@
-    .login__toggle { display: inline-flex; align-items: center; justify-self: start;
-      min-block-size: 44px; border: 0; padding: 0; background: none; color: var(--mc-cyan); }
+    .login__link { display: inline-flex; align-items: center; justify-self: start;
+      min-block-size: 44px; color: var(--mc-cyan); }
@@
-  readonly mode = signal<'login' | 'register'>('login');
   readonly busy = signal(false);
 
   email = '';
   password = '';
-  displayName = '';
-
-  toggle(): void {
-    this.mode.update((m) => (m === 'login' ? 'register' : 'login'));
-  }
 
   async submit(): Promise<void> {
     this.busy.set(true);
 
     try {
-      if (this.mode() === 'login') {
-        await this.auth.login(this.email, this.password);
-      } else {
-        await this.auth.register(this.email, this.password, this.displayName);
-      }
+      await this.auth.login(this.email, this.password);
```

- [ ] **Step 4：跑單檔測試**（`--include='**/login.component.spec.ts'`）：Expected `TOTAL: 2 SUCCESS`
- [ ] **Step 5：跑全部測試**：Expected `TOTAL: 284 SUCCESS`（測試數不變，第一條是改寫）
- [ ] **Step 6：Commit**

```
git add web/src/app/features/auth/login.component.ts web/src/app/features/auth/login.component.spec.ts
```

```
feat(web): 登入頁移除註冊模式，改為「有邀請碼？」連結

後端已要求邀請碼，登入頁的註冊模式只會拿到 400。註冊移到獨立的 /register 頁（下一步新增）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

**— Checkpoint A：測試全綠，可以在這裡收工（UI 暫時沒有註冊入口）—**

## Task 3：`RegisterRequest` 與 `AuthService.register`

**Files:** Modify: `web/src/app/core/models.ts`、`web/src/app/core/auth.service.ts`、`web/src/app/core/auth.service.spec.ts`

`register` 改成接收物件：四個都是 `string` 的位置參數，傳錯順序編譯器抓不到，而 `email` 和 `invitationCode` 對調就會變成一個永遠回 403 的 bug，還很難從通用訊息看出來。這個 Task 也是 `SUPPRESS_ERROR_TOAST` 第一次被使用的地方；測試要直接檢查請求上的 predicate，因為 `HttpTestingController` 不會經過 interceptor。

- [ ] **Step 1：寫失敗測試** `auth.service.spec.ts`

import 調整：

```diff
-import { provideHttpClient } from '@angular/common/http';
+import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
 import { Router, provideRouter } from '@angular/router';
 import { AuthService } from './auth.service';
-import { AuthResponse } from './models';
+import { SUPPRESS_ERROR_TOAST } from './error.interceptor';
+import { AuthResponse, RegisterRequest } from './models';
```

在 `'stores tokens and user after login'` 之後新增：

```ts
  it('registers with the invitation code and leaves 403 to the caller', async () => {
    const request: RegisterRequest = {
      email: 'a@b.c',
      password: 'P@ssw0rd!',
      displayName: 'Adam',
      invitationCode: 'abcde-fghjk-mnpqr-stvwx',
    };

    const promise = service.register(request);
    const call = http.expectOne('/api/auth/register');

    // 碼原樣送出：正規化是後端的責任
    expect(call.request.body).toEqual(request);
    const isSuppressed = call.request.context.get(SUPPRESS_ERROR_TOAST);
    expect(isSuppressed(new HttpErrorResponse({ status: 403 }))).toBeTrue();
    expect(isSuppressed(new HttpErrorResponse({ status: 400 }))).toBeFalse();

    call.flush(response);
    await promise;
    expect(service.isAuthenticated()).toBeTrue();
  });
```

- [ ] **Step 2：跑測試確認失敗**：預期編譯失敗，`Module '"./models"' has no exported member 'RegisterRequest'`，以及 `Expected 3 arguments, but got 1`。
- [ ] **Step 3：最小實作**

`models.ts`，接在 `AuthResponse` 之後：

```ts
/** 對應後端 `RegisterCommand`。invitationCode 原樣送出，大小寫、連字號與易混淆字元由後端正規化。 */
export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
  invitationCode: string;
}
```

`auth.service.ts`：

```diff
-import { HttpClient } from '@angular/common/http';
+import { HttpClient, HttpContext } from '@angular/common/http';
@@
 import { API_BASE } from './api-base';
-import { AuthResponse, UserDto } from './models';
+import { SUPPRESS_ERROR_TOAST } from './error.interceptor';
+import { AuthResponse, RegisterRequest, UserDto } from './models';
@@
-  async register(email: string, password: string, displayName: string): Promise<void> {
+  /**
+   * 403 代表邀請無效（後端不區分原因）。這裡關掉它的 toast，由呼叫端在頁內說明；
+   * 其他錯誤仍由 errorInterceptor 顯示。
+   */
+  async register(request: RegisterRequest): Promise<void> {
     const response = await firstValueFrom(
-      this.http.post<AuthResponse>(`${API_BASE}/auth/register`, { email, password, displayName }),
+      this.http.post<AuthResponse>(`${API_BASE}/auth/register`, request, {
+        context: new HttpContext().set(SUPPRESS_ERROR_TOAST, (error) => error.status === 403),
+      }),
     );
     this.store(response);
   }
```

- [ ] **Step 4：跑單檔測試**（`--include='**/auth.service.spec.ts'`）：Expected 比原本多 1 筆，全部 SUCCESS
- [ ] **Step 5：跑全部測試**：Expected `TOTAL: 285 SUCCESS`
- [ ] **Step 6：Commit**

```
git add web/src/app/core/models.ts web/src/app/core/auth.service.ts web/src/app/core/auth.service.spec.ts
```

```
feat(web): AuthService.register 帶上邀請碼，403 交由呼叫端顯示

改用 RegisterRequest 物件參數，避免四個字串參數傳錯順序（email 與碼對調會變成永遠 403）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Task 4：`RegisterComponent` 與 `/register` 路由

**Files:** Create: `web/src/app/features/auth/register.component.ts`、`web/src/app/features/auth/register.component.spec.ts`；Modify: `web/src/app/app.routes.ts`

這是使用者真正會看到的部分。容易錯的地方有三個：

1. `?code=` 只讀一次當作欄位初值，欄位要可以編輯（只拿到碼的人要手動輸入）。
2. 成功後固定導向 `/`，**不讀 `returnUrl`**：新帳號沒有「登入前想去的頁面」，而且這樣註冊連結不能被做成開放轉址。測試刻意帶一個 `returnUrl` 來證明它被忽略。
3. 只有 403 顯示頁內訊息；其他錯誤已經有 toast，再顯示頁內訊息就重複了。重新送出時要先清掉上一次的訊息。

路由必須放在 `{ path: '**' }` 之前，否則 `/register` 會被導回首頁。測試直接檢查 `routes` 陣列。

- [ ] **Step 1：寫失敗測試** `register.component.spec.ts`

```ts
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { routes } from '../../app.routes';
import { AuthService } from '../../core/auth.service';
import { RegisterComponent } from './register.component';

describe('RegisterComponent', () => {
  let register: jasmine.Spy;

  async function setup(query: Record<string, string> = {}) {
    register = jasmine.createSpy('register').and.resolveTo();
    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
        { provide: AuthService, useValue: { register } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RegisterComponent);
    fixture.detectChanges();
    return fixture;
  }

  function fillForm(component: RegisterComponent): void {
    component.invitationCode = 'abcde-fghjk-mnpqr-stvwx';
    component.displayName = 'Adam';
    component.email = 'adam@example.com';
    component.password = 'P@ssw0rd!';
  }

  function alertOf(element: HTMLElement): HTMLElement | null {
    return element.querySelector('[role="alert"]');
  }

  it('prefills the invitation code from ?code=', async () => {
    const fixture = await setup({ code: 'ABCDE-FGHJK-MNPQR-STVWX' });

    expect(fixture.componentInstance.invitationCode).toBe('ABCDE-FGHJK-MNPQR-STVWX');
  });

  it('registers with the form values and goes to the showcase, ignoring returnUrl', async () => {
    const fixture = await setup({ returnUrl: '/catalog' });
    const navigate = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    fillForm(fixture.componentInstance);

    await fixture.componentInstance.submit();

    expect(register).toHaveBeenCalledOnceWith({
      email: 'adam@example.com',
      password: 'P@ssw0rd!',
      displayName: 'Adam',
      invitationCode: 'abcde-fghjk-mnpqr-stvwx',
    });
    expect(navigate).toHaveBeenCalledOnceWith('/');
  });

  it('explains a rejected invitation inline', async () => {
    const fixture = await setup();
    const navigate = spyOn(TestBed.inject(Router), 'navigateByUrl');
    register.and.rejectWith(new HttpErrorResponse({ status: 403 }));
    fillForm(fixture.componentInstance);

    await fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(alertOf(fixture.nativeElement)?.textContent).toContain('邀請碼無效或已失效');
    expect(navigate).not.toHaveBeenCalled();
    expect(fixture.componentInstance.busy()).toBeFalse();
  });

  it('leaves other failures to the error toast', async () => {
    const fixture = await setup();
    register.and.rejectWith(new HttpErrorResponse({ status: 400 }));
    fillForm(fixture.componentInstance);

    await fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(alertOf(fixture.nativeElement)).toBeNull();
  });

  it('clears the rejection when submitting again', async () => {
    const fixture = await setup();
    spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    register.and.rejectWith(new HttpErrorResponse({ status: 403 }));
    fillForm(fixture.componentInstance);
    await fixture.componentInstance.submit();

    register.and.resolveTo();
    await fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(alertOf(fixture.nativeElement)).toBeNull();
  });

  it('keeps register actions at least 44px tall at 390px', async () => {
    const fixture = await setup();

    const frame = document.createElement('iframe');
    frame.style.width = '390px';
    frame.style.height = '844px';
    frame.style.border = '0';
    document.body.append(frame);

    try {
      const frameDocument = frame.contentDocument!;
      const styles = frameDocument.createElement('style');
      styles.textContent = Array.from(document.styleSheets)
        .flatMap((sheet) => Array.from(sheet.cssRules))
        .map((rule) => rule.cssText)
        .join('\n');
      frameDocument.head.append(styles);
      frameDocument.body.append(fixture.nativeElement.cloneNode(true));

      const targets = frameDocument.querySelectorAll<HTMLElement>(
        'button[type="submit"], .register__link',
      );
      expect(targets.length).toBe(2);
      targets.forEach((target) => {
        expect(parseFloat(frame.contentWindow!.getComputedStyle(target).height))
          .withContext(target.textContent?.trim() ?? target.tagName)
          .toBeGreaterThanOrEqual(44);
      });
    } finally {
      frame.remove();
    }
  });

  it('is routed at /register before the catch-all', async () => {
    const route = routes.find((r) => r.path === 'register');

    expect(route).toBeDefined();
    expect(await route!.loadComponent!()).toBe(RegisterComponent);
    expect(routes.indexOf(route!)).toBeLessThan(routes.findIndex((r) => r.path === '**'));
  });
});
```

- [ ] **Step 2：跑測試確認失敗**：預期編譯失敗，`Cannot find module './register.component'`。
- [ ] **Step 3：最小實作**

`web/src/app/features/auth/register.component.ts`：

```ts
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-register',
  imports: [FormsModule, RouterLink],
  template: `
    <main class="register">
      <section class="register__terminal mc-panel">
        <div class="mc-eyebrow">PRIVATE ARCHIVE / INVITATION</div>
        <h1>MY//COLLECTION</h1>
        <p class="mc-muted">註冊需要邀請碼。Email 必須與收到邀請的信箱相同。</p>
        <form (ngSubmit)="submit()">
          <label>邀請碼<input name="invitationCode" [(ngModel)]="invitationCode" required maxlength="64"
            autocomplete="off" autocapitalize="characters" spellcheck="false" /></label>
          <label>顯示名稱<input name="displayName" [(ngModel)]="displayName" required maxlength="64" /></label>
          <label>Email<input name="email" type="email" [(ngModel)]="email" required autocomplete="email" /></label>
          <label>密碼<input name="password" type="password" [(ngModel)]="password" required minlength="8"
            autocomplete="new-password" /></label>

          @if (rejected()) {
            <p class="register__alert" role="alert">
              邀請碼無效或已失效。請確認 email 與收到邀請的信箱相同；邀請只能使用一次，且有使用期限。
              仍無法註冊時，請向邀請你的人索取新的邀請。
            </p>
          }

          <button type="submit" [disabled]="busy()">{{ busy() ? '連線中…' : '建立帳號' }}</button>
        </form>

        <a class="register__link" routerLink="/login">已經有帳號？登入</a>
      </section>
    </main>
  `,
  // 與 LoginComponent 相似但刻意不抽共用：目前只有兩處，出現第三個類似頁面再抽
  styles: `
    .register { min-height: calc(100vh - 8rem); display: grid; place-items: center; padding: 2rem 1rem; }
    .register__terminal { width: min(100%, 26rem); display: grid; gap: 1rem; }
    .register h1 { margin: 0; letter-spacing: 0.08em; }
    .register p { margin: 0; }
    .register form { display: grid; gap: 0.75rem; }
    .register label { display: grid; gap: 0.35rem; color: var(--mc-text-muted); }
    .register__alert { color: var(--mc-magenta); }
    .register__link { display: inline-flex; align-items: center; justify-self: start;
      min-block-size: 44px; color: var(--mc-cyan); }
  `,
})
export class RegisterComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly busy = signal(false);
  /** 後端對所有無效原因回同一個 403（ADR-0013），這裡也只給通用說明。 */
  readonly rejected = signal(false);

  /** 維運指令產生的連結會帶 ?code=。只當初值，之後以欄位為準（見設計文件決策二）。 */
  invitationCode = inject(ActivatedRoute).snapshot.queryParamMap.get('code') ?? '';
  displayName = '';
  email = '';
  password = '';

  async submit(): Promise<void> {
    this.busy.set(true);
    this.rejected.set(false);

    try {
      await this.auth.register({
        email: this.email,
        password: this.password,
        displayName: this.displayName,
        invitationCode: this.invitationCode,
      });

      // 不讀 returnUrl：新帳號沒有「登入前想去的頁面」，也避免註冊連結被做成開放轉址
      await this.router.navigateByUrl('/');
    } catch (error) {
      // 403 的 toast 已由 AuthService 關掉，改在頁內說明；其他錯誤 errorInterceptor 已經顯示
      if (error instanceof HttpErrorResponse && error.status === 403) {
        this.rejected.set(true);
      }
    } finally {
      this.busy.set(false);
    }
  }
}
```

`app.routes.ts`，接在 `login` 之後：

```diff
   {
     path: 'login',
     loadComponent: () => import('./features/auth/login.component').then((m) => m.LoginComponent),
   },
+  {
+    path: 'register',
+    loadComponent: () =>
+      import('./features/auth/register.component').then((m) => m.RegisterComponent),
+  },
```

- [ ] **Step 4：跑單檔測試**（`--include='**/register.component.spec.ts'`）：Expected `TOTAL: 7 SUCCESS`
- [ ] **Step 5：跑全部測試**：Expected `TOTAL: 292 SUCCESS`；另跑 `npx ng build`：0 warnings（確認懶載入 chunk 正常產生）
- [ ] **Step 6：Commit**

```
git add web/src/app/features/auth/register.component.ts web/src/app/features/auth/register.component.spec.ts web/src/app/app.routes.ts
```

```
feat(web): 新增 /register 頁，從 ?code= 帶入邀請碼（I2）

註冊成功固定導向首頁、不讀 returnUrl；403 在頁內顯示通用的繁中說明，其他錯誤沿用 toast。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

**— Checkpoint B：前端功能完成，測試全綠，前後端可以一起部署 —**

## Task 5：文件

**Files:** Modify: `AGENTS.md`、`docs/architecture/16-module-web.md`、`docs/architecture/90-tech-debt.md`、`docs/deployment/production-operations.md`

沒有測試，但「發邀請」是部署後第一件要做的事，而且要連正式資料庫，步驟寫錯的代價比程式碼錯還高。寫完後照著文件在**開發資料庫**上走一次（見「手動驗證」第 1 條）。

- [ ] `AGENTS.md` 前端慣例：

```diff
-- 錯誤由 `errorInterceptor` 顯示 toast；元件的 error 回呼使用 `IGNORE_HANDLED_BY_INTERCEPTOR`。
+- 錯誤由 `errorInterceptor` 顯示 toast；元件的 error 回呼使用 `IGNORE_HANDLED_BY_INTERCEPTOR`。頁面要自行說明某個狀態碼時，由發請求的 service 在 `HttpContext` 設定 `SUPPRESS_ERROR_TOAST`（predicate，只接手需要的狀態碼），不要在元件裡另外顯示卻沒有關掉 toast。目前只有註冊的 403 這樣做。
```

- [ ] `16-module-web.md` 路由表：`/login` 的備註改成「只有登入；有指向 `/register` 的連結。成功後 `navigateByUrl(returnUrl ?? '/')`」；在其下新增一列 `| /register | RegisterComponent | — | 邀請制（ADR-0013）；?code= 帶入欄位初值；成功後固定導向 /，不讀 returnUrl；403 頁內顯示通用訊息 |`。interceptor 段落（「依序為 `loadingInterceptor` → …」那段）之後補一句：`errorInterceptor` 會先問請求上的 `SUPPRESS_ERROR_TOAST`，predicate 回 `true` 就不顯示 toast，但錯誤照樣往外拋。
- [ ] `90-tech-debt.md` I2 列：「【已解決（後端）2026-09-28，`d9b0a7a`；前端待前端計畫】」改成「【已解決 2026-09-28，後端 `d9b0a7a`、前端 `<Task 4 SHA>`】」；建議方向欄刪掉「**未處理**：…」那句，改成「前端新增 `/register`，登入頁移除註冊模式」。第 40 行「（後端已解決）」改成「（已解決）」。
- [ ] `production-operations.md`：在「## 例行維運」之後新增一節：

````markdown
## 發邀請

註冊採邀請制（ADR-0013），邀請只能在維運者本機用 `maintenance invite` 發出，Web 與 API 都沒有入口。

連線資訊只放在**當前 PowerShell session 的環境變數**，不要寫進 user secrets：user secrets 是本機開發共用的，寫進去之後下一次 `dotnet run` 就會連到正式環境。環境變數會覆蓋 user secrets，關掉視窗就消失。

```powershell
$env:Mongo__ConnectionString = gcloud secrets versions access latest --secret=mongo-connection-string --project=mycollection-504914
$env:Mongo__Database = 'mycollection'
$env:Cors__AllowedOrigins__0 = 'https://mycollection-web-cswrakuenq-de.a.run.app'   # 註冊連結的 origin

dotnet run --project src/MyCollection.Api -- maintenance invite create --email <對方的 email> [--days <n>]
dotnet run --project src/MyCollection.Api -- maintenance invite list
dotnet run --project src/MyCollection.Api -- maintenance invite revoke --email <email>

Remove-Item Env:Mongo__ConnectionString, Env:Mongo__Database, Env:Cors__AllowedOrigins__0
```

- 不要 `echo` 或把 `$env:Mongo__ConnectionString` 貼進任何工具輸出；外洩時依 runbook〈Credential exposure in tool output〉輪替。
- `create` 輸出碼與連結，把**連結**交給對方；對方註冊時的 email 必須與 `--email` 相同。對同一個 email 再發一次，會自動撤銷舊的未使用邀請。
- 碼存明文，`list` 可以重看，不必因為碼遺失而撤銷重發。
- exit code：`0` 成功、`1` 業務錯誤（例如 email 已註冊，訊息在 stderr）、`2` 參數錯誤（會印出用法）。
- 指令啟動時會執行 `EnsureIndexesAsync`（冪等），不會改動資料。
````

  寫入前先核對：`Mongo__Database`、`Cors__AllowedOrigins__0` 與 `infra/terraform/runtime/services.tf` 設定給 Cloud Run 的名稱和值一致；secret 名稱與本文件〈Secret Manager〉一節一致；exit code 與 `src/MyCollection.Api/Maintenance/InviteMaintenance.cs` 一致。任何一項不符，以程式碼和 Terraform 為準修正本段。
- [ ] Commit：

```
git add AGENTS.md docs/architecture/16-module-web.md docs/architecture/90-tech-debt.md docs/deployment/production-operations.md
```

```
docs: 標記 I2 完全解決，補上 SUPPRESS_ERROR_TOAST 慣例與發邀請的維運步驟

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## 完成後的驗證

- [ ] 前端 `npm test -- --watch=false --browsers=ChromeHeadless`：**292 SUCCESS**，0 failed
- [ ] `npx ng build`：0 warnings
- [ ] 後端 `dotnet test MyCollection.slnx`：仍是 670 passed（本計畫不應影響後端）
- [ ] `git status`：乾淨
- [ ] `git log feb6149..HEAD --oneline`：6 顆 commit（Task 0～5）
- [ ] `git diff feb6149..HEAD` 沒有連線字串、金鑰、個人 email（`production-operations.md` 的範例只能有 `<對方的 email>` 這類 placeholder）
- [ ] `grep -rn "register(" web/src/app --include=*.ts`：除了 spec，只剩 `auth.service.ts` 的定義和 `register.component.ts` 的呼叫

## 手動驗證

需要本機 Mongo（開發資料庫）、`dotnet run --project src/MyCollection.Api` 與 `cd web && npm start`。

1. 依 `production-operations.md`〈發邀請〉的指令，但**不設定環境變數**（沿用本機 user secrets 的開發資料庫），`create --email me@example.test`：拿到碼與連結。連結的 origin 應該是本機 `Cors:AllowedOrigins:0`；如果它不是 `http://localhost:4200`，手動把 origin 換掉。
2. 開啟連結：邀請碼欄位已帶入。顯示名稱隨意、email 輸入 `ME@Example.test`（大寫）、密碼 8 碼以上 → 導向首頁，已登入。
3. 登出，再開一次同一個連結並送出相同內容：頁內出現繁中的「邀請碼無效或已失效…」，**沒有** toast；DevTools Network 看到 403。
4. 重新發一份邀請，開啟連結後把 email 改成別的 → 同樣是頁內訊息、沒有 toast。
5. 開 `/register`（不帶 `?code=`），手動輸入第 4 步的碼，但全部小寫並把 `-` 換成空白、email 正確 → 註冊成功。
6. 開 `/register?returnUrl=/catalog` 完成註冊 → 仍導向 `/`，不是 `/catalog`。
7. 停掉 API 後送出註冊表單 → 出現「無法連線到伺服器。」toast，頁內沒有邀請訊息。
8. `/login`：沒有顯示名稱欄位、沒有切換按鈕；「有邀請碼？前往註冊」導向 `/register`。用 DevTools 的 390px 寬度確認兩頁的按鈕與連結都容易點擊。

## 後續（不在本計畫內）

- 部署：前後端在同一個分支，由使用者開 PR、合併並走 canary 部署。部署完成後，照 `production-operations.md`〈發邀請〉發出正式環境的第一份邀請。
- 已登入的使用者開啟 `/register` 時的處理（會覆蓋目前的 session），與 W1 一起看。
- I1 速率限制。
- 其他頁面的錯誤改成頁內顯示：需要時沿用 `SUPPRESS_ERROR_TOAST`，逐案決定。

## 事實查核（2026-09-28，執行前，唯讀）

| 引用 | 存在？ | 證據 | 修正 |
|---|---|---|---|
| `HttpContextToken`、`HttpContext.set<T>/get<T>` | ✅ | `@angular/common` 20.3.30 `module.d.d.ts:17,66,74` | — |
| `convertToParamMap` | ✅ | `@angular/router` `router_module.d.d.ts:71` | — |
| RouterLink 搭配假的 `ActivatedRoute`（只有 `snapshot.queryParamMap`）時的絕對路徑 | ✅ | `router2.mjs:5812-5831`：`createUrlTree` 取 `relativeTo.snapshot` 失敗會 fallback 到 `currentUrlTree.root`，絕對路徑的指令保留 | — |
| Jasmine `resolveTo`／`rejectWith`／`toHaveBeenCalledOnceWith`／`fail` | ✅ | `@types/jasmine` 5.1.15 `index.d.ts:150,984,1446,1448` | — |
| `ng test --include` | ✅ | `@angular/build:karma` schema 有 `include`；前一份前端計畫用過 `--include='**/x.spec.ts'` | 單檔指令改成同樣寫法 |
| log 檔位置 | ✅ | 根目錄 `.gitignore:112` 忽略 `*.log` | 改用前例的 `../.tmp/karma.log` |
| `NotificationService.error`、`providedIn: 'root'` | ✅ | `notification.service.ts:9,15` | — |
| CSS 變數 `--mc-magenta`、`--mc-cyan`、`--mc-text-muted` | ✅ | `styles` 內定義；`login.component.ts` 已使用後兩者 | — |
| 後端 `RegisterCommand(Email, Password, DisplayName, InvitationCode)`；DisplayName／InvitationCode 上限 64、Password 最少 8 | ✅ | `RegisterCommand.cs:9,17-19` | — |
| 403 detail 文字 | ✅ | `MongoInvitationRedeemer.cs:17` | — |
| `maintenance invite` exit code 0／1／2 | ✅ | `InviteMaintenance.cs:41-72` | — |
| Cloud Run 環境變數 `Mongo__Database`、`Cors__AllowedOrigins__0` | ✅ | `infra/terraform/runtime/services.tf:98,123`（origin 值是 `var.web_public_url`，Task 5 要求再核對） | — |
| Secret 名稱 `mongo-connection-string`、DB 名 `mycollection` | ✅ | `production-operations.md` 〈環境座標〉〈Secret Manager〉 | — |
| Create：`error.interceptor.spec.ts`、`register.component.ts`、`register.component.spec.ts` | ✅ 尚未存在 | `ls` 找不到 | — |
| Modify：`login.component.ts` 的 `mode`／`toggle()`／`.login__toggle`、`auth.service.ts` 的 3 參數 `register`、`auth.service.spec.ts` 第 3 行的 import、`app.routes.ts` 的 `**` | ✅ 與描述相符 | 直接讀檔 | — |
| `16-module-web.md` 路由表與 interceptor 段落、`90-tech-debt.md` 第 40、56 行、`AGENTS.md` 第 79 行 | ✅ | `grep` | — |
| 測試碼的競態 | ✅ | `failWith` 在 `flush` 前已用 `firstValueFrom(...).then(ok, err)` 訂閱並掛好拒絕處理；元件測試直接 `await submit()`，不依賴表單事件時序 | — |
| 空轉風險（vacuous pass） | ✅ | 「清掉訊息」「其他錯誤不顯示」兩條在錯誤實作下會失敗；`errorInterceptor` 第一條是刻意的特性測試，已在 Task 1 說明 | — |
