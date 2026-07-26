# Plan 5：Angular 前端 + 部署實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **前置：** Plan 1–4 已完成並全綠。後端 API 已可運作。

**Goal:** 建立 Angular 20 standalone + signals 前端（Showcase 牆、完整庫存、品項編輯、品類 schema 編輯器、設定頁、公開分享頁），核心是 schema 驅動的 `DynamicFormComponent`，並以 `docker compose up` 一鍵啟動 api + web + mongo。

**Architecture:** 全 standalone components，狀態用 signals（不引入 NgRx）。`core/` 放 JWT interceptor、錯誤處理、auth guard；`shared/DynamicFormComponent` 吃 `CategoryField[]` 產出 Reactive Form，是整個前端唯一需要仔細設計的元件。開發時 `ng serve` 走 proxy，部署時 nginx 送靜態檔並反代 `/api`。

**Tech Stack:** Angular 20（standalone、signals、`@if`/`@for` 控制流程）、TypeScript、nginx、Docker Compose。Node v24 / npm 11。

**Task 10 是後端收尾**，與前端無關：修掉 Plan 1 留下的登入時序側信道。放在這份計畫是因為它屬於「全部做完後的安全性收尾」，不阻擋任何前端工作，可在任意時點插入。

## 執行順序

Task 7 已拆成 7a/7b/7c、Task 8 已拆成 8a/8b（理由見各自的拆分說明），實際共 13 個 Task：

```
Task 10（登入時序側信道）→ Task 8a（後端屬性篩選）→ 後端封版
   ↓
Task 1（Angular 骨架）→ 2 → 3 → 4 → 5 → 6 → 7a → 7b → 7c → 8b → 9
```

**兩個純後端的 Task（10、8a）先做完再碰前端。** 之後所有工作都關在 `web/` 內，`src/`、`tests/` 完全凍結——這條界線讓每個前端 Task 的 review 都可以用「`git diff --stat -- src tests` 必須為空」機械化地驗證有沒有越界。反過來排（前端做到一半才回頭改後端）會讓 `SearchItemsQuery` 的簽章在前端服務層已經接上之後才變動。

---

## 檔案結構

```
web/
  src/app/
    core/
      models.ts                 後端 DTO 的 TypeScript 對應
      auth.service.ts           signals 保存 token 與 user
      auth.interceptor.ts       附加 Bearer、401 自動 refresh
      error.interceptor.ts      ProblemDetails → 統一錯誤事件
      notification.service.ts   錯誤/成功訊息
      auth.guard.ts
      api/                      catalog / category / share / ingestion service
    shared/
      dynamic-form/             DynamicFormComponent（核心）
      item-card/                ItemCardComponent
      image-uploader/           ImageUploaderComponent
      tag-input/
    features/
      showcase/                 首頁瀑布流牆
      catalog/                  完整庫存 + 篩選側欄 + grid/list
      item-detail/              檢視 + 編輯
      categories/               品類 schema 編輯器
      settings/                 Steam 綁定 + 同步紀錄
      public/                   分享頁（獨立 layout，無 auth）
    app.routes.ts
    app.config.ts
  proxy.conf.json
  Dockerfile
  nginx.conf
docker-compose.yml
src/MyCollection.Api/Dockerfile
```

---

### Task 1：Angular 專案骨架

**Files:**
- Create: `web/`（`ng new`）
- Create: `web/proxy.conf.json`
- Modify: `web/angular.json`、`web/src/app/app.config.ts`

- [ ] **Step 1: 建立專案**

在 **worktree 根目錄**執行（不是 master checkout 的 `F:\VibeCode\MyCollection`）：

```bash
NG_CLI_ANALYTICS=false npx --yes @angular/cli@20 new web --style=css --ssr=false --routing --skip-git --package-manager=npm --defaults
```

`NG_CLI_ANALYTICS=false` 與 `--defaults` 缺一不可：前者擋掉 npx 首次執行時的 analytics 詢問，後者讓 Angular 20 其餘的互動提問（AI 工具設定、zoneless 等）全部取預設值。少了它們，指令會停在提問畫面等 stdin，而自動化執行環境的 stdin 是關閉的，會直接卡死。

實測產出 Angular **20.3**，測試框架是 **Karma + Jasmine**（`@angular/build:karma`），不是 vitest。

- [ ] **Step 2: 建立開發用 proxy**

`web/proxy.conf.json`：

```json
{
  "/api": {
    "target": "http://localhost:5080",
    "secure": false,
    "changeOrigin": true,
    "pathRewrite": { "^/api": "" }
  }
}
```

`web/angular.json` 的 `projects.web.architect.serve` 加入：

```json
            "options": { "proxyConfig": "proxy.conf.json" }
```

注意 `ng new` 產生的 `serve` 底下**只有 `configurations`、沒有 `options` 鍵**，必須整個建出來而非「加入既有的 options」。

`pathRewrite` 是 webpack-dev-server 的語法，而 Angular 17+ 的 dev-server 已改用 Vite——但 `@angular/build/src/utils/load-proxy-config.js` 會把 `pathRewrite` 轉譯成 Vite 的 `rewrite` 函式，所以上面的設定在 Vite base 的 dev-server 上有效，不需要改寫。

- [ ] **Step 3: 固定 API base path**

`web/src/app/core/api-base.ts`：

```ts
/**
 * 開發時由 proxy.conf.json 轉發到 localhost:5080，
 * 部署時由 nginx 反代到 api 容器。前端永遠只認 /api。
 */
export const API_BASE = '/api';
```

- [ ] **Step 4: 驗證建置與測試**

Run: `cd web && npm run build`
Expected: `Application bundle generation complete`，輸出位置 `web/dist/web`，實際靜態檔在 `web/dist/web/browser`（Task 9 的 Dockerfile 依賴這個路徑）。

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: `TOTAL: 2 SUCCESS`（`ng new` 的樣板測試）。此指令需要本機有 Chrome，Karma 會自行探測。

- [ ] **Step 5: Commit**

```bash
git add web .gitignore
git commit -m "chore(web): 建立 Angular 20 專案骨架"
```

---

### Task 2：核心型別與 Auth 服務

**Files:**
- Create: `web/src/app/core/models.ts`
- Create: `web/src/app/core/auth.service.ts`
- Test: `web/src/app/core/auth.service.spec.ts`

- [ ] **Step 1: 寫失敗測試**

`web/src/app/core/auth.service.spec.ts`：

```ts
import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { AuthService } from './auth.service';
import { AuthResponse } from './models';

const response: AuthResponse = {
  accessToken: 'access-1',
  refreshToken: 'refresh-1',
  expiresAt: '2026-07-25T03:30:00Z',
  user: { id: 'u1', email: 'a@b.c', displayName: 'Adam' },
};

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('starts unauthenticated', () => {
    expect(service.isAuthenticated()).toBe(false);
    expect(service.accessToken()).toBeNull();
  });

  it('stores tokens and user after login', async () => {
    const promise = service.login('a@b.c', 'P@ssw0rd!');
    http.expectOne(`/api/auth/login`).flush(response);
    await promise;

    expect(service.isAuthenticated()).toBe(true);
    expect(service.accessToken()).toBe('access-1');
    expect(service.user()?.displayName).toBe('Adam');
  });

  it('restores the session from storage on construction', async () => {
    const promise = service.login('a@b.c', 'P@ssw0rd!');
    http.expectOne(`/api/auth/login`).flush(response);
    await promise;

    const restored = TestBed.runInInjectionContext(() => new AuthService());
    expect(restored.isAuthenticated()).toBe(true);
    expect(restored.accessToken()).toBe('access-1');
  });

  it('clears everything on logout', async () => {
    const promise = service.login('a@b.c', 'P@ssw0rd!');
    http.expectOne(`/api/auth/login`).flush(response);
    await promise;

    service.logout();

    expect(service.isAuthenticated()).toBe(false);
    expect(localStorage.getItem('mycollection.session')).toBeNull();
  });

  it('refresh replaces the stored token pair', async () => {
    const login = service.login('a@b.c', 'P@ssw0rd!');
    http.expectOne(`/api/auth/login`).flush(response);
    await login;

    const refresh = service.refresh();
    const request = http.expectOne(`/api/auth/refresh`);
    expect(request.request.body).toEqual({ refreshToken: 'refresh-1' });
    request.flush({ ...response, accessToken: 'access-2', refreshToken: 'refresh-2' });
    await refresh;

    expect(service.accessToken()).toBe('access-2');
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: 找不到模組 `./auth.service`。

- [ ] **Step 3: 實作型別**

`web/src/app/core/models.ts`：

```ts
export interface UserDto {
  id: string;
  email: string;
  displayName: string;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: UserDto;
}

export type FieldType = 'Text' | 'Number' | 'Date' | 'Select' | 'Bool' | 'Url';

export interface CategoryFieldDto {
  key: string;
  label: string;
  type: FieldType;
  options: string[] | null;
  required: boolean;
  searchable: boolean;
  showOnCard: boolean;
}

export interface CategoryDto {
  id: string;
  name: string;
  icon: string;
  kind: 'Physical' | 'Digital';
  isSystem: boolean;
  fields: CategoryFieldDto[];
}

export interface ItemImageDto {
  id: string;
  path: string;
  cardPath: string;
  thumbPath: string;
  isPrimary: boolean;
  order: number;
}

export interface AcquisitionDto {
  acquiredAt: string | null;
  price: { amount: number; currency: string } | null;
  vendor: string | null;
}

export interface ItemDto {
  id: string;
  categoryId: string;
  name: string;
  description: string | null;
  images: ItemImageDto[];
  tags: string[];
  isShowcased: boolean;
  source: 'Manual' | 'Steam' | 'OpenGraph';
  externalRef: { provider: string; externalId: string; url: string | null; lastSyncedAt: string } | null;
  acquisition: AcquisitionDto | null;
  locationId: string | null;
  attributes: Record<string, unknown>;
  createdAt: string;
  updatedAt: string;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ShareLinkDto {
  id: string;
  slug: string;
  scope: 'Showcase' | 'Category';
  includeCategoryIds: string[];
  includePrice: boolean;
  expiresAt: string | null;
  createdAt: string;
}

export interface PublicItemDto {
  id: string;
  name: string;
  description: string | null;
  categoryName: string;
  tags: string[];
  images: { cardPath: string; thumbPath: string; isPrimary: boolean; order: number }[];
  attributes: Record<string, unknown>;
  price: { amount: number; currency: string } | null;
}

export interface PublicShareDto {
  ownerDisplayName: string;
  scope: string;
  items: PublicItemDto[];
}

export interface SyncJobDto {
  id: string;
  provider: string;
  status: 'Running' | 'Succeeded' | 'Failed';
  created: number;
  updated: number;
  failed: number;
  error: string | null;
  startedAt: string;
  finishedAt: string | null;
}

export interface ExternalAccountDto {
  provider: string;
  externalUserId: string;
  updatedAt: string;
}

export interface FetchedMetadataDto {
  provider: string;
  externalId: string;
  name: string;
  description: string | null;
  imageUrl: string | null;
  attributes: Record<string, unknown>;
}

/** RFC 9457 ProblemDetails。errors 只在 400 驗證失敗時出現。 */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
```

- [ ] **Step 4: 實作 AuthService**

`web/src/app/core/auth.service.ts`：

```ts
import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE } from './api-base';
import { AuthResponse, UserDto } from './models';

const STORAGE_KEY = 'mycollection.session';

interface StoredSession {
  accessToken: string;
  refreshToken: string;
  user: UserDto;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly session = signal<StoredSession | null>(this.restore());

  readonly accessToken = computed(() => this.session()?.accessToken ?? null);
  readonly refreshToken = computed(() => this.session()?.refreshToken ?? null);
  readonly user = computed(() => this.session()?.user ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);

  async register(email: string, password: string, displayName: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>(`${API_BASE}/auth/register`, { email, password, displayName }),
    );
    this.store(response);
  }

  async login(email: string, password: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>(`${API_BASE}/auth/login`, { email, password }),
    );
    this.store(response);
  }

  /** 401 時由 auth.interceptor 呼叫。失敗代表 refresh token 也過期了。 */
  async refresh(): Promise<void> {
    const refreshToken = this.refreshToken();
    if (!refreshToken) {
      throw new Error('No refresh token available.');
    }

    const response = await firstValueFrom(
      this.http.post<AuthResponse>(`${API_BASE}/auth/refresh`, { refreshToken }),
    );
    this.store(response);
  }

  logout(): void {
    this.session.set(null);
    localStorage.removeItem(STORAGE_KEY);
  }

  private store(response: AuthResponse): void {
    const session: StoredSession = {
      accessToken: response.accessToken,
      refreshToken: response.refreshToken,
      user: response.user,
    };
    this.session.set(session);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
  }

  private restore(): StoredSession | null {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw) as StoredSession;
    } catch {
      localStorage.removeItem(STORAGE_KEY);
      return null;
    }
  }
}
```

- [ ] **Step 5: 跑測試確認通過**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: `AuthService` 5 筆全過。

- [ ] **Step 6: Commit**

```bash
git add web
git commit -m "feat(web): 新增核心型別與 AuthService"
```

---

### Task 3：HTTP interceptors 與 guard

**Files:**
- Create: `web/src/app/core/auth.interceptor.ts`
- Create: `web/src/app/core/notification.service.ts`
- Create: `web/src/app/core/error.interceptor.ts`
- Create: `web/src/app/core/auth.guard.ts`
- Modify: `web/src/app/app.config.ts`
- Test: `web/src/app/core/auth.interceptor.spec.ts`

- [ ] **Step 1: 寫失敗測試**

`web/src/app/core/auth.interceptor.spec.ts`：

```ts
import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => controller.verify());

  /**
   * 讓 refresh() 的 promise 鏈與 from(promise) 的 .then 全部跑完。
   * setTimeout 是 macrotask，排在所有 pending microtask 之後，
   * 因此不必猜要 await 幾次 Promise.resolve()。
   */
  const settle = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

  async function signIn(): Promise<void> {
    const promise = auth.login('a@b.c', 'x');
    controller.expectOne('/api/auth/login').flush({
      accessToken: 'access-1',
      refreshToken: 'refresh-1',
      expiresAt: '2026-07-25T03:30:00Z',
      user: { id: 'u1', email: 'a@b.c', displayName: 'Adam' },
    });
    await promise;
  }

  it('does not attach a header when signed out', () => {
    http.get('/api/items').subscribe();

    expect(controller.expectOne('/api/items').request.headers.has('Authorization')).toBe(false);
  });

  it('attaches the bearer token when signed in', async () => {
    await signIn();

    http.get('/api/items').subscribe();

    expect(controller.expectOne('/api/items').request.headers.get('Authorization'))
      .toBe('Bearer access-1');
  });

  it('never attaches the token to auth endpoints', async () => {
    await signIn();

    http.post('/api/auth/refresh', {}).subscribe();

    expect(controller.expectOne('/api/auth/refresh').request.headers.has('Authorization')).toBe(false);
  });

  it('refreshes once on 401 and retries the original request', async () => {
    await signIn();

    const result = firstValueFrom(http.get<{ ok: boolean }>('/api/items'));

    controller.expectOne('/api/items').flush(null, { status: 401, statusText: 'Unauthorized' });

    controller.expectOne('/api/auth/refresh').flush({
      accessToken: 'access-2',
      refreshToken: 'refresh-2',
      expiresAt: '2026-07-25T04:00:00Z',
      user: { id: 'u1', email: 'a@b.c', displayName: 'Adam' },
    });

    await settle();

    const retried = controller.expectOne('/api/items');
    expect(retried.request.headers.get('Authorization')).toBe('Bearer access-2');
    retried.flush({ ok: true });

    expect(await result).toEqual({ ok: true });
  });

  it('logs out when the refresh also fails', async () => {
    await signIn();

    firstValueFrom(http.get('/api/items')).catch(() => undefined);

    controller.expectOne('/api/items').flush(null, { status: 401, statusText: 'Unauthorized' });
    controller.expectOne('/api/auth/refresh').flush(null, { status: 403, statusText: 'Forbidden' });

    await settle();

    expect(auth.isAuthenticated()).toBe(false);
  });
});
```

> **為什麼兩個 401 測試要 `await settle()`：** `AuthService.refresh()` 是 `async` method，內部用
> `firstValueFrom(http.post(...))`；`auth.interceptor.ts` 再用 `from(auth.refresh()).pipe(switchMap(...))`
> 把這個 Promise 接回 Observable。`flush()` 對 mock 後端而言是同步的，但 Promise 的
> `.then()`／`await` 續行永遠是排入 microtask queue，不會跟觸發它的同步程式碼在同一輪跑完。
> 從「refresh 的 `/api/auth/refresh` 被 flush」到「重試的請求真正送出」，中間要經過
> `promise resolve → store() → async function 回傳的 promise settle → from(promise) 的 .then →
> switchMap`，共數個 microtask tick；「refresh 失敗時呼叫 `auth.logout()`」也是同樣的鏈。
> 若在 `flush()` 後不等待就直接呼叫 `controller.expectOne(...)` 或斷言，會確定性地撲空
> （不是 flaky，是每次都失敗）。
>
> 修正時選 `setTimeout(resolve, 0)`（macrotask）而不是 `await Promise.resolve()`
> （microtask）：`await Promise.resolve()` 必須猜對要等幾個 tick，一旦 `AuthService.refresh()`
> 之後多加一個 `await`，tick 數就變了，測試又會悄悄壞掉；`setTimeout` 一定排在所有現存的
> pending microtask 之後執行，不必管鏈有多長，比較不脆弱。`fakeAsync`/`tick()` 也能解決同樣的
> 問題，但要連 `signIn()` 一起改寫（`fakeAsync` 底下 promise 的 zone 語意不同），改動面較大，
> 這裡不採用。

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: 找不到 `./auth.interceptor`。

- [ ] **Step 3: 實作**

`web/src/app/core/auth.interceptor.ts`：

```ts
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

const AUTH_ENDPOINTS = ['/auth/login', '/auth/register', '/auth/refresh'];

/** 附加 Bearer token；401 時嘗試一次 refresh 後重送原請求。 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);

  const isAuthEndpoint = AUTH_ENDPOINTS.some((path) => request.url.includes(path));
  const token = auth.accessToken();

  const authorised =
    token && !isAuthEndpoint
      ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : request;

  return next(authorised).pipe(
    catchError((error: unknown) => {
      const isUnauthorised = error instanceof HttpErrorResponse && error.status === 401;

      if (!isUnauthorised || isAuthEndpoint || !auth.refreshToken()) {
        return throwError(() => error);
      }

      return from(auth.refresh()).pipe(
        switchMap(() =>
          next(request.clone({ setHeaders: { Authorization: `Bearer ${auth.accessToken()}` } })),
        ),
        catchError((refreshError: unknown) => {
          auth.logout();
          return throwError(() => refreshError);
        }),
      );
    }),
  );
};
```

`web/src/app/core/notification.service.ts`：

```ts
import { Injectable, signal } from '@angular/core';

export interface Notification {
  id: number;
  kind: 'error' | 'success';
  message: string;
}

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private nextId = 1;

  readonly notifications = signal<Notification[]>([]);

  error(message: string): void {
    this.push('error', message);
  }

  success(message: string): void {
    this.push('success', message);
  }

  dismiss(id: number): void {
    this.notifications.update((all) => all.filter((n) => n.id !== id));
  }

  private push(kind: Notification['kind'], message: string): void {
    const notification: Notification = { id: this.nextId++, kind, message };
    this.notifications.update((all) => [...all, notification]);
    setTimeout(() => this.dismiss(notification.id), 6000);
  }
}
```

`web/src/app/core/error.interceptor.ts`：

```ts
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ProblemDetails } from './models';
import { NotificationService } from './notification.service';

/** 把 RFC 9457 ProblemDetails 轉成可讀訊息。401 交給 authInterceptor 處理，不在這裡吵。 */
export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  const notifications = inject(NotificationService);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status !== 401) {
        notifications.error(describe(error));
      }

      return throwError(() => error);
    }),
  );
};

function describe(error: HttpErrorResponse): string {
  if (error.status === 0) {
    return '無法連線到伺服器。';
  }

  const problem = error.error as ProblemDetails | null;

  if (problem?.errors) {
    const messages = Object.entries(problem.errors)
      .map(([field, texts]) => `${field}: ${texts.join('、')}`)
      .join('\n');
    return messages || (problem.title ?? '請求失敗。');
  }

  return problem?.detail ?? problem?.title ?? `請求失敗（HTTP ${error.status}）。`;
}
```

`web/src/app/core/auth.guard.ts`：

```ts
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.isAuthenticated()
    ? true
    : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
```

`web/src/app/app.config.ts`：

```ts
import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZoneChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { authInterceptor } from './core/auth.interceptor';
import { errorInterceptor } from './core/error.interceptor';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
  ],
};
```

若 `ng new` 產生的樣板沒有 `provideBrowserGlobalErrorListeners`，移除該行即可。

- [ ] **Step 4: 跑測試確認通過**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: `authInterceptor` 5 筆全過。

- [ ] **Step 5: Commit**

```bash
git add web
git commit -m "feat(web): 新增 JWT interceptor、錯誤處理與 auth guard"
```

---

### Task 4：DynamicFormComponent（核心元件）

**Files:**
- Create: `web/src/app/shared/dynamic-form/dynamic-form.component.ts`
- Test: `web/src/app/shared/dynamic-form/dynamic-form.component.spec.ts`

`fields` 同時餵給後端驗證與這個元件。新增品類完全不需要改這裡的程式碼。

- [ ] **Step 1: 寫失敗測試**

`web/src/app/shared/dynamic-form/dynamic-form.component.spec.ts`：

```ts
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CategoryFieldDto } from '../../core/models';
import { DynamicFormComponent } from './dynamic-form.component';

function field(overrides: Partial<CategoryFieldDto>): CategoryFieldDto {
  return {
    key: 'brand',
    label: '廠商',
    type: 'Text',
    options: null,
    required: false,
    searchable: false,
    showOnCard: false,
    ...overrides,
  };
}

describe('DynamicFormComponent', () => {
  let fixture: ComponentFixture<DynamicFormComponent>;
  let component: DynamicFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [DynamicFormComponent] }).compileComponents();
    fixture = TestBed.createComponent(DynamicFormComponent);
    component = fixture.componentInstance;
  });

  function render(fields: CategoryFieldDto[], value: Record<string, unknown> = {}): void {
    fixture.componentRef.setInput('fields', fields);
    fixture.componentRef.setInput('value', value);
    fixture.detectChanges();
  }

  it('renders one control per field', () => {
    render([field({ key: 'brand' }), field({ key: 'scale', label: '比例' })]);

    const inputs = fixture.nativeElement.querySelectorAll('[data-field]');
    expect(inputs.length).toBe(2);
  });

  it('renders a select with the schema options', () => {
    render([field({ key: 'brand', type: 'Select', options: ['GSC', 'ALTER'] })]);

    const select: HTMLSelectElement = fixture.nativeElement.querySelector('select[data-field="brand"]');
    expect(select).toBeTruthy();
    expect(Array.from(select.options).map((o) => o.value)).toEqual(['', 'GSC', 'ALTER']);
  });

  it('maps field types to input types', () => {
    render([
      field({ key: 'height', type: 'Number' }),
      field({ key: 'releasedAt', type: 'Date' }),
      field({ key: 'isLimited', type: 'Bool' }),
      field({ key: 'productUrl', type: 'Url' }),
    ]);

    const el = fixture.nativeElement;
    expect(el.querySelector('[data-field="height"]').type).toBe('number');
    expect(el.querySelector('[data-field="releasedAt"]').type).toBe('date');
    expect(el.querySelector('[data-field="isLimited"]').type).toBe('checkbox');
    expect(el.querySelector('[data-field="productUrl"]').type).toBe('url');
  });

  it('marks required fields invalid when empty', () => {
    render([field({ key: 'brand', required: true })]);

    expect(component.form.valid).toBe(false);

    component.form.controls['brand'].setValue('GSC');
    expect(component.form.valid).toBe(true);
  });

  it('validates url fields', () => {
    render([field({ key: 'productUrl', type: 'Url' })]);

    component.form.controls['productUrl'].setValue('not a url');
    expect(component.form.valid).toBe(false);

    component.form.controls['productUrl'].setValue('https://example.com/a');
    expect(component.form.valid).toBe(true);
  });

  it('patches initial values from the value input', () => {
    render([field({ key: 'brand' }), field({ key: 'height', type: 'Number' })], {
      brand: 'GSC',
      height: 200,
    });

    expect(component.form.value).toEqual({ brand: 'GSC', height: 200 });
  });

  it('emits attributes with empty strings dropped', () => {
    const emitted: Record<string, unknown>[] = [];
    render([field({ key: 'brand' }), field({ key: 'scale' })], { brand: 'GSC' });
    component.valueChange.subscribe((v) => emitted.push(v));

    component.form.controls['brand'].setValue('ALTER');

    expect(emitted.at(-1)).toEqual({ brand: 'ALTER' });
  });

  it('coerces date values to ISO-8601 UTC', () => {
    const emitted: Record<string, unknown>[] = [];
    render([field({ key: 'releasedAt', type: 'Date' })]);
    component.valueChange.subscribe((v) => emitted.push(v));

    component.form.controls['releasedAt'].setValue('2026-01-15');

    expect(emitted.at(-1)).toEqual({ releasedAt: '2026-01-15T00:00:00.000Z' });
  });

  it('rebuilds the form when the schema changes', () => {
    render([field({ key: 'brand' })]);
    expect(Object.keys(component.form.controls)).toEqual(['brand']);

    fixture.componentRef.setInput('fields', [field({ key: 'publisher', label: '發行商' })]);
    fixture.detectChanges();

    expect(Object.keys(component.form.controls)).toEqual(['publisher']);
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: 找不到 `./dynamic-form.component`。

- [ ] **Step 3: 實作**

`web/src/app/shared/dynamic-form/dynamic-form.component.ts`：

```ts
import { Component, effect, input, output } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { CategoryFieldDto } from '../../core/models';

/**
 * 吃 CategoryField[] 產出 Reactive Form。這是 schema 驅動的最後一哩：
 * 同一份 fields 已經產生了後端驗證規則與篩選器，這裡再產生表單。
 */
@Component({
  selector: 'app-dynamic-form',
  imports: [ReactiveFormsModule],
  template: `
    <form [formGroup]="form" class="dynamic-form">
      @for (field of fields(); track field.key) {
        <label class="dynamic-form__row">
          <span class="dynamic-form__label">
            {{ field.label }}
            @if (field.required) { <em aria-hidden="true">*</em> }
          </span>

          @switch (field.type) {
            @case ('Select') {
              <select [formControlName]="field.key" [attr.data-field]="field.key">
                <option value=""></option>
                @for (option of field.options ?? []; track option) {
                  <option [value]="option">{{ option }}</option>
                }
              </select>
            }
            @case ('Bool') {
              <input type="checkbox" [formControlName]="field.key" [attr.data-field]="field.key" />
            }
            @case ('Number') {
              <input type="number" [formControlName]="field.key" [attr.data-field]="field.key" />
            }
            @case ('Date') {
              <input type="date" [formControlName]="field.key" [attr.data-field]="field.key" />
            }
            @case ('Url') {
              <input type="url" [formControlName]="field.key" [attr.data-field]="field.key" />
            }
            @default {
              <input type="text" [formControlName]="field.key" [attr.data-field]="field.key" />
            }
          }

          @if (form.controls[field.key]?.invalid && form.controls[field.key]?.touched) {
            <small class="dynamic-form__error">{{ errorFor(field) }}</small>
          }
        </label>
      }
    </form>
  `,
  styles: `
    .dynamic-form { display: grid; gap: 0.75rem; }
    .dynamic-form__row { display: grid; gap: 0.25rem; }
    .dynamic-form__label em { color: #c0392b; font-style: normal; }
    .dynamic-form__error { color: #c0392b; font-size: 0.8rem; }
  `,
})
export class DynamicFormComponent {
  readonly fields = input.required<CategoryFieldDto[]>();
  readonly value = input<Record<string, unknown>>({});

  readonly valueChange = output<Record<string, unknown>>();
  readonly validityChange = output<boolean>();

  form = new FormGroup<Record<string, FormControl>>({});

  constructor() {
    effect(() => {
      const fields = this.fields();
      const initial = this.value();

      this.form = new FormGroup<Record<string, FormControl>>(
        Object.fromEntries(fields.map((f) => [f.key, this.buildControl(f, initial[f.key])])),
      );

      this.form.valueChanges.subscribe(() => {
        this.valueChange.emit(this.attributes());
        this.validityChange.emit(this.form.valid);
      });

      this.validityChange.emit(this.form.valid);
    });
  }

  /** 目前表單值，已剔除空值——後端把空字串當成型別錯誤。 */
  attributes(): Record<string, unknown> {
    const result: Record<string, unknown> = {};

    for (const field of this.fields()) {
      const raw = this.form.controls[field.key]?.value;

      if (raw === null || raw === undefined || raw === '') {
        continue;
      }

      result[field.key] = this.coerce(field, raw);
    }

    return result;
  }

  errorFor(field: CategoryFieldDto): string {
    const control = this.form.controls[field.key];

    if (control?.hasError('required')) {
      return `${field.label} 為必填`;
    }
    if (control?.hasError('pattern')) {
      return `${field.label} 必須是完整的 http(s) 網址`;
    }

    return `${field.label} 格式不正確`;
  }

  private buildControl(field: CategoryFieldDto, initial: unknown): FormControl {
    const validators: ValidatorFn[] = [];

    if (field.required) {
      validators.push(field.type === 'Bool' ? Validators.requiredTrue : Validators.required);
    }

    if (field.type === 'Url') {
      validators.push(Validators.pattern(/^https?:\/\/\S+$/));
    }

    return new FormControl(this.toControlValue(field, initial), validators);
  }

  private toControlValue(field: CategoryFieldDto, initial: unknown): unknown {
    if (initial === null || initial === undefined) {
      return field.type === 'Bool' ? false : '';
    }

    // ISO-8601 → yyyy-MM-dd，input[type=date] 只接受這個格式
    if (field.type === 'Date' && typeof initial === 'string') {
      return initial.slice(0, 10);
    }

    return initial;
  }

  private coerce(field: CategoryFieldDto, raw: unknown): unknown {
    switch (field.type) {
      case 'Number':
        return Number(raw);
      case 'Bool':
        return Boolean(raw);
      case 'Date':
        return new Date(`${String(raw)}T00:00:00Z`).toISOString();
      default:
        return raw;
    }
  }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: `DynamicFormComponent` 9 筆全過。

- [ ] **Step 5: Commit**

```bash
git add web
git commit -m "feat(web): 新增 schema 驅動的 DynamicFormComponent"
```

---

### Task 5：API 服務層

**Files:**
- Create: `web/src/app/core/api/catalog.service.ts`
- Create: `web/src/app/core/api/category.service.ts`
- Create: `web/src/app/core/api/share.service.ts`
- Create: `web/src/app/core/api/ingestion.service.ts`
- Test: `web/src/app/core/api/catalog.service.spec.ts`

- [ ] **Step 1: 寫失敗測試**

`web/src/app/core/api/catalog.service.spec.ts`：

```ts
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { CatalogService } from './catalog.service';

describe('CatalogService', () => {
  let service: CatalogService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CatalogService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('builds a search query with only the supplied filters', () => {
    firstValueFrom(service.search({ search: 'portal', tags: ['FPS', '最愛'], page: 2, pageSize: 12 }));

    const request = http.expectOne((r) => r.url === '/api/items');
    expect(request.request.params.get('search')).toBe('portal');
    expect(request.request.params.getAll('tags')).toEqual(['FPS', '最愛']);
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('12');
    expect(request.request.params.has('categoryId')).toBe(false);
    request.flush({ items: [], total: 0, page: 2, pageSize: 12 });
  });

  it('posts an item create payload', () => {
    firstValueFrom(
      service.create({
        categoryId: 'c1',
        name: '公仔',
        description: null,
        tags: [],
        isShowcased: false,
        attributes: { brand: 'GSC' },
        acquisition: null,
      }),
    );

    const request = http.expectOne('/api/items');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.attributes).toEqual({ brand: 'GSC' });
    request.flush({});
  });

  it('uploads an image as multipart form data', () => {
    firstValueFrom(service.uploadImage('i1', new File(['x'], 'a.png', { type: 'image/png' })));

    const request = http.expectOne('/api/items/i1/images');
    expect(request.request.method).toBe('POST');
    expect(request.request.body instanceof FormData).toBe(true);
    request.flush({});
  });

  it('fetches the showcase wall', () => {
    firstValueFrom(service.showcase(1, 24));

    const request = http.expectOne((r) => r.url === '/api/showcase');
    expect(request.request.params.get('pageSize')).toBe('24');
    request.flush({ items: [], total: 0, page: 1, pageSize: 24 });
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: 找不到 `./catalog.service`。

- [ ] **Step 3: 實作**

`web/src/app/core/api/catalog.service.ts`：

```ts
import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-base';
import { ItemDto, ItemImageDto, PagedResult } from '../models';

export interface ItemSearchOptions {
  search?: string;
  categoryId?: string;
  tags?: string[];
  isShowcased?: boolean;
  page?: number;
  pageSize?: number;
}

export interface ItemWritePayload {
  categoryId: string;
  name: string;
  description: string | null;
  tags: string[];
  isShowcased: boolean;
  attributes: Record<string, unknown>;
  acquisition: {
    acquiredAt: string | null;
    amount: number | null;
    currency: string | null;
    vendor: string | null;
  } | null;
  locationId?: string | null;
}

@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly http = inject(HttpClient);

  search(options: ItemSearchOptions): Observable<PagedResult<ItemDto>> {
    let params = new HttpParams();

    if (options.search) params = params.set('search', options.search);
    if (options.categoryId) params = params.set('categoryId', options.categoryId);
    if (options.isShowcased !== undefined) params = params.set('isShowcased', options.isShowcased);
    if (options.page) params = params.set('page', options.page);
    if (options.pageSize) params = params.set('pageSize', options.pageSize);
    for (const tag of options.tags ?? []) {
      params = params.append('tags', tag);
    }

    return this.http.get<PagedResult<ItemDto>>(`${API_BASE}/items`, { params });
  }

  showcase(page = 1, pageSize = 24): Observable<PagedResult<ItemDto>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResult<ItemDto>>(`${API_BASE}/showcase`, { params });
  }

  get(id: string): Observable<ItemDto> {
    return this.http.get<ItemDto>(`${API_BASE}/items/${id}`);
  }

  tags(): Observable<string[]> {
    return this.http.get<string[]>(`${API_BASE}/items/tags`);
  }

  create(payload: ItemWritePayload): Observable<ItemDto> {
    return this.http.post<ItemDto>(`${API_BASE}/items`, payload);
  }

  update(id: string, payload: ItemWritePayload): Observable<ItemDto> {
    return this.http.put<ItemDto>(`${API_BASE}/items/${id}`, payload);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE}/items/${id}`);
  }

  uploadImage(itemId: string, file: File): Observable<ItemImageDto> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<ItemImageDto>(`${API_BASE}/items/${itemId}/images`, form);
  }

  deleteImage(itemId: string, imageId: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE}/items/${itemId}/images/${imageId}`);
  }

  setPrimaryImage(itemId: string, imageId: string): Observable<void> {
    return this.http.post<void>(`${API_BASE}/items/${itemId}/images/${imageId}/primary`, null);
  }
}
```

`web/src/app/core/api/category.service.ts`：

```ts
import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-base';
import { CategoryDto, CategoryFieldDto } from '../models';

export interface CategoryWritePayload {
  name: string;
  icon: string;
  kind: 'Physical' | 'Digital';
  fields: CategoryFieldDto[];
}

@Injectable({ providedIn: 'root' })
export class CategoryService {
  private readonly http = inject(HttpClient);

  list(): Observable<CategoryDto[]> {
    return this.http.get<CategoryDto[]>(`${API_BASE}/categories`);
  }

  create(payload: CategoryWritePayload): Observable<CategoryDto> {
    return this.http.post<CategoryDto>(`${API_BASE}/categories`, payload);
  }

  update(id: string, payload: CategoryWritePayload): Observable<CategoryDto> {
    return this.http.put<CategoryDto>(`${API_BASE}/categories/${id}`, payload);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE}/categories/${id}`);
  }
}
```

`web/src/app/core/api/share.service.ts`：

```ts
import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-base';
import { PublicShareDto, ShareLinkDto } from '../models';

export interface ShareWritePayload {
  scope: 'Showcase' | 'Category';
  includeCategoryIds: string[];
  includePrice: boolean;
  expiresAt: string | null;
}

@Injectable({ providedIn: 'root' })
export class ShareService {
  private readonly http = inject(HttpClient);

  list(): Observable<ShareLinkDto[]> {
    return this.http.get<ShareLinkDto[]>(`${API_BASE}/shares`);
  }

  create(payload: ShareWritePayload): Observable<ShareLinkDto> {
    return this.http.post<ShareLinkDto>(`${API_BASE}/shares`, payload);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE}/shares/${id}`);
  }

  /** 匿名端點：authInterceptor 不會附加 token，因為使用者可能未登入。 */
  getPublic(slug: string): Observable<PublicShareDto> {
    return this.http.get<PublicShareDto>(`${API_BASE}/public/${slug}`);
  }
}
```

`web/src/app/core/api/ingestion.service.ts`：

```ts
import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-base';
import { ExternalAccountDto, FetchedMetadataDto, SyncJobDto } from '../models';

@Injectable({ providedIn: 'root' })
export class IngestionService {
  private readonly http = inject(HttpClient);

  accounts(): Observable<ExternalAccountDto[]> {
    return this.http.get<ExternalAccountDto[]>(`${API_BASE}/external-accounts`);
  }

  link(provider: string, externalUserId: string, apiKey: string): Observable<ExternalAccountDto> {
    return this.http.post<ExternalAccountDto>(`${API_BASE}/external-accounts`, {
      provider,
      externalUserId,
      apiKey,
    });
  }

  unlink(provider: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE}/external-accounts/${provider}`);
  }

  sync(provider: string): Observable<SyncJobDto> {
    return this.http.post<SyncJobDto>(`${API_BASE}/ingest/sync/${provider}`, null);
  }

  jobs(limit = 20): Observable<SyncJobDto[]> {
    return this.http.get<SyncJobDto[]>(`${API_BASE}/ingest/jobs`, {
      params: new HttpParams().set('limit', limit),
    });
  }

  fetchByUrl(url: string): Observable<FetchedMetadataDto> {
    return this.http.post<FetchedMetadataDto>(`${API_BASE}/ingest/fetch`, null, {
      params: new HttpParams().set('url', url),
    });
  }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: `CatalogService` 4 筆全過。

- [ ] **Step 5: Commit**

```bash
git add web
git commit -m "feat(web): 新增 API 服務層"
```

---

### Task 6：ItemCard 與 ImageUploader

**Files:**
- Create: `web/src/app/shared/item-card/item-card.component.ts`
- Create: `web/src/app/shared/image-uploader/image-uploader.component.ts`
- Create: `web/src/app/shared/tag-input/tag-input.component.ts`
- Test: `web/src/app/shared/item-card/item-card.component.spec.ts`

- [ ] **Step 1: 寫失敗測試**

`web/src/app/shared/item-card/item-card.component.spec.ts`：

```ts
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ItemDto } from '../../core/models';
import { ItemCardComponent } from './item-card.component';

function item(overrides: Partial<ItemDto> = {}): ItemDto {
  return {
    id: 'i1',
    categoryId: 'c1',
    name: '初音ミク 1/8',
    description: null,
    images: [],
    tags: ['GSC'],
    isShowcased: false,
    source: 'Manual',
    externalRef: null,
    acquisition: null,
    locationId: null,
    attributes: {},
    createdAt: '2026-07-25T03:00:00Z',
    updatedAt: '2026-07-25T03:00:00Z',
    ...overrides,
  };
}

describe('ItemCardComponent', () => {
  let fixture: ComponentFixture<ItemCardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ItemCardComponent],
      providers: [provideRouter([])],
    }).compileComponents();
    fixture = TestBed.createComponent(ItemCardComponent);
  });

  function render(value: ItemDto): void {
    fixture.componentRef.setInput('item', value);
    fixture.detectChanges();
  }

  it('shows the item name', () => {
    render(item());

    expect(fixture.nativeElement.textContent).toContain('初音ミク 1/8');
  });

  it('uses the local card image when present', () => {
    render(item({ images: [{ id: 'x', path: 'p/full.webp', cardPath: 'p/card.webp', thumbPath: 'p/thumb.webp', isPrimary: true, order: 0 }] }));

    const img: HTMLImageElement = fixture.nativeElement.querySelector('img');
    expect(img.getAttribute('src')).toBe('/api/media/p/card.webp');
  });

  it('falls back to the remote header url for synced items without local images', () => {
    render(item({ source: 'Steam', attributes: { headerUrl: 'https://cdn/620.jpg' } }));

    const img: HTMLImageElement = fixture.nativeElement.querySelector('img');
    expect(img.getAttribute('src')).toBe('https://cdn/620.jpg');
  });

  it('renders a placeholder when there is no image at all', () => {
    render(item());

    expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-placeholder]')).toBeTruthy();
  });

  it('marks showcased items', () => {
    render(item({ isShowcased: true }));

    expect(fixture.nativeElement.querySelector('[data-showcased]')).toBeTruthy();
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: 找不到 `./item-card.component`。

- [ ] **Step 3: 實作**

`web/src/app/shared/item-card/item-card.component.ts`：

```ts
import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { API_BASE } from '../../core/api-base';
import { ItemDto } from '../../core/models';

@Component({
  selector: 'app-item-card',
  imports: [RouterLink],
  template: `
    <a class="card" [routerLink]="['/items', item().id]">
      @if (imageUrl(); as url) {
        <img [src]="url" [alt]="item().name" loading="lazy" />
      } @else {
        <div class="card__placeholder" data-placeholder>{{ item().name.charAt(0) }}</div>
      }

      <div class="card__body">
        <h3 class="card__title">{{ item().name }}</h3>
        @if (item().isShowcased) {
          <span class="card__badge" data-showcased>精選</span>
        }
        @if (item().tags.length) {
          <ul class="card__tags">
            @for (tag of item().tags; track tag) {
              <li>{{ tag }}</li>
            }
          </ul>
        }
      </div>
    </a>
  `,
  styles: `
    .card { display: block; border-radius: 0.75rem; overflow: hidden; background: #fff;
            box-shadow: 0 1px 3px rgb(0 0 0 / 12%); color: inherit; text-decoration: none; }
    .card img { width: 100%; display: block; aspect-ratio: 4 / 3; object-fit: cover; }
    .card__placeholder { display: grid; place-items: center; aspect-ratio: 4 / 3;
                         background: #ecf0f1; font-size: 2rem; color: #95a5a6; }
    .card__body { padding: 0.75rem; display: grid; gap: 0.4rem; }
    .card__title { font-size: 0.95rem; margin: 0; }
    .card__badge { justify-self: start; font-size: 0.7rem; padding: 0.1rem 0.4rem;
                   border-radius: 0.25rem; background: #f1c40f; }
    .card__tags { display: flex; flex-wrap: wrap; gap: 0.25rem; list-style: none; margin: 0; padding: 0; }
    .card__tags li { font-size: 0.7rem; padding: 0.1rem 0.4rem; border-radius: 0.25rem; background: #ecf0f1; }
  `,
})
export class ItemCardComponent {
  readonly item = input.required<ItemDto>();

  /**
   * 本地圖片優先；同步進來但尚未被設為 Showcase 的品項還沒下載圖片，
   * 直接引用 provider CDN。
   */
  readonly imageUrl = computed(() => {
    const value = this.item();

    const primary = value.images.find((i) => i.isPrimary) ?? value.images[0];
    if (primary) {
      return `${API_BASE}/media/${primary.cardPath}`;
    }

    for (const key of ['headerUrl', 'iconUrl']) {
      const candidate = value.attributes[key];
      if (typeof candidate === 'string' && candidate.startsWith('http')) {
        return candidate;
      }
    }

    return null;
  });
}
```

`web/src/app/shared/image-uploader/image-uploader.component.ts`：

```ts
import { Component, input, output, signal } from '@angular/core';
import { API_BASE } from '../../core/api-base';
import { ItemImageDto } from '../../core/models';

@Component({
  selector: 'app-image-uploader',
  template: `
    <div class="uploader">
      <div class="uploader__grid">
        @for (image of images(); track image.id) {
          <figure class="uploader__item" [class.uploader__item--primary]="image.isPrimary">
            <img [src]="mediaUrl(image.cardPath)" alt="" />
            <figcaption>
              @if (!image.isPrimary) {
                <button type="button" (click)="setPrimary.emit(image.id)">設為主圖</button>
              }
              <button type="button" (click)="remove.emit(image.id)">刪除</button>
            </figcaption>
          </figure>
        }
      </div>

      <label class="uploader__drop">
        <input type="file" accept="image/*" multiple (change)="onSelected($event)" />
        <span>{{ busy() ? '上傳中…' : '選擇或拖放圖片（單張上限 10 MB）' }}</span>
      </label>
    </div>
  `,
  styles: `
    .uploader { display: grid; gap: 0.75rem; }
    .uploader__grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(120px, 1fr)); gap: 0.5rem; }
    .uploader__item { margin: 0; display: grid; gap: 0.25rem; }
    .uploader__item img { width: 100%; aspect-ratio: 1; object-fit: cover; border-radius: 0.5rem; }
    .uploader__item--primary img { outline: 2px solid #f1c40f; }
    .uploader__drop { display: grid; place-items: center; padding: 1.5rem; gap: 0.5rem;
                      border: 2px dashed #bdc3c7; border-radius: 0.75rem; cursor: pointer; }
  `,
})
export class ImageUploaderComponent {
  readonly images = input<ItemImageDto[]>([]);
  readonly busy = signal(false);

  readonly upload = output<File[]>();
  readonly remove = output<string>();
  readonly setPrimary = output<string>();

  mediaUrl(path: string): string {
    return `${API_BASE}/media/${path}`;
  }

  onSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);

    if (files.length > 0) {
      this.upload.emit(files);
    }

    input.value = '';
  }
}
```

`web/src/app/shared/tag-input/tag-input.component.ts`：

```ts
import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-tag-input',
  template: `
    <div class="tags">
      @for (tag of tags(); track tag) {
        <span class="tags__chip">
          {{ tag }}
          <button type="button" (click)="removeTag(tag)" aria-label="移除">×</button>
        </span>
      }
      <input
        type="text"
        placeholder="新增標籤後按 Enter"
        (keydown.enter)="addTag($event)"
        (keydown.comma)="addTag($event)"
      />
    </div>
  `,
  styles: `
    .tags { display: flex; flex-wrap: wrap; gap: 0.35rem; align-items: center;
            border: 1px solid #dfe4e6; border-radius: 0.5rem; padding: 0.35rem; }
    .tags__chip { display: inline-flex; gap: 0.25rem; align-items: center; font-size: 0.8rem;
                  background: #ecf0f1; border-radius: 0.35rem; padding: 0.1rem 0.4rem; }
    .tags input { border: 0; outline: none; flex: 1; min-width: 8rem; }
  `,
})
export class TagInputComponent {
  readonly tags = input<string[]>([]);
  readonly tagsChange = output<string[]>();

  addTag(event: Event): void {
    event.preventDefault();
    const input = event.target as HTMLInputElement;
    const value = input.value.trim().replace(/,$/, '');

    if (value && !this.tags().includes(value)) {
      this.tagsChange.emit([...this.tags(), value]);
    }

    input.value = '';
  }

  removeTag(tag: string): void {
    this.tagsChange.emit(this.tags().filter((t) => t !== tag));
  }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: `ItemCardComponent` 5 筆全過。

- [ ] **Step 5: Commit**

```bash
git add web
git commit -m "feat(web): 新增 ItemCard、ImageUploader 與 TagInput"
```

---

> **原本這裡是單一的 Task 7（11 個 Step、7 個 component、約 1,100 行）。已拆成 7a / 7b / 7c 三個 Task。**
>
> 拆分理由：單一 Task 的份量超出一個實作者能一次拿在手上的量，兩階段 review 也審不動——reviewer 要嘛草草放行，要嘛在第 4 個 component 才發現第 1 個的設計問題。拆開後每段結束都是可建置、可測試、可 commit 的完整狀態。
>
> **路由表必須跟著拆。** `loadComponent: () => import('./features/catalog/catalog.component')` 是動態 import，但 TypeScript 仍會在**編譯期**檢查模組存在，檔案還沒建就是 `TS2307`、建置直接失敗。所以 7a 只寫它自己建出來的路由，7b、7c 各自追加，不要一次貼完整張表。
>
> **傳給實作者的共通提醒（三段都適用）：** `DynamicFormComponent` 的 `effect` 同時讀 `fields()` 與 `value()`，每次變動都重建 FormGroup。若把 `[value]` 綁到父層用 `(valueChange)` 更新的同一份狀態，會形成「打字 → emit → 父層更新 value → effect 重建表單」的無窮迴圈。**`[value]` 只能綁初始值（例如載入回來的品項），不可綁隨 `valueChange` 變動的狀態。**

---

### Task 7a：路由表、登入頁與 Showcase 牆

**Files:**
- Create: `web/src/app/features/auth/login.component.ts`
- Create: `web/src/app/features/showcase/showcase.component.ts`
- Modify: `web/src/app/app.routes.ts`

- [ ] **Step 1: 路由表（僅 7a 的路由）**

`web/src/app/app.routes.ts`：

```ts
import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login.component').then((m) => m.LoginComponent),
  },
  {
    path: '',
    canActivate: [authGuard],
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./features/showcase/showcase.component').then((m) => m.ShowcaseComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
```

`catalog`、`items/*`、`categories`、`settings`、`p/:slug` 的路由分別由 Task 7b、7c 追加。此刻寫進去會因為 component 檔案不存在而編譯失敗。

- [ ] **Step 2: 登入頁**

`web/src/app/features/auth/login.component.ts`：

```ts
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <main class="login">
      <h1>MyCollection</h1>

      <form (ngSubmit)="submit()">
        @if (mode() === 'register') {
          <label>顯示名稱<input name="displayName" [(ngModel)]="displayName" required /></label>
        }
        <label>Email<input name="email" type="email" [(ngModel)]="email" required /></label>
        <label>密碼<input name="password" type="password" [(ngModel)]="password" required minlength="8" /></label>

        <button type="submit" [disabled]="busy()">
          {{ mode() === 'login' ? '登入' : '註冊' }}
        </button>
      </form>

      <button type="button" class="login__toggle" (click)="toggle()">
        {{ mode() === 'login' ? '還沒有帳號？註冊' : '已經有帳號？登入' }}
      </button>
    </main>
  `,
  styles: `
    .login { max-width: 22rem; margin: 4rem auto; display: grid; gap: 1rem; }
    .login form { display: grid; gap: 0.75rem; }
    .login label { display: grid; gap: 0.25rem; }
    .login__toggle { background: none; border: 0; color: #2980b9; cursor: pointer; }
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly mode = signal<'login' | 'register'>('login');
  readonly busy = signal(false);

  email = '';
  password = '';
  displayName = '';

  toggle(): void {
    this.mode.update((m) => (m === 'login' ? 'register' : 'login'));
  }

  async submit(): Promise<void> {
    this.busy.set(true);

    try {
      if (this.mode() === 'login') {
        await this.auth.login(this.email, this.password);
      } else {
        await this.auth.register(this.email, this.password, this.displayName);
      }

      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/';
      await this.router.navigateByUrl(returnUrl);
    } catch {
      // errorInterceptor 已經顯示訊息
    } finally {
      this.busy.set(false);
    }
  }
}
```

- [ ] **Step 3: Showcase 牆**

`web/src/app/features/showcase/showcase.component.ts`：

```ts
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CatalogService } from '../../core/api/catalog.service';
import { ItemDto } from '../../core/models';
import { ItemCardComponent } from '../../shared/item-card/item-card.component';

@Component({
  selector: 'app-showcase',
  imports: [ItemCardComponent, RouterLink],
  template: `
    <header class="showcase__header">
      <h1>精選收藏</h1>
      <a routerLink="/catalog">看全部庫存 →</a>
    </header>

    @if (loading()) {
      <p>載入中…</p>
    } @else if (items().length === 0) {
      <p class="showcase__empty">
        還沒有精選品項。到<a routerLink="/catalog">庫存</a>把喜歡的東西設為精選吧。
      </p>
    } @else {
      <div class="showcase__wall">
        @for (item of items(); track item.id) {
          <app-item-card [item]="item" />
        }
      </div>

      @if (items().length < total()) {
        <button type="button" (click)="loadMore()" [disabled]="loading()">載入更多</button>
      }
    }
  `,
  styles: `
    .showcase__header { display: flex; justify-content: space-between; align-items: baseline; }
    .showcase__wall { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 1rem; }
    .showcase__empty { color: #7f8c8d; }
  `,
})
export class ShowcaseComponent {
  private readonly catalog = inject(CatalogService);

  readonly items = signal<ItemDto[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);

  private page = 1;

  constructor() {
    this.load();
  }

  loadMore(): void {
    this.page += 1;
    this.load();
  }

  private load(): void {
    this.loading.set(true);

    this.catalog.showcase(this.page, 24).subscribe({
      next: (result) => {
        this.items.update((current) => [...current, ...result.items]);
        this.total.set(result.total);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
```

- [ ] **Step 4: 驗證建置與測試**

Run: `cd web && npm run build`
Expected: `Application bundle generation complete`

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: 全綠。7a 不新增測試（頁面元件的行為由 Task 8 的整合測試涵蓋），既有測試不可因路由變更而轉紅。

- [ ] **Step 5: Commit**

```bash
git add web/src/app/features web/src/app/app.routes.ts
git commit -m "feat(web): 新增路由表、登入頁與 Showcase 牆"
```

---

### Task 7b：庫存頁與品項檢視/編輯頁

**Files:**
- Create: `web/src/app/features/catalog/catalog.component.ts`
- Create: `web/src/app/features/item-detail/item-detail.component.ts`
- Modify: `web/src/app/app.routes.ts`（追加路由）

提醒：品項編輯頁會用到 `DynamicFormComponent`。`[value]` 只能綁「從後端載回來的品項屬性」這種初始值，**不可**綁隨 `(valueChange)` 更新的狀態，否則會無窮迴圈重建表單（原因見 Task 7 拆分說明）。

**`acquisition` 的讀寫形狀不一樣，轉換不可漏。** 後端刻意用了兩種結構：

| 方向 | 型別 | 形狀 |
|---|---|---|
| 讀（`GET /items/{id}`） | `AcquisitionDto`（`ItemDtos.cs:12`） | `{ acquiredAt, price: { amount, currency }, vendor }` |
| 寫（`POST`/`PUT /items`） | `AcquisitionInput`（`ItemCommands.cs:12`） | `{ acquiredAt, amount, currency, vendor }` |

編輯頁載入時拿到巢狀的 `price`，送出時必須攤平成 `amount` + `currency`。漏掉這層轉換的話 `amount` 會是 `undefined`，後端收到 null 就把價格清成空——**畫面上金額還在（那是載入時的舊值），存檔後才消失，而且沒有任何測試會抓到**。撰寫編輯頁的送出邏輯時，明確寫出 `amount: item.acquisition?.price?.amount` 這層取值，不要用展開運算子直接把 `AcquisitionDto` 丟進 payload。

- [ ] **Step 1: 庫存頁**

`web/src/app/features/catalog/catalog.component.ts`：

```ts
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CatalogService } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import { CategoryDto, ItemDto } from '../../core/models';
import { ItemCardComponent } from '../../shared/item-card/item-card.component';

@Component({
  selector: 'app-catalog',
  imports: [FormsModule, ItemCardComponent, RouterLink],
  template: `
    <div class="catalog">
      <aside class="catalog__filters">
        <label>搜尋<input type="search" [(ngModel)]="search" (ngModelChange)="reload()" /></label>

        <label>
          品類
          <select [(ngModel)]="categoryId" (ngModelChange)="reload()">
            <option value="">全部</option>
            @for (category of categories(); track category.id) {
              <option [value]="category.id">{{ category.name }}</option>
            }
          </select>
        </label>

        <fieldset>
          <legend>標籤</legend>
          @for (tag of allTags(); track tag) {
            <label class="catalog__tag">
              <input type="checkbox" [checked]="selectedTags().includes(tag)" (change)="toggleTag(tag)" />
              {{ tag }}
            </label>
          }
        </fieldset>
      </aside>

      <section class="catalog__results">
        <header>
          <span>{{ total() }} 件</span>
          <a routerLink="/items/new">新增品項</a>
        </header>

        <div class="catalog__grid">
          @for (item of items(); track item.id) {
            <app-item-card [item]="item" />
          }
        </div>

        @if (items().length < total()) {
          <button type="button" (click)="loadMore()">載入更多</button>
        }
      </section>
    </div>
  `,
  styles: `
    .catalog { display: grid; grid-template-columns: 16rem 1fr; gap: 1.5rem; align-items: start; }
    .catalog__filters { display: grid; gap: 0.75rem; position: sticky; top: 1rem; }
    .catalog__grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 1rem; }
    .catalog__tag { display: block; font-size: 0.85rem; }
    @media (max-width: 720px) { .catalog { grid-template-columns: 1fr; } }
  `,
})
export class CatalogComponent {
  private readonly catalog = inject(CatalogService);
  private readonly categoryApi = inject(CategoryService);

  readonly items = signal<ItemDto[]>([]);
  readonly total = signal(0);
  readonly categories = signal<CategoryDto[]>([]);
  readonly allTags = signal<string[]>([]);
  readonly selectedTags = signal<string[]>([]);

  search = '';
  categoryId = '';

  private page = 1;

  constructor() {
    this.categoryApi.list().subscribe((c) => this.categories.set(c));
    this.catalog.tags().subscribe((t) => this.allTags.set(t));
    this.load();
  }

  reload(): void {
    this.page = 1;
    this.items.set([]);
    this.load();
  }

  loadMore(): void {
    this.page += 1;
    this.load();
  }

  toggleTag(tag: string): void {
    this.selectedTags.update((tags) =>
      tags.includes(tag) ? tags.filter((t) => t !== tag) : [...tags, tag],
    );
    this.reload();
  }

  private load(): void {
    this.catalog
      .search({
        search: this.search || undefined,
        categoryId: this.categoryId || undefined,
        tags: this.selectedTags(),
        page: this.page,
        pageSize: 24,
      })
      .subscribe((result) => {
        this.items.update((current) => [...current, ...result.items]);
        this.total.set(result.total);
      });
  }
}
```

- [ ] **Step 2: 品項檢視/編輯頁**

`web/src/app/features/item-detail/item-detail.component.ts`：

```ts
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { CatalogService, ItemWritePayload } from '../../core/api/catalog.service';
import { CategoryService } from '../../core/api/category.service';
import { IngestionService } from '../../core/api/ingestion.service';
import { NotificationService } from '../../core/notification.service';
import { CategoryDto, ItemDto } from '../../core/models';
import { DynamicFormComponent } from '../../shared/dynamic-form/dynamic-form.component';
import { ImageUploaderComponent } from '../../shared/image-uploader/image-uploader.component';
import { TagInputComponent } from '../../shared/tag-input/tag-input.component';

@Component({
  selector: 'app-item-detail',
  imports: [FormsModule, DynamicFormComponent, ImageUploaderComponent, TagInputComponent],
  template: `
    <form class="detail" (ngSubmit)="save()">
      <header class="detail__header">
        <h1>{{ itemId() ? '編輯品項' : '新增品項' }}</h1>
        <div>
          <button type="submit" [disabled]="!canSave()">儲存</button>
          @if (itemId()) {
            <button type="button" (click)="remove()">刪除</button>
          }
        </div>
      </header>

      @if (!itemId()) {
        <fieldset class="detail__fetch">
          <legend>從商品網址自動填表</legend>
          <input type="url" [(ngModel)]="fetchUrl" name="fetchUrl" placeholder="https://…" />
          <button type="button" (click)="fetchMetadata()" [disabled]="!fetchUrl">擷取</button>
        </fieldset>
      }

      <label>
        品類
        <select [(ngModel)]="categoryId" name="categoryId" (ngModelChange)="onCategoryChanged()" required>
          <option value="">請選擇</option>
          @for (category of categories(); track category.id) {
            <option [value]="category.id">{{ category.name }}</option>
          }
        </select>
      </label>

      <label>名稱<input [(ngModel)]="name" name="name" required /></label>
      <label>描述<textarea [(ngModel)]="description" name="description" rows="3"></textarea></label>

      <label class="detail__checkbox">
        <input type="checkbox" [(ngModel)]="isShowcased" name="isShowcased" />
        設為精選（顯示在首頁牆面）
      </label>

      <app-tag-input [tags]="tags()" (tagsChange)="tags.set($event)" />

      @if (selectedCategory(); as category) {
        @if (category.fields.length) {
          <section>
            <h2>{{ category.name }} 專屬欄位</h2>
            <app-dynamic-form
              [fields]="category.fields"
              [value]="attributes()"
              (valueChange)="attributes.set($event)"
              (validityChange)="attributesValid.set($event)"
            />
          </section>
        }

        @if (category.kind === 'Physical') {
          <fieldset class="detail__acquisition">
            <legend>購入資訊</legend>
            <label>日期<input type="date" [(ngModel)]="acquiredAt" name="acquiredAt" /></label>
            <label>金額<input type="number" [(ngModel)]="price" name="price" /></label>
            <label>幣別<input [(ngModel)]="currency" name="currency" /></label>
            <label>通路<input [(ngModel)]="vendor" name="vendor" /></label>
          </fieldset>
        }
      }

      @if (itemId(); as id) {
        <section>
          <h2>圖片</h2>
          <app-image-uploader
            [images]="item()?.images ?? []"
            (upload)="uploadImages(id, $event)"
            (remove)="removeImage(id, $event)"
            (setPrimary)="setPrimaryImage(id, $event)"
          />
        </section>
      }
    </form>
  `,
  styles: `
    .detail { display: grid; gap: 1rem; max-width: 46rem; }
    .detail__header { display: flex; justify-content: space-between; align-items: center; }
    .detail label { display: grid; gap: 0.25rem; }
    .detail__checkbox { display: flex !important; gap: 0.5rem; align-items: center; }
    .detail__fetch { display: flex; gap: 0.5rem; align-items: center; }
    .detail__acquisition { display: grid; grid-template-columns: repeat(2, 1fr); gap: 0.5rem; }
  `,
})
export class ItemDetailComponent {
  private readonly catalog = inject(CatalogService);
  private readonly categoryApi = inject(CategoryService);
  private readonly ingestion = inject(IngestionService);
  private readonly notifications = inject(NotificationService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly itemId = signal<string | null>(this.route.snapshot.paramMap.get('id'));
  readonly item = signal<ItemDto | null>(null);
  readonly categories = signal<CategoryDto[]>([]);
  readonly selectedCategory = signal<CategoryDto | null>(null);
  readonly attributes = signal<Record<string, unknown>>({});
  readonly attributesValid = signal(true);
  readonly tags = signal<string[]>([]);

  categoryId = '';
  name = '';
  description = '';
  isShowcased = false;
  fetchUrl = '';
  acquiredAt = '';
  price: number | null = null;
  currency = 'TWD';
  vendor = '';

  constructor() {
    this.categoryApi.list().subscribe((categories) => {
      this.categories.set(categories);
      this.syncSelectedCategory();
    });

    const id = this.itemId();
    if (id) {
      this.catalog.get(id).subscribe((item) => this.hydrate(item));
    }
  }

  canSave(): boolean {
    return Boolean(this.categoryId) && this.name.trim().length > 0 && this.attributesValid();
  }

  onCategoryChanged(): void {
    this.syncSelectedCategory();
  }

  fetchMetadata(): void {
    this.ingestion.fetchByUrl(this.fetchUrl).subscribe((metadata) => {
      this.name = metadata.name;
      this.description = metadata.description ?? '';
      this.notifications.success('已從網址帶入資料，請確認後儲存。');
    });
  }

  save(): void {
    const payload = this.toPayload();
    const id = this.itemId();

    const request = id ? this.catalog.update(id, payload) : this.catalog.create(payload);

    request.subscribe((saved) => {
      this.notifications.success('已儲存。');
      if (!id) {
        void this.router.navigate(['/items', saved.id]);
      } else {
        this.hydrate(saved);
      }
    });
  }

  remove(): void {
    const id = this.itemId();
    if (!id) {
      return;
    }

    this.catalog.remove(id).subscribe(() => {
      this.notifications.success('已刪除。');
      void this.router.navigate(['/catalog']);
    });
  }

  uploadImages(itemId: string, files: File[]): void {
    for (const file of files) {
      this.catalog.uploadImage(itemId, file).subscribe(() => this.reloadItem(itemId));
    }
  }

  removeImage(itemId: string, imageId: string): void {
    this.catalog.deleteImage(itemId, imageId).subscribe(() => this.reloadItem(itemId));
  }

  setPrimaryImage(itemId: string, imageId: string): void {
    this.catalog.setPrimaryImage(itemId, imageId).subscribe(() => this.reloadItem(itemId));
  }

  private reloadItem(itemId: string): void {
    this.catalog.get(itemId).subscribe((item) => this.hydrate(item));
  }

  private hydrate(item: ItemDto): void {
    this.item.set(item);
    this.itemId.set(item.id);
    this.categoryId = item.categoryId;
    this.name = item.name;
    this.description = item.description ?? '';
    this.isShowcased = item.isShowcased;
    this.tags.set(item.tags);
    this.attributes.set(item.attributes);
    this.acquiredAt = item.acquisition?.acquiredAt?.slice(0, 10) ?? '';
    this.price = item.acquisition?.price?.amount ?? null;
    this.currency = item.acquisition?.price?.currency ?? 'TWD';
    this.vendor = item.acquisition?.vendor ?? '';
    this.syncSelectedCategory();
  }

  private syncSelectedCategory(): void {
    this.selectedCategory.set(this.categories().find((c) => c.id === this.categoryId) ?? null);
  }

  private toPayload(): ItemWritePayload {
    const hasAcquisition = Boolean(this.acquiredAt || this.price || this.vendor);

    return {
      categoryId: this.categoryId,
      name: this.name.trim(),
      description: this.description.trim() || null,
      tags: this.tags(),
      isShowcased: this.isShowcased,
      attributes: this.attributes(),
      acquisition: hasAcquisition
        ? {
            acquiredAt: this.acquiredAt ? new Date(`${this.acquiredAt}T00:00:00Z`).toISOString() : null,
            amount: this.price,
            currency: this.currency || 'TWD',
            vendor: this.vendor || null,
          }
        : null,
    };
  }
}
```

- [ ] **Step 3: 追加 7b 的路由**

`web/src/app/app.routes.ts` 的 `canActivate: [authGuard]` 那個節點的 `children` 陣列，在 Showcase 路由之後追加：

```ts
      {
        path: 'catalog',
        loadComponent: () =>
          import('./features/catalog/catalog.component').then((m) => m.CatalogComponent),
      },
      {
        path: 'items/new',
        loadComponent: () =>
          import('./features/item-detail/item-detail.component').then((m) => m.ItemDetailComponent),
      },
      {
        path: 'items/:id',
        loadComponent: () =>
          import('./features/item-detail/item-detail.component').then((m) => m.ItemDetailComponent),
      },
```

`items/new` 必須排在 `items/:id` **之前**。Angular router 是先到先匹配，順序顛倒的話 `/items/new` 會被 `:id` 吃掉，`new` 被當成 ObjectId 送去後端查詢。

- [ ] **Step 4: 驗證建置與測試**

Run: `cd web && npm run build`
Expected: `Application bundle generation complete`

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: 全綠，既有測試不可轉紅。

- [ ] **Step 5: Commit**

```bash
git add web/src/app/features web/src/app/app.routes.ts
git commit -m "feat(web): 新增庫存頁與品項檢視/編輯頁"
```

---

### Task 7c：品類編輯器、設定頁、公開分享頁與應用外殼

**Files:**
- Create: `web/src/app/features/categories/categories.component.ts`
- Create: `web/src/app/features/settings/settings.component.ts`
- Create: `web/src/app/features/public/public-share.component.ts`
- Modify: `web/src/app/app.routes.ts`（追加路由）、`web/src/app/app.ts`

應用外殼排在最後，是為了讓它導覽列上的每一個 `routerLink` 目標都已經存在。若提早做，連結會被 `{ path: '**', redirectTo: '' }` 全部導回首頁——建置是綠的，但點擊行為是錯的，而且沒有測試會抓到。

- [ ] **Step 1: 品類 schema 編輯器**

`web/src/app/features/categories/categories.component.ts`：

```ts
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CategoryService, CategoryWritePayload } from '../../core/api/category.service';
import { NotificationService } from '../../core/notification.service';
import { CategoryDto, CategoryFieldDto, FieldType } from '../../core/models';

const FIELD_TYPES: FieldType[] = ['Text', 'Number', 'Date', 'Select', 'Bool', 'Url'];

@Component({
  selector: 'app-categories',
  imports: [FormsModule],
  template: `
    <h1>品類</h1>

    <ul class="categories">
      @for (category of categories(); track category.id) {
        <li>
          <button type="button" (click)="edit(category)">{{ category.name }}</button>
          @if (category.isSystem) { <em>系統內建</em> }
        </li>
      }
    </ul>

    <button type="button" (click)="startNew()">新增品類</button>

    @if (draft(); as current) {
      <form class="editor" (ngSubmit)="save()">
        <h2>{{ editingId() ? '編輯品類' : '新增品類' }}</h2>

        <label>名稱<input [(ngModel)]="current.name" name="name" required /></label>
        <label>圖示<input [(ngModel)]="current.icon" name="icon" /></label>

        <label>
          類型
          <select [(ngModel)]="current.kind" name="kind">
            <option value="Physical">實體</option>
            <option value="Digital">數位</option>
          </select>
        </label>

        <h3>欄位</h3>
        @for (field of current.fields; track $index) {
          <fieldset class="editor__field">
            <input [(ngModel)]="field.key" [name]="'key' + $index" placeholder="key（camelCase）" required />
            <input [(ngModel)]="field.label" [name]="'label' + $index" placeholder="顯示名稱" required />

            <select [(ngModel)]="field.type" [name]="'type' + $index">
              @for (type of fieldTypes; track type) {
                <option [value]="type">{{ type }}</option>
              }
            </select>

            @if (field.type === 'Select') {
              <input
                [ngModel]="(field.options ?? []).join(',')"
                (ngModelChange)="setOptions(field, $event)"
                [name]="'options' + $index"
                placeholder="選項，以逗號分隔"
              />
            }

            <label><input type="checkbox" [(ngModel)]="field.required" [name]="'required' + $index" /> 必填</label>
            <label><input type="checkbox" [(ngModel)]="field.showOnCard" [name]="'card' + $index" /> 顯示於卡片</label>

            <button type="button" (click)="removeField($index)">移除</button>
          </fieldset>
        }

        <button type="button" (click)="addField()">新增欄位</button>

        <div class="editor__actions">
          <button type="submit">儲存</button>
          <button type="button" (click)="draft.set(null)">取消</button>
          @if (editingId()) {
            <button type="button" (click)="remove()">刪除品類</button>
          }
        </div>
      </form>
    }
  `,
  styles: `
    .categories { list-style: none; padding: 0; display: grid; gap: 0.35rem; }
    .editor { display: grid; gap: 0.75rem; max-width: 42rem; margin-top: 1.5rem; }
    .editor__field { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: center; }
    .editor__actions { display: flex; gap: 0.5rem; }
  `,
})
export class CategoriesComponent {
  private readonly api = inject(CategoryService);
  private readonly notifications = inject(NotificationService);

  readonly fieldTypes = FIELD_TYPES;
  readonly categories = signal<CategoryDto[]>([]);
  readonly draft = signal<CategoryWritePayload | null>(null);
  readonly editingId = signal<string | null>(null);

  constructor() {
    this.reload();
  }

  startNew(): void {
    this.editingId.set(null);
    this.draft.set({ name: '', icon: 'box', kind: 'Physical', fields: [] });
  }

  edit(category: CategoryDto): void {
    if (category.isSystem) {
      this.notifications.error('系統內建品類無法編輯。');
      return;
    }

    this.editingId.set(category.id);
    this.draft.set({
      name: category.name,
      icon: category.icon,
      kind: category.kind,
      fields: category.fields.map((f) => ({ ...f, options: f.options ? [...f.options] : null })),
    });
  }

  addField(): void {
    this.draft.update((current) =>
      current
        ? {
            ...current,
            fields: [
              ...current.fields,
              { key: '', label: '', type: 'Text', options: null, required: false, searchable: false, showOnCard: false },
            ],
          }
        : current,
    );
  }

  removeField(index: number): void {
    this.draft.update((current) =>
      current ? { ...current, fields: current.fields.filter((_, i) => i !== index) } : current,
    );
  }

  setOptions(field: CategoryFieldDto, raw: string): void {
    field.options = raw
      .split(',')
      .map((o) => o.trim())
      .filter((o) => o.length > 0);
  }

  save(): void {
    const payload = this.draft();
    if (!payload) {
      return;
    }

    const id = this.editingId();
    const request = id ? this.api.update(id, payload) : this.api.create(payload);

    request.subscribe(() => {
      this.notifications.success('已儲存品類。');
      this.draft.set(null);
      this.reload();
    });
  }

  remove(): void {
    const id = this.editingId();
    if (!id) {
      return;
    }

    this.api.remove(id).subscribe(() => {
      this.notifications.success('已刪除品類。');
      this.draft.set(null);
      this.reload();
    });
  }

  private reload(): void {
    this.api.list().subscribe((categories) => this.categories.set(categories));
  }
}
```

- [ ] **Step 2: 設定頁（Steam 綁定、同步紀錄、分享連結）**

`web/src/app/features/settings/settings.component.ts`：

```ts
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IngestionService } from '../../core/api/ingestion.service';
import { ShareService } from '../../core/api/share.service';
import { NotificationService } from '../../core/notification.service';
import { ExternalAccountDto, ShareLinkDto, SyncJobDto } from '../../core/models';

@Component({
  selector: 'app-settings',
  imports: [FormsModule],
  template: `
    <h1>設定</h1>

    <section>
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

    <section>
      <h2>同步紀錄</h2>
      <table>
        <thead>
          <tr><th>時間</th><th>來源</th><th>狀態</th><th>新增</th><th>更新</th><th>失敗</th></tr>
        </thead>
        <tbody>
          @for (job of jobs(); track job.id) {
            <tr>
              <td>{{ job.startedAt | date: 'yyyy-MM-dd HH:mm' }}</td>
              <td>{{ job.provider }}</td>
              <td [title]="job.error ?? ''">{{ job.status }}</td>
              <td>{{ job.created }}</td>
              <td>{{ job.updated }}</td>
              <td>{{ job.failed }}</td>
            </tr>
          } @empty {
            <tr><td colspan="6">尚無同步紀錄。</td></tr>
          }
        </tbody>
      </table>
    </section>

    <section>
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
    section { margin-block: 1.5rem; display: grid; gap: 0.5rem; justify-items: start; }
    table { border-collapse: collapse; width: 100%; }
    th, td { border-bottom: 1px solid #ecf0f1; padding: 0.35rem 0.5rem; text-align: left; }
    .hint { color: #7f8c8d; font-size: 0.85rem; }
    .settings__inline { display: flex; gap: 0.5rem; align-items: center; }
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
```

`SettingsComponent` 用到 `DatePipe`，在 `imports` 加入 `DatePipe`（`import { DatePipe } from '@angular/common';`，並把 `DatePipe` 加進 `imports` 陣列）。

- [ ] **Step 3: 公開分享頁**

`web/src/app/features/public/public-share.component.ts`：

```ts
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { API_BASE } from '../../core/api-base';
import { ShareService } from '../../core/api/share.service';
import { PublicShareDto } from '../../core/models';

@Component({
  selector: 'app-public-share',
  template: `
    @if (share(); as data) {
      <main class="public">
        <header>
          <h1>{{ data.ownerDisplayName }} 的收藏</h1>
          <p>{{ data.items.length }} 件</p>
        </header>

        <div class="public__wall">
          @for (item of data.items; track item.id) {
            <article class="public__card">
              @if (imageUrl(item.images); as url) {
                <img [src]="url" [alt]="item.name" loading="lazy" />
              }
              <h2>{{ item.name }}</h2>
              <small>{{ item.categoryName }}</small>
              @if (item.price; as price) {
                <strong>{{ price.amount }} {{ price.currency }}</strong>
              }
            </article>
          }
        </div>
      </main>
    } @else if (notFound()) {
      <main class="public"><p>找不到這個分享連結，可能已被刪除或過期。</p></main>
    }
  `,
  styles: `
    .public { max-width: 72rem; margin: 2rem auto; padding: 0 1rem; }
    .public__wall { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 1rem; }
    .public__card { display: grid; gap: 0.25rem; }
    .public__card img { width: 100%; aspect-ratio: 4 / 3; object-fit: cover; border-radius: 0.5rem; }
    .public__card h2 { font-size: 0.95rem; margin: 0; }
  `,
})
export class PublicShareComponent {
  private readonly api = inject(ShareService);
  private readonly route = inject(ActivatedRoute);

  readonly share = signal<PublicShareDto | null>(null);
  readonly notFound = signal(false);

  constructor() {
    const slug = this.route.snapshot.paramMap.get('slug')!;

    this.api.getPublic(slug).subscribe({
      next: (data) => this.share.set(data),
      error: () => this.notFound.set(true),
    });
  }

  imageUrl(images: PublicShareDto['items'][number]['images']): string | null {
    const primary = images.find((i) => i.isPrimary) ?? images[0];
    return primary ? `${API_BASE}/media/${primary.cardPath}` : null;
  }
}
```

- [ ] **Step 4: 追加 7c 的路由**

`web/src/app/app.routes.ts`：`children` 陣列追加 `categories` 與 `settings`，並在**頂層**（`login` 之後、`path: ''` 那個受 guard 保護的節點之前）加入公開分享頁：

```ts
  {
    path: 'p/:slug',
    loadComponent: () =>
      import('./features/public/public-share.component').then((m) => m.PublicShareComponent),
  },
```

```ts
      {
        path: 'categories',
        loadComponent: () =>
          import('./features/categories/categories.component').then((m) => m.CategoriesComponent),
      },
      {
        path: 'settings',
        loadComponent: () =>
          import('./features/settings/settings.component').then((m) => m.SettingsComponent),
      },
```

`p/:slug` 必須在頂層而非 `children` 內。放進 children 會套上 `authGuard`，未登入的訪客開分享連結會被踢去登入頁——分享功能就完全失效了，而且自己測試時多半已經登入，不會發現。

- [ ] **Step 5: 應用外殼**

`web/src/app/app.ts`（Angular 20 樣板檔名；若是 `app.component.ts` 則改該檔）：

```ts
import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';
import { NotificationService } from './core/notification.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    @if (auth.isAuthenticated()) {
      <nav class="nav">
        <a routerLink="/" routerLinkActive="nav--active" [routerLinkActiveOptions]="{ exact: true }">精選</a>
        <a routerLink="/catalog" routerLinkActive="nav--active">庫存</a>
        <a routerLink="/categories" routerLinkActive="nav--active">品類</a>
        <a routerLink="/settings" routerLinkActive="nav--active">設定</a>
        <button type="button" (click)="auth.logout()">登出</button>
      </nav>
    }

    <div class="toasts">
      @for (notification of notifications.notifications(); track notification.id) {
        <div class="toast" [class.toast--error]="notification.kind === 'error'">
          {{ notification.message }}
        </div>
      }
    </div>

    <main class="shell">
      <router-outlet />
    </main>
  `,
  styles: `
    .nav { display: flex; gap: 1rem; align-items: center; padding: 0.75rem 1rem; border-bottom: 1px solid #ecf0f1; }
    .nav--active { font-weight: 600; }
    .shell { max-width: 72rem; margin: 1.5rem auto; padding: 0 1rem; }
    .toasts { position: fixed; top: 1rem; right: 1rem; display: grid; gap: 0.5rem; z-index: 10; }
    .toast { padding: 0.6rem 0.9rem; border-radius: 0.5rem; background: #2ecc71; color: #fff; max-width: 22rem; white-space: pre-line; }
    .toast--error { background: #e74c3c; }
  `,
})
export class App {
  readonly auth = inject(AuthService);
  readonly notifications = inject(NotificationService);
}
```

- [ ] **Step 6: 驗證建置與測試**

Run: `cd web && npm run build && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: 建置成功、測試全過。

另外手動確認一次導覽列：外殼上的每個 `routerLink`（`/`、`/catalog`、`/categories`、`/settings`）都要真的到達對應頁面，而不是被 `**` 通配路由導回首頁。這一項沒有自動化測試涵蓋。

- [ ] **Step 7: Commit**

```bash
git add web
git commit -m "feat(web): 新增品類編輯器、設定頁、公開分享頁與應用外殼"
```

---

> **原本這裡是單一的 Task 8（12 個 Step）。已拆成 8a（後端）/ 8b（前端）。**
>
> 拆分理由：Step 1–5 是純 .NET（`dotnet test` 驗證），Step 6–12 是純 Angular（`npm test` 驗證），兩者的工具鏈、測試框架、驗證指令完全不同。合在一個 Task 裡，實作者要在兩套心智模型之間切換，而且中途沒有任何可 commit 的綠燈點——後端改完但前端還沒接上時，`SearchItemsQuery` 的簽章已經變了。拆開後 8a 結束時後端是完整且全綠的。

### Task 8a：後端支援依 schema 屬性篩選

spec §5.3 要求 `fields` 同時餵給**動態表單、動態驗證、篩選器 UI** 三處。前兩者已完成，Task 8a + 8b 補上第三處，並讓 `showOnCard` 真的影響卡片顯示。8a 負責後端查詢能力。

**注意：這是 Plan 5 唯一會動到 .NET 後端的前端相關 Task**（另一個是 Task 10，與前端無關）。做完之後後端就應該完全凍結，Task 1–7、8b、9 都不該再出現 `src/` 或 `tests/` 的異動。

**Files:**
- Modify: `src/MyCollection.Application/Items/IItemRepository.cs`（`ItemQuerySpec`）
- Modify: `src/MyCollection.Infrastructure/Mongo/MongoItemRepository.cs`
- Modify: `src/MyCollection.Application/Items/ItemQueries.cs`
- Modify: `src/MyCollection.Api/Endpoints/ItemEndpoints.cs`
- Test: `tests/MyCollection.Tests/Integration/MongoItemRepositoryTests.cs`（追加）

- [ ] **Step 1: 寫失敗的後端測試**

在 `tests/MyCollection.Tests/Integration/MongoItemRepositoryTests.cs` 類別內追加：

```csharp
    [Fact]
    public async Task SearchAsync_filters_by_attribute_values()
    {
        await fixture.Context.Items.InsertManyAsync(
        [
            NewItem(Owner, "GSC 公仔", FigureCategory),
            NewItem(Owner, "ALTER 公仔", FigureCategory)
        ]);
        await fixture.Context.Items.UpdateOneAsync(
            MongoDB.Driver.Builders<Item>.Filter.Eq(x => x.Name, "ALTER 公仔"),
            MongoDB.Driver.Builders<Item>.Update.Set("attributes.brand", "ALTER"));

        var result = await _sut.SearchAsync(
            new ItemQuerySpec { Attributes = new Dictionary<string, string> { ["brand"] = "ALTER" } },
            CancellationToken.None);

        result.Items.Should().ContainSingle().Which.Name.Should().Be("ALTER 公仔");
    }

    [Fact]
    public async Task SearchAsync_combines_attribute_filters_with_and()
    {
        await SeedAsync();

        var result = await _sut.SearchAsync(
            new ItemQuerySpec
            {
                Attributes = new Dictionary<string, string> { ["brand"] = "GSC", ["scale"] = "1/8" }
            },
            CancellationToken.None);

        result.Total.Should().Be(0, "沒有品項同時符合兩個屬性");
    }

    [Fact]
    public async Task SearchAsync_ignores_attribute_filters_with_blank_values()
    {
        await SeedAsync();

        var result = await _sut.SearchAsync(
            new ItemQuerySpec { Attributes = new Dictionary<string, string> { ["brand"] = "" } },
            CancellationToken.None);

        result.Total.Should().Be(3, "空值代表「不篩選」");
    }
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter MongoItemRepositoryTests`
Expected: 編譯失敗，`ItemQuerySpec` 沒有 `Attributes` 屬性。

- [ ] **Step 3: 擴充後端**

`src/MyCollection.Application/Items/IItemRepository.cs` 的 `ItemQuerySpec` 追加：

```csharp
    /// <summary>依 category schema 的 searchable 欄位篩選，key 為 field key、value 為精確比對值。</summary>
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }
```

`src/MyCollection.Infrastructure/Mongo/MongoItemRepository.cs` 的 `SearchAsync` 在 `if (!string.IsNullOrWhiteSpace(spec.Search))` 之前插入：

```csharp
        foreach (var (key, value) in spec.Attributes ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            // 欄位 key 已由 category schema 的 camelCase 規則約束，不會構成注入路徑
            filters.Add(Filter.Eq($"attributes.{key}", value));
        }
```

`src/MyCollection.Application/Items/ItemQueries.cs` 的 `SearchItemsQuery` 改為：

```csharp
public record SearchItemsQuery(
    string? Search = null,
    string? CategoryId = null,
    IReadOnlyList<string>? Tags = null,
    bool? IsShowcased = null,
    int Page = 1,
    int PageSize = 24,
    IReadOnlyDictionary<string, string>? Attributes = null) : IRequest<PagedResult<ItemDto>>;
```

`SearchItemsQueryHandler` 建立 `ItemQuerySpec` 時追加：

```csharp
            Attributes = request.Attributes,
```

- [ ] **Step 4: 端點解析 `attr.` 前綴**

`src/MyCollection.Api/Endpoints/ItemEndpoints.cs` 的 `GET /` 改為：

```csharp
        group.MapGet("/", async (HttpRequest request, ISender sender, CancellationToken ct) =>
        {
            var query = request.Query;

            // attr.brand=GSC → Attributes["brand"] = "GSC"
            var attributes = query
                .Where(kv => kv.Key.StartsWith("attr.", StringComparison.Ordinal) && kv.Key.Length > 5)
                .ToDictionary(kv => kv.Key[5..], kv => kv.Value.ToString());

            return Results.Ok(await sender.Send(new SearchItemsQuery(
                query["search"].FirstOrDefault(),
                query["categoryId"].FirstOrDefault(),
                query["tags"].ToArray()!,
                bool.TryParse(query["isShowcased"], out var showcased) ? showcased : null,
                int.TryParse(query["page"], out var page) ? page : 1,
                int.TryParse(query["pageSize"], out var pageSize) ? pageSize : 24,
                attributes), ct));
        });
```

- [ ] **Step 5: 跑後端測試確認通過**

Run: `dotnet test --filter "MongoItemRepositoryTests|CatalogEndpointsTests"`
Expected: `Failed: 0`

Run: `dotnet test`
Expected: 全綠、0 失敗。`SearchItemsQuery` 的簽章變了，所有呼叫端都必須跟著編譯過。

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(api): 品項查詢支援依 schema 屬性篩選"
```

---

### Task 8b：前端屬性篩選 UI 與卡片欄位

**Files:**
- Modify: `web/src/app/core/api/catalog.service.ts`
- Modify: `web/src/app/features/catalog/catalog.component.ts`
- Modify: `web/src/app/shared/item-card/item-card.component.ts`
- Test: `web/src/app/shared/item-card/item-card.component.spec.ts`（追加）

前置：Task 8a 已完成，後端 `GET /items` 已能解析 `attr.{key}={value}` 查詢參數。

- [ ] **Step 1: 寫失敗的前端測試**

在 `web/src/app/shared/item-card/item-card.component.spec.ts` 的 `describe` 內追加：

```ts
  it('renders only the attributes marked showOnCard', () => {
    fixture.componentRef.setInput('item', item({ attributes: { brand: 'GSC', scale: '1/8' } }));
    fixture.componentRef.setInput('cardFields', [
      { key: 'brand', label: '廠商', type: 'Text', options: null, required: false, searchable: true, showOnCard: true },
      { key: 'scale', label: '比例', type: 'Text', options: null, required: false, searchable: false, showOnCard: false },
    ]);
    fixture.detectChanges();

    const text = fixture.nativeElement.querySelector('[data-card-fields]').textContent;
    expect(text).toContain('GSC');
    expect(text).not.toContain('1/8');
  });

  it('renders no attribute row when no field is marked showOnCard', () => {
    fixture.componentRef.setInput('item', item({ attributes: { brand: 'GSC' } }));
    fixture.componentRef.setInput('cardFields', []);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-card-fields]')).toBeNull();
  });
```

- [ ] **Step 2: 跑前端測試確認失敗**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: 2 筆新測試 FAIL（`cardFields` input 不存在）。

- [ ] **Step 3: 擴充 ItemCard**

`web/src/app/shared/item-card/item-card.component.ts`：

- 頂端 import 追加 `CategoryFieldDto`：

```ts
import { CategoryFieldDto, ItemDto } from '../../core/models';
```

- 類別內追加：

```ts
  /** 所屬品類的 fields。只有 showOnCard 的欄位會出現在卡片上。 */
  readonly cardFields = input<CategoryFieldDto[]>([]);

  readonly cardAttributes = computed(() =>
    this.cardFields()
      .filter((f) => f.showOnCard)
      .map((f) => ({ label: f.label, value: this.item().attributes[f.key] }))
      .filter((entry) => entry.value !== null && entry.value !== undefined && entry.value !== ''),
  );
```

- template 的 `card__tags` 區塊之前插入：

```html
        @if (cardAttributes().length) {
          <dl class="card__fields" data-card-fields>
            @for (entry of cardAttributes(); track entry.label) {
              <dt>{{ entry.label }}</dt>
              <dd>{{ entry.value }}</dd>
            }
          </dl>
        }
```

- styles 追加：

```css
    .card__fields { display: grid; grid-template-columns: auto 1fr; gap: 0 0.4rem;
                    margin: 0; font-size: 0.75rem; color: #7f8c8d; }
    .card__fields dt { font-weight: 600; }
    .card__fields dd { margin: 0; }
```

- [ ] **Step 4: 前端服務支援屬性篩選**

`web/src/app/core/api/catalog.service.ts` 的 `ItemSearchOptions` 追加：

```ts
  attributes?: Record<string, string>;
```

`search()` 內、`return` 之前追加：

```ts
    for (const [key, value] of Object.entries(options.attributes ?? {})) {
      if (value) {
        params = params.set(`attr.${key}`, value);
      }
    }
```

- [ ] **Step 5: 篩選側欄依 schema 動態產生**

`web/src/app/features/catalog/catalog.component.ts`：

- imports 追加 `CategoryFieldDto`：

```ts
import { CategoryDto, CategoryFieldDto, ItemDto } from '../../core/models';
```

- template 的標籤 `<fieldset>` 之前插入：

```html
        @for (field of searchableFields(); track field.key) {
          <label>
            {{ field.label }}
            @if (field.type === 'Select') {
              <select [ngModel]="attributeFilters()[field.key] ?? ''"
                      (ngModelChange)="setAttributeFilter(field.key, $event)"
                      [name]="'attr_' + field.key">
                <option value="">全部</option>
                @for (option of field.options ?? []; track option) {
                  <option [value]="option">{{ option }}</option>
                }
              </select>
            } @else {
              <input type="text"
                     [ngModel]="attributeFilters()[field.key] ?? ''"
                     (ngModelChange)="setAttributeFilter(field.key, $event)"
                     [name]="'attr_' + field.key" />
            }
          </label>
        }
```

- template 的 `<app-item-card [item]="item" />` 改為：

```html
            <app-item-card [item]="item" [cardFields]="fieldsFor(item.categoryId)" />
```

- 類別內追加：

```ts
  readonly attributeFilters = signal<Record<string, string>>({});

  /** 只有選定品類時才有屬性篩選——不同品類的 schema 無法混用。 */
  readonly searchableFields = computed<CategoryFieldDto[]>(() => {
    const category = this.categories().find((c) => c.id === this.categoryId);
    return category?.fields.filter((f) => f.searchable) ?? [];
  });

  fieldsFor(categoryId: string): CategoryFieldDto[] {
    return this.categories().find((c) => c.id === categoryId)?.fields ?? [];
  }

  setAttributeFilter(key: string, value: string): void {
    this.attributeFilters.update((current) => ({ ...current, [key]: value }));
    this.reload();
  }
```

- `reload()` 改為同時清掉不再適用的屬性篩選：

```ts
  reload(): void {
    const allowed = new Set(this.searchableFields().map((f) => f.key));
    this.attributeFilters.update((current) =>
      Object.fromEntries(Object.entries(current).filter(([key]) => allowed.has(key))),
    );

    this.page = 1;
    this.items.set([]);
    this.load();
  }
```

- `load()` 的 `search({...})` 物件追加：

```ts
        attributes: this.attributeFilters(),
```

- 類別頂端 import 追加 `computed`：

```ts
import { Component, computed, inject, signal } from '@angular/core';
```

- [ ] **Step 6: 跑測試確認通過**

Run: `cd web && npm run build && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: 建置成功、`ItemCardComponent` 7 筆全過。

- [ ] **Step 7: Commit**

```bash
git add web
git commit -m "feat(web): schema 的 searchable 與 showOnCard 驅動篩選器與卡片欄位"
```

只 `git add web`。後端的部分在 Task 8a 已經 commit 過了，這裡若寫成 `git add src tests` 會把不相干的殘留一起帶進來。

---

### Task 9：Docker 化與 Compose

**Files:**
- Create: `src/MyCollection.Api/Dockerfile`
- Create: `web/Dockerfile`、`web/nginx.conf`
- Create: `docker-compose.yml`、`.env.example`
- Create: `.dockerignore`

- [ ] **Step 1: `.dockerignore`**

`.dockerignore`：

```
**/bin
**/obj
**/node_modules
**/dist
data
.git
docs
```

- [ ] **Step 2: API Dockerfile**

`src/MyCollection.Api/Dockerfile`：

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY MyCollection.slnx ./
COPY src/MyCollection.Domain/*.csproj src/MyCollection.Domain/
COPY src/MyCollection.Application/*.csproj src/MyCollection.Application/
COPY src/MyCollection.Infrastructure/*.csproj src/MyCollection.Infrastructure/
COPY src/MyCollection.Api/*.csproj src/MyCollection.Api/
RUN dotnet restore src/MyCollection.Api/MyCollection.Api.csproj

COPY src/ src/
RUN dotnet publish src/MyCollection.Api/MyCollection.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MyCollection.Api.dll"]
```

`docker build` 的 context 是 repo 根目錄，因此 compose 需指定 `dockerfile: src/MyCollection.Api/Dockerfile`。

- [ ] **Step 3: Web Dockerfile 與 nginx 設定**

`web/nginx.conf`：

```nginx
server {
    listen 80;
    server_name _;

    root /usr/share/nginx/html;
    index index.html;

    client_max_body_size 12m;

    # 反代到 API 容器；剝掉 /api 前綴，後端路由是 /items 而非 /api/items
    #
    # `^~` 不可省略。nginx 的比對順序是「exact = → ^~ 前綴 → regex → 一般前綴」，
    # 沒有 ^~ 的話下面那條副檔名 regex 會贏過這條前綴規則：圖片網址是
    # /api/media/{id}/card.webp（見 MediaEndpoints 的 GET /media/{**path}），
    # 結尾是 .webp 就被 regex 接走，nginx 改去磁碟找 html/api/media/... 而 404。
    # 症狀是「網站正常但所有圖片掛掉」，且只在 Docker 部署下出現，ng serve 不會重現。
    location ^~ /api/ {
        proxy_pass         http://api:8080/;
        proxy_http_version 1.1;
        proxy_set_header   Host $host;
        proxy_set_header   X-Real-IP $remote_addr;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
    }

    # Angular 靜態資源可長期快取（檔名含 hash）
    location ~* \.(js|css|woff2|webp|png|jpg|svg)$ {
        expires 1y;
        add_header Cache-Control "public, immutable";
    }

    # SPA fallback
    location / {
        try_files $uri $uri/ /index.html;
    }
}
```

`web/Dockerfile`：

```dockerfile
FROM node:24-alpine AS build
WORKDIR /src

COPY web/package*.json ./
RUN npm ci

COPY web/ ./
RUN npm run build

FROM nginx:alpine AS runtime
COPY web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist/web/browser /usr/share/nginx/html

EXPOSE 80
```

若 `ng build` 的輸出路徑不是 `dist/web/browser`，以 `web/angular.json` 的 `outputPath` 為準調整最後一行。

- [ ] **Step 4: Compose**

`docker-compose.yml`：

```yaml
services:
  mongo:
    image: mongo:8.0
    restart: unless-stopped
    volumes:
      - ./data/mongo:/data/db
    healthcheck:
      test: ["CMD", "mongosh", "--quiet", "--eval", "db.adminCommand('ping')"]
      interval: 10s
      timeout: 5s
      retries: 5

  api:
    build:
      context: .
      dockerfile: src/MyCollection.Api/Dockerfile
    restart: unless-stopped
    depends_on:
      mongo:
        condition: service_healthy
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      Mongo__ConnectionString: mongodb://mongo:27017
      Mongo__Database: mycollection
      Jwt__Key: ${JWT_KEY:?JWT_KEY is required}
      Jwt__Issuer: mycollection
      Jwt__Audience: mycollection-web
      SecretProtection__Key: ${SECRET_PROTECTION_KEY:?SECRET_PROTECTION_KEY is required}
      Storage__Provider: Local
      Storage__LocalRoot: /app/data/media
    volumes:
      - ./data/media:/app/data/media

  web:
    build:
      context: .
      dockerfile: web/Dockerfile
    restart: unless-stopped
    depends_on:
      - api
    ports:
      - "8080:80"
```

`.env.example`：

```dotenv
# 兩把金鑰都必須是 Base64 編碼的 32-byte 隨機值
# PowerShell: [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 }))
# bash:       openssl rand -base64 32
JWT_KEY=
SECRET_PROTECTION_KEY=
```

`Jwt__Key` 是 HMAC 簽章金鑰（任意足夠長的字串即可），`SECRET_PROTECTION_KEY` 必須是能解碼成 32 bytes 的 Base64，否則 API 啟動即失敗。

- [ ] **Step 5: 驗證整套啟動**

```bash
cp .env.example .env
# 填入兩把金鑰後
docker compose build
docker compose up -d
```

Run: `curl http://localhost:8080/api/health`
Expected: `{"status":"ok"}`

Run: 瀏覽器開 `http://localhost:8080`
Expected: 顯示登入/註冊頁。

- [ ] **Step 6: Commit**

```bash
git add Dockerfile* docker-compose.yml .env.example .dockerignore src/MyCollection.Api/Dockerfile web/Dockerfile web/nginx.conf
git commit -m "chore: 新增 Docker 化與 docker-compose 部署"
```

---

### Task 10：封住登入的時序側信道（Plan 1 遺留）

**背景：** Plan 1 Task 10 的 `LoginCommandHandler` 對「帳號不存在」與「密碼錯誤」回傳相同訊息 `"Invalid email or password."`，防的是訊息內容洩漏。但**回應時間**仍會洩漏：帳號不存在時 `user is null` 短路，完全不跑 PBKDF2；帳號存在但密碼錯時要跑滿 210,000 次迭代（實測約 20ms）。攻擊者拿一份 email 清單各打一次登入，用回應時間就能篩出哪些已註冊，訊息一致的防護等於白做。

**修法：** `user is null` 時仍對一組固定的假雜湊跑一次 `Verify`，讓兩條路徑都付出相同的 PBKDF2 成本。假雜湊在靜態欄位算一次即可（型別初始化時，不影響每次請求）。

**Files:**
- Modify: `src/MyCollection.Application/Auth/LoginCommand.cs`
- Modify: `tests/MyCollection.Tests/Unit/LoginCommandTests.cs`

- [ ] **Step 1: 寫失敗測試**

在 `tests/MyCollection.Tests/Unit/LoginCommandTests.cs` 的類別內加入：

```csharp
    [Fact]
    public async Task Unknown_email_still_performs_password_verification()
    {
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var act = () => CreateSut().Handle(new LoginCommand("nobody@example.com", "x"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();

        // 帳號不存在時若跳過 Verify，回應時間會比密碼錯誤短約一個 PBKDF2 的成本（實測約 20ms），
        // 攻擊者據此即可列舉已註冊的 email。兩條路徑必須都付出相同成本。
        _hasher.Verify(h => h.Verify(It.IsAny<string>(), "x"), Times.Once);
    }
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter LoginCommandTests`
Expected: `Unknown_email_still_performs_password_verification` 失敗，訊息為 Moq 回報 `Verify` 預期呼叫 1 次但實際 0 次。其餘 3 個測試仍通過。

- [ ] **Step 3: 實作**

`src/MyCollection.Application/Auth/LoginCommand.cs` 的 `LoginCommandHandler` 改為：

```csharp
public sealed class LoginCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    TimeProvider timeProvider) : IRequestHandler<LoginCommand, AuthResponse>
{
    private const string InvalidCredentials = "Invalid email or password.";

    /// <summary>
    /// 帳號不存在時拿來墊檔的雜湊。只在型別初始化時算一次，不影響每次請求的成本。
    /// 目的是讓「帳號不存在」與「密碼錯誤」兩條路徑跑一樣多的 PBKDF2 迭代，
    /// 否則回應時間差（約 20ms）會直接洩漏該 email 是否已註冊。
    /// </summary>
    private static readonly string DummyHash =
        "pbkdf2.210000.AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByEmailAsync(request.Email, cancellationToken);

        // 即使帳號不存在也跑一次驗證，兩條路徑的耗時才一致（見 DummyHash 註解）
        var passwordMatches = passwordHasher.Verify(user?.PasswordHash ?? DummyHash, request.Password);

        // 帳號不存在與密碼錯誤回傳相同訊息，避免帳號列舉
        if (user is null || !passwordMatches)
        {
            throw new ForbiddenException(InvalidCredentials);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var refreshToken = tokenService.CreateRefreshToken();

        await users.SetRefreshTokenAsync(
            user.Id,
            tokenService.HashRefreshToken(refreshToken),
            now.Add(tokenService.RefreshTokenLifetime),
            cancellationToken);

        return new AuthResponse(
            tokenService.CreateAccessToken(user),
            refreshToken,
            now.Add(tokenService.AccessTokenLifetime),
            new UserDto(user.Id.ToString(), user.Email, user.DisplayName));
    }
}
```

`DummyHash` 的 base64 內容是全零，格式合法（`Pbkdf2PasswordHasher.Verify` 會解析成功並實際跑滿 210,000 次迭代），但永遠不會與真實密碼相符。**不要**改成 `"invalid"` 之類的字串——`Verify` 會在格式檢查階段就 `return false`，等於沒跑 PBKDF2，這個 Task 就白做了。

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter LoginCommandTests`
Expected: 通過 4。

Run: `dotnet test`
Expected: 全綠、0 失敗、0 警告。

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "fix(auth): 封住登入的帳號列舉時序側信道"
```

---

## 驗收（對應 spec §12）

- [ ] `dotnet test` 全綠，含 Testcontainers 整合測試
- [ ] `cd web && npm run build && npm test -- --watch=false --browsers=ChromeHeadless` 全綠
- [ ] `docker compose up` 後：註冊帳號 → 建立自訂品類 → 手動新增一隻公仔（含圖片）→ 貼商品 URL 驗證 OpenGraph 自動填表
- [ ] 綁定真實 Steam API Key + SteamID → 觸發同步 → 品項數量正確、全部 `isShowcased: false`
- [ ] 將 3 款遊戲與 1 隻公仔設為 Showcase → 首頁牆面正確混合顯示 → Showcase 遊戲的圖片已下載到本地
- [ ] 再次觸發同步 → 設定頁同步紀錄顯示 `created: 0`，Showcase 旗標與標籤未被覆蓋
- [ ] 建立分享連結 → 無痕視窗開 `/p/{slug}` → 只看得到 Showcase 品項，回應 payload 不含 `acquisition`

## 明確不做（YAGNI，對應 spec §11）

位置階層 UI · 估值曲線與匯率 · 保固到期提醒 · PSN 整合 · Discogs/IGDB · CSV 匯入匯出 · 多人共享 group · 行動 App · 虛擬捲動（第一版用「載入更多」，資料量到達數千筆再導入）
