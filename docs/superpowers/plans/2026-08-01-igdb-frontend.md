# IGDB 前端整合實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓使用者能在新增遊戲品項時搜尋 IGDB 帶入資料，並對既有品項批次或單筆補完 IGDB 欄位。

**Architecture:** 三個新的前端單元——`ProviderService`（IGDB 是否可用）、`IgdbSearchDialogComponent`（原生 `<dialog>` 搜尋挑選）、`IgdbEnrichComponent`（設定頁批次補完）。對話框只負責「搜尋、讓使用者挑、吐出選中的 DTO」，套用語意留在 `ItemDetailComponent`。另有一行後端改動讓 IGDB 封面能被下載成本地圖片。

**Tech Stack:** Angular 20.3（signals、standalone components、`@if`/`@for` 控制流）、RxJS、Karma + Jasmine；後端 .NET 10 + xUnit + FluentAssertions。

設計文件：`docs/superpowers/specs/2026-08-01-igdb-frontend-design.md`
後端計畫（已完成）：`docs/superpowers/plans/2026-08-01-igdb-metadata-backend.md`

---

## 執行前必讀

### 環境

- **路徑**：`f:\VibeCode\MyCollection`
- **分支**：`mongoAtlas`（不是 master，可直接 commit）
- **前端測試**：`cd web && npm test -- --watch=false --browsers=ChromeHeadless`
- **單檔測試**：上述指令加 `--include=src/app/path/to/file.spec.ts`
- **後端測試**：`dotnet test`（從 repo 根目錄）

### 基準線

```
前端  73 passed / 0 failed
後端  447 passed / 0 failed
dotnet build  0 warnings / 0 errors
```

**任何時候數字低於基準線或出現失敗，就是弄壞了東西。**

### 絕對不要碰的檔案

`web/angular.json` 有一個與本任務無關的未提交變更（Angular CLI 自動寫入的 analytics UUID）。
**不要 stage、不要 commit、不要還原它。** 每個 Task 的 `git add` 都列出明確路徑，不要用 `git add .`。

### 慣例

- 程式碼註解用繁體中文（台灣），commit message 用英文
- 元件用 standalone + signals；模板用 `@if` / `@for`，不用 `*ngIf` / `*ngFor`
- HTTP 錯誤一律由 `errorInterceptor` 顯示，元件用 `IGNORE_HANDLED_BY_INTERCEPTOR` 吞掉、只在 `finalize` 解鎖按鈕
- 測試依層分工：**服務**用 `HttpTestingController`，**元件**用假服務（`useValue`）
- TDD 順序不可跳：寫失敗測試 → 跑一次確認失敗且原因正確 → 最小實作 → 跑測試確認通過 → commit
- **一個 Task 一個 commit**

---

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `web/src/app/core/api/provider.service.ts` | 抓一次 `/ingest/providers`，回答「某 provider 是否具備某能力」。`IGDB_PROVIDER_KEY` 常數的家 |
| `web/src/app/core/api/provider.service.spec.ts` | 上者的測試 |
| `web/src/app/core/api/ingestion.service.spec.ts` | `IngestionService` 新方法的 URL 與參數契約測試 |
| `web/src/app/shared/igdb-search-dialog/igdb-search-dialog.component.ts` | 對話框：輸入關鍵字、搜尋、封面網格、`(select)` 吐出 DTO |
| `web/src/app/shared/igdb-search-dialog/igdb-search-dialog.component.spec.ts` | 上者的測試 |
| `web/src/app/features/settings/igdb-enrich.component.ts` | 設定頁的批次補完面板 |
| `web/src/app/features/settings/igdb-enrich.component.spec.ts` | 上者的測試 |
| `tests/MyCollection.Tests/Unit/ShowcaseImageDownloaderTests.cs` | `ResolveSourceUrl` 的來源優先序 |

### 修改

| 檔案 | 改動 |
|---|---|
| `web/src/app/core/models.ts` | `SyncJobDto.skipped`；新增 `ProviderDto` |
| `web/src/app/core/api/ingestion.service.ts` | `providers()`、`search()`、`enrich()` |
| `web/src/app/features/item-detail/item-detail.component.ts` | 掛對話框、統一套用路徑、既有品項的狀態相依按鈕 |
| `web/src/app/features/item-detail/item-detail.component.spec.ts` | 既有 6 個測試補 `ProviderService` stub；新增 5 個測試 |
| `web/src/app/features/settings/settings.component.ts` | 紀錄表加「略過」欄、掛 `<app-igdb-enrich>`、`reloadJobs` 改 `protected` |
| `web/src/app/features/settings/settings.component.spec.ts` | 既有測試補 `ProviderService` stub；新增「略過」欄測試 |
| `src/MyCollection.Infrastructure/Imaging/ShowcaseImageDownloader.cs` | 來源候選加 `coverUrl`；`ResolveSourceUrl` 改 `public` |

### Task 相依順序

```
Task 1（型別與服務）→ Task 2（ProviderService）→ Task 3（對話框）
                                              ↘ Task 4（新增品項）→ Task 5（既有品項）
                                              ↘ Task 6（補完面板）→ Task 7（設定頁）
Task 8（後端 coverUrl）獨立，任何時候都能做
```

---

### Task 1：型別與 IngestionService 的三個新方法

**Files:**
- Create: `web/src/app/core/api/ingestion.service.spec.ts`
- Modify: `web/src/app/core/models.ts`
- Modify: `web/src/app/core/api/ingestion.service.ts`

查詢參數名稱寫錯後端會回 400，而元件測試餵的是假服務、抓不到。這三個名字是與後端的實際契約，值得逐一釘住。

- [ ] **Step 1: 寫失敗測試**

`web/src/app/core/api/ingestion.service.spec.ts`：

```ts
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { SyncJobDto } from '../models';
import { IngestionService } from './ingestion.service';

describe('IngestionService', () => {
  let service: IngestionService;
  let http: HttpTestingController;

  const job: SyncJobDto = {
    id: 'j1',
    provider: 'igdb',
    status: 'Succeeded',
    created: 0,
    updated: 1,
    failed: 0,
    skipped: 2,
    error: null,
    startedAt: '2026-08-01T03:00:00Z',
    finishedAt: '2026-08-01T03:00:05Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(IngestionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists the registered providers', async () => {
    const pending = firstValueFrom(service.providers());

    const request = http.expectOne('/api/ingest/providers');
    expect(request.request.method).toBe('GET');
    request.flush([{ key: 'igdb', capabilities: 'Search' }]);

    expect((await pending)[0].key).toBe('igdb');
  });

  it('sends provider, q and limit as query parameters when searching', async () => {
    const pending = firstValueFrom(service.search('igdb', 'the witcher 3'));

    const request = http.expectOne((r) => r.url === '/api/ingest/search');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('provider')).toBe('igdb');
    expect(request.request.params.get('q')).toBe('the witcher 3');
    expect(request.request.params.get('limit')).toBe('20');
    request.flush([]);

    expect(await pending).toEqual([]);
  });

  it('posts itemIds to the provider-scoped enrich route', async () => {
    const pending = firstValueFrom(service.enrich('igdb', ['65b0000000000000000000aa']));

    const request = http.expectOne('/api/ingest/enrich/igdb');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ itemIds: ['65b0000000000000000000aa'] });
    request.flush(job);

    expect((await pending).skipped).toBe(2);
  });

  /** 批次模式不送 itemIds，後端才會套用它自己的 limit 預設值（50）。 */
  it('sends a null itemIds for a batch run', async () => {
    const pending = firstValueFrom(service.enrich('igdb'));

    const request = http.expectOne('/api/ingest/enrich/igdb');
    expect(request.request.body).toEqual({ itemIds: null });
    request.flush(job);

    await pending;
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/core/api/ingestion.service.spec.ts`

Expected: 編譯失敗。`SyncJobDto` 沒有 `skipped`，`IngestionService` 沒有 `providers` / `search` / `enrich`。

- [ ] **Step 3: 加型別**

`web/src/app/core/models.ts`，在 `SyncJobDto` 的 `failed` 之後插入一行：

```ts
export interface SyncJobDto {
  id: string;
  provider: string;
  status: 'Running' | 'Succeeded' | 'Failed';
  created: number;
  updated: number;
  failed: number;
  /** 正常但未處理的筆數，例如外部來源查無對應。與 failed 語意不同。 */
  skipped: number;
  error: string | null;
  startedAt: string;
  finishedAt: string | null;
}
```

在同一檔案的 `ExternalAccountDto` 之後加入：

```ts
export interface ProviderDto {
  key: string;
  /** 逗號分隔的能力旗標，例如 "BulkSync, UrlLookup" 或 "Search"。 */
  capabilities: string;
}
```

- [ ] **Step 4: 加三個方法**

`web/src/app/core/api/ingestion.service.ts`：

檔頭的 import 改成：

```ts
import { ExternalAccountDto, FetchedMetadataDto, ProviderDto, SyncJobDto } from '../models';
```

在 `fetchByUrl` 之後、類別結尾之前加入：

```ts
  providers(): Observable<ProviderDto[]> {
    return this.http.get<ProviderDto[]>(`${API_BASE}/ingest/providers`);
  }

  search(provider: string, query: string, limit = 20): Observable<FetchedMetadataDto[]> {
    return this.http.get<FetchedMetadataDto[]>(`${API_BASE}/ingest/search`, {
      params: new HttpParams().set('provider', provider).set('q', query).set('limit', limit),
    });
  }

  /**
   * 不給 itemIds 是批次模式。body 一定要送 itemIds 這個 key（值為 null），
   * 後端的 EnrichRequest 是 record，缺 key 與 null 都會得到 null，但明確送出讓契約在測試裡看得見。
   */
  enrich(provider: string, itemIds?: string[]): Observable<SyncJobDto> {
    return this.http.post<SyncJobDto>(`${API_BASE}/ingest/enrich/${provider}`, {
      itemIds: itemIds ?? null,
    });
  }
```

- [ ] **Step 5: 跑測試確認通過**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/core/api/ingestion.service.spec.ts`
Expected: `TOTAL: 5 SUCCESS`

- [ ] **Step 6: 跑全部測試**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: `TOTAL: 78 SUCCESS`（73 + 5）

- [ ] **Step 7: Commit**

```bash
git add web/src/app/core/models.ts web/src/app/core/api/ingestion.service.ts web/src/app/core/api/ingestion.service.spec.ts
git commit -m "feat(web): add provider discovery, search and enrich api methods"
```

---

### Task 2：ProviderService

**Files:**
- Create: `web/src/app/core/api/provider.service.ts`
- Create: `web/src/app/core/api/provider.service.spec.ts`

IGDB 未設定時後端不會註冊它，`/ingest/providers` 就不會列出。這個服務把那個事實變成一個全站共用的布林值。

- [ ] **Step 1: 寫失敗測試**

`web/src/app/core/api/provider.service.spec.ts`：

```ts
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ProviderDto } from '../models';
import { IGDB_PROVIDER_KEY, ProviderService } from './provider.service';

describe('ProviderService', () => {
  let http: HttpTestingController;

  function createWith(providers: ProviderDto[]): ProviderService {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    http = TestBed.inject(HttpTestingController);
    const service = TestBed.inject(ProviderService);

    http.expectOne('/api/ingest/providers').flush(providers);

    return service;
  }

  afterEach(() => http.verify());

  it('reports a capability the provider declares', () => {
    const service = createWith([{ key: 'igdb', capabilities: 'Search' }]);

    expect(service.supports(IGDB_PROVIDER_KEY, 'Search')).toBeTrue();
  });

  /** 後端回的是 [Flags] 的 ToString()，多重能力長這樣："BulkSync, UrlLookup"。 */
  it('parses combined capability flags', () => {
    const service = createWith([{ key: 'steam', capabilities: 'BulkSync, UrlLookup' }]);

    expect(service.supports('steam', 'UrlLookup')).toBeTrue();
    expect(service.supports('steam', 'Search')).toBeFalse();
  });

  it('reports false for a provider that is not registered', () => {
    const service = createWith([{ key: 'steam', capabilities: 'BulkSync' }]);

    expect(service.supports(IGDB_PROVIDER_KEY, 'Search')).toBeFalse();
  });

  /**
   * 這是啟動時的背景請求。失敗不該在使用者還沒做任何事之前就跳一則錯誤，
   * 也不該讓任何呼叫端拿到例外——退化成「沒有任何 provider」即可。
   */
  it('degrades to no providers when the request fails', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    http = TestBed.inject(HttpTestingController);
    const service = TestBed.inject(ProviderService);

    http.expectOne('/api/ingest/providers')
      .flush(null, { status: 502, statusText: 'Bad Gateway' });

    expect(service.supports(IGDB_PROVIDER_KEY, 'Search')).toBeFalse();
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/core/api/provider.service.spec.ts`
Expected: 編譯失敗，找不到 `./provider.service`。

- [ ] **Step 3: 實作**

`web/src/app/core/api/provider.service.ts`：

```ts
import { Injectable, inject, signal } from '@angular/core';
import { catchError, of } from 'rxjs';
import { ProviderDto } from '../models';
import { IngestionService } from './ingestion.service';

/** 後端 ProviderKeys.Igdb 的對應值。不要在別處寫字面值。 */
export const IGDB_PROVIDER_KEY = 'igdb';

@Injectable({ providedIn: 'root' })
export class ProviderService {
  private readonly ingestion = inject(IngestionService);

  /**
   * 初值為空陣列——「還不知道」與「後端沒註冊」共用同一個狀態。
   * 兩者的正確 UI 都是不顯示入口，所以不需要區分。
   *
   * 刻意不用 APP_INITIALIZER：那會讓整個應用在這個請求完成前無法渲染，
   * 而它的結果只影響三顆按鈕該不該出現。初值為空的代價是按鈕晚幾百毫秒出現。
   */
  private readonly providers = signal<ProviderDto[]>([]);

  constructor() {
    this.ingestion
      .providers()
      .pipe(catchError(() => of<ProviderDto[]>([])))
      .subscribe((providers) => this.providers.set(providers));
  }

  supports(key: string, capability: string): boolean {
    const provider = this.providers().find((p) => p.key === key);

    return (
      provider != null &&
      provider.capabilities
        .split(',')
        .map((flag) => flag.trim())
        .includes(capability)
    );
  }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/core/api/provider.service.spec.ts`
Expected: `TOTAL: 4 SUCCESS`

- [ ] **Step 5: 跑全部測試**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: `TOTAL: 82 SUCCESS`

- [ ] **Step 6: Commit**

```bash
git add web/src/app/core/api/provider.service.ts web/src/app/core/api/provider.service.spec.ts
git commit -m "feat(web): add provider capability discovery service"
```

---

### Task 3：IGDB 搜尋對話框

**Files:**
- Create: `web/src/app/shared/igdb-search-dialog/igdb-search-dialog.component.ts`
- Create: `web/src/app/shared/igdb-search-dialog/igdb-search-dialog.component.spec.ts`

這個元件**不知道**品類、不知道是新增還是綁定、不寫任何東西回表單。它只做「搜尋、讓使用者挑、把挑中的吐出去」。

**兩個容易踩的地方：**

1. 對話框內**不可以有 `<form>`**。`ItemDetailComponent` 的模板根節點就是 `<form>`，巢狀 form 是非法 HTML。所有按鈕一律 `type="button"`，Enter 用 `(keydown.enter)` 接。
2. 這裡的 `ngModel` 會沿元素注入器往上找到外層的 `NgForm`。加 `[ngModelOptions]="{standalone: true}"` 切斷，否則這個查詢字串會變成品項表單的一個欄位。

- [ ] **Step 1: 寫失敗測試**

`web/src/app/shared/igdb-search-dialog/igdb-search-dialog.component.spec.ts`：

```ts
import { TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { FetchedMetadataDto } from '../../core/models';
import { IgdbSearchDialogComponent } from './igdb-search-dialog.component';

describe('IgdbSearchDialogComponent', () => {
  const witcher: FetchedMetadataDto = {
    provider: 'igdb',
    externalId: '1942',
    name: 'The Witcher 3: Wild Hunt',
    description: 'A story-driven adventure.',
    imageUrl: 'https://images.igdb.com/igdb/image/upload/t_cover_big/co1wyy.jpg',
    attributes: {
      igdbId: 1942,
      developer: 'CD Projekt RED',
      releaseDate: '2015-05-18T00:00:00Z',
    },
  };

  // useValue 餵的是假服務，型別不必完全吻合真實簽章——unknown 中轉一次即可。
  async function createWith(ingestion: unknown) {
    await TestBed.configureTestingModule({
      imports: [IgdbSearchDialogComponent],
      providers: [{ provide: IngestionService, useValue: ingestion }],
    }).compileComponents();

    const fixture = TestBed.createComponent(IgdbSearchDialogComponent);
    fixture.detectChanges();
    fixture.componentInstance.open();
    fixture.detectChanges();

    return fixture;
  }

  it('trims the query before sending it', async () => {
    const search = jasmine.createSpy('search').and.returnValue(of([]));
    const fixture = await createWith({ search });

    fixture.componentInstance.query = '  the witcher 3  ';
    fixture.componentInstance.search();

    expect(search).toHaveBeenCalledWith('igdb', 'the witcher 3');
  });

  it('renders one selectable card per result', async () => {
    const fixture = await createWith({
      search: () => of([witcher, { ...witcher, externalId: '1943', name: 'Hearts of Stone' }]),
    });

    fixture.componentInstance.query = 'witcher';
    fixture.componentInstance.search();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('[data-igdb-result]').length).toBe(2);
  });

  it('emits the chosen result and closes', async () => {
    const fixture = await createWith({
      search: () => of([witcher]),
    });

    let emitted: FetchedMetadataDto | null = null;
    fixture.componentInstance.select.subscribe((r) => (emitted = r));

    fixture.componentInstance.query = 'witcher';
    fixture.componentInstance.search();
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-igdb-result]').click();
    fixture.detectChanges();

    expect(emitted).toEqual(witcher);
    expect(fixture.nativeElement.querySelector('dialog').open).toBeFalse();
  });

  /** 查無結果不是錯誤，不走 errorInterceptor。 */
  it('shows an empty state instead of nothing when the search returns no games', async () => {
    const fixture = await createWith({
      search: () => of([]),
    });

    fixture.componentInstance.query = 'zzzz';
    fixture.componentInstance.search();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-igdb-empty]')).toBeTruthy();
  });

  /** 沒有這道鎖，連點三下就是三個請求，而後端 IGDB 只允許 4 req/sec。 */
  it('locks the search button while a request is in flight', async () => {
    const pending = new Subject<FetchedMetadataDto[]>();
    const fixture = await createWith({
      search: () => pending,
    });

    fixture.componentInstance.query = 'witcher';
    fixture.componentInstance.search();
    fixture.detectChanges();

    const button: HTMLButtonElement = fixture.nativeElement.querySelector('[data-igdb-search]');
    expect(button.disabled).toBeTrue();
    expect(button.textContent).toContain('搜尋中');
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/shared/igdb-search-dialog/igdb-search-dialog.component.spec.ts`
Expected: 編譯失敗，找不到 `./igdb-search-dialog.component`。

- [ ] **Step 3: 實作**

`web/src/app/shared/igdb-search-dialog/igdb-search-dialog.component.ts`：

```ts
import { Component, ElementRef, inject, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { IGDB_PROVIDER_KEY } from '../../core/api/provider.service';
import { IGNORE_HANDLED_BY_INTERCEPTOR } from '../../core/error.interceptor';
import { FetchedMetadataDto } from '../../core/models';

/**
 * 只做三件事：搜尋、讓使用者挑、把挑中的吐出去。
 * 刻意不知道目標品類，也不知道呼叫端是要新增還是要綁定——那是 ItemDetailComponent 的事。
 *
 * 對話框內不放 <form>：ItemDetailComponent 的模板根節點就是 <form>，巢狀 form 是非法 HTML。
 */
@Component({
  selector: 'app-igdb-search-dialog',
  imports: [FormsModule],
  template: `
    <dialog #dialog class="igdb">
      <div class="igdb__bar">
        <input
          [(ngModel)]="query"
          [ngModelOptions]="{ standalone: true }"
          [disabled]="searching()"
          (keydown.enter)="search()"
          placeholder="遊戲名稱"
          data-igdb-query
        />
        <button
          type="button"
          (click)="search()"
          [disabled]="!query.trim() || searching()"
          data-igdb-search
        >
          {{ searching() ? '搜尋中…' : '搜尋' }}
        </button>
        <button type="button" (click)="close()">取消</button>
      </div>

      @if (results().length) {
        <ul class="igdb__grid">
          @for (result of results(); track result.externalId) {
            <li>
              <button type="button" (click)="choose(result)" data-igdb-result>
                @if (result.imageUrl) {
                  <img [src]="result.imageUrl" [alt]="result.name" loading="lazy" />
                } @else {
                  <span class="igdb__nocover">無封面</span>
                }
                <strong>{{ result.name }}</strong>
                <small>{{ subtitle(result) }}</small>
              </button>
            </li>
          }
        </ul>
      } @else if (searched()) {
        <p class="igdb__empty" data-igdb-empty>查無符合的遊戲。換個關鍵字試試。</p>
      }
    </dialog>
  `,
  styles: `
    .igdb { width: min(46rem, 92vw); border: 1px solid var(--mc-border-strong); background: var(--mc-surface); color: inherit; }
    .igdb::backdrop { background: rgb(0 0 0 / 0.6); }
    .igdb__bar { display: flex; gap: 0.5rem; align-items: center; }
    .igdb__bar input { flex: 1; }
    .igdb__grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(9rem, 1fr)); gap: 0.75rem; margin: 1rem 0 0; padding: 0; list-style: none; max-height: 60vh; overflow-y: auto; }
    .igdb__grid button { display: grid; gap: 0.35rem; width: 100%; text-align: left; padding: 0.5rem; }
    .igdb__grid img { width: 100%; aspect-ratio: 3 / 4; object-fit: cover; }
    .igdb__nocover { display: grid; place-items: center; aspect-ratio: 3 / 4; border: 1px dashed var(--mc-border); color: var(--mc-text-muted); font-size: 0.8rem; }
    .igdb__grid small { color: var(--mc-text-muted); font-size: 0.78rem; }
    .igdb__empty { margin: 1rem 0 0; color: var(--mc-text-muted); }
    @media (max-width: 30rem) {
      .igdb__bar { display: grid; grid-template-columns: 1fr; }
    }
  `,
})
export class IgdbSearchDialogComponent {
  private readonly ingestion = inject(IngestionService);

  readonly select = output<FetchedMetadataDto>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly results = signal<FetchedMetadataDto[]>([]);
  protected readonly searching = signal(false);

  /** 區分「還沒搜過」與「搜過但沒結果」——只有後者該顯示空狀態。 */
  protected readonly searched = signal(false);

  query = '';

  /** 每次開啟都是全新的一輪，不留上次的關鍵字與結果。 */
  open(): void {
    this.query = '';
    this.results.set([]);
    this.searched.set(false);
    this.dialog().nativeElement.showModal();
  }

  close(): void {
    this.dialog().nativeElement.close();
  }

  search(): void {
    const query = this.query.trim();

    if (!query || this.searching()) {
      return;
    }

    // 錯誤訊息由 errorInterceptor 顯示，這裡只負責解鎖讓使用者能換關鍵字重試。
    this.searching.set(true);
    this.ingestion
      .search(IGDB_PROVIDER_KEY, query)
      .pipe(finalize(() => this.searching.set(false)))
      .subscribe({
        next: (results) => {
          this.results.set(results);
          this.searched.set(true);
        },
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }

  protected choose(result: FetchedMetadataDto): void {
    this.select.emit(result);
    this.close();
  }

  /** 年份 · 開發商。任一缺席就不留下孤立的分隔符號。 */
  protected subtitle(result: FetchedMetadataDto): string {
    const released = result.attributes['releaseDate'];
    const developer = result.attributes['developer'];

    return [
      typeof released === 'string' ? released.slice(0, 4) : null,
      typeof developer === 'string' ? developer : null,
    ]
      .filter((part): part is string => part !== null)
      .join(' · ');
  }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/shared/igdb-search-dialog/igdb-search-dialog.component.spec.ts`
Expected: `TOTAL: 5 SUCCESS`

- [ ] **Step 5: 跑全部測試**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: `TOTAL: 87 SUCCESS`

- [ ] **Step 6: Commit**

```bash
git add web/src/app/shared/igdb-search-dialog
git commit -m "feat(web): add igdb search dialog"
```

---

### Task 4：新增品項的搜尋建檔

**Files:**
- Modify: `web/src/app/features/item-detail/item-detail.component.ts`
- Modify: `web/src/app/features/item-detail/item-detail.component.spec.ts`

這個 Task 同時把既有的 OpenGraph `fetchMetadata()` 收斂進同一條套用路徑。改完之後兩個來源共用 `applyMetadata()`，
而不是各有各的怪癖——既有的 `fetchMetadata` 目前把 `attributes` 與 `imageUrl` 整個丟掉。

- [ ] **Step 1: 既有 6 個測試補上 ProviderService stub**

`ItemDetailComponent` 之後會注入 `ProviderService`，而 `ProviderService` 又注入 `IngestionService`。
既有測試餵的假 `IngestionService` 沒有 `providers()`，真的 `ProviderService` 建構子會炸。
**在 `item-detail.component.spec.ts` 的每一個 `providers: [...]` 陣列裡**（共 6 處）加上：

```ts
        { provide: ProviderService, useValue: { supports: () => false } },
```

並在檔頭加入 import：

```ts
import { ProviderService } from '../../core/api/provider.service';
```

`supports: () => false` 讓既有 6 個測試的畫面維持原樣（不出現 IGDB 按鈕），它們的斷言因此不受影響。

- [ ] **Step 2: 寫失敗測試**

在 `item-detail.component.spec.ts` 的 `describe` 內、最後一個 `it` 之後加入：

```ts
  const igdbCategory: CategoryDto = {
    id: 'physical-games',
    name: '實體遊戲',
    icon: 'gamepad-2',
    kind: 'Physical',
    isSystem: true,
    fields: [
      { key: 'igdbId', label: 'IGDB ID', type: 'Number', options: null, required: false, searchable: false, showOnCard: false },
      { key: 'developer', label: '開發商', type: 'Text', options: null, required: false, searchable: true, showOnCard: false },
    ],
  };

  const witcher: FetchedMetadataDto = {
    provider: 'igdb',
    externalId: '1942',
    name: 'The Witcher 3: Wild Hunt',
    description: 'A story-driven adventure.',
    imageUrl: null,
    attributes: { igdbId: 1942, developer: 'CD Projekt RED', igdbRating: 93.5 },
  };

  async function createNewItemWithIgdb(available: boolean) {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => null } } } },
        { provide: CategoryService, useValue: { list: () => of([igdbCategory]) } },
        { provide: CatalogService, useValue: {} },
        { provide: IngestionService, useValue: { search: () => of([]) } },
        { provide: NotificationService, useValue: { success: () => undefined, error: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => available } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();

    return fixture;
  }

  it('hides the igdb entry point when the provider is not registered', async () => {
    const fixture = await createNewItemWithIgdb(false);

    expect(fixture.nativeElement.querySelector('[data-igdb-open]')).toBeNull();
  });

  /** 品類決定哪些欄位能寫。沒選品類就搜尋，等於不知道要把結果放進哪個 schema。 */
  it('disables the igdb button until a category is chosen', async () => {
    const fixture = await createNewItemWithIgdb(true);

    const button: HTMLButtonElement = fixture.nativeElement.querySelector('[data-igdb-open]');
    expect(button.disabled).toBeTrue();

    fixture.componentInstance.categoryId = igdbCategory.id;
    fixture.componentInstance.onCategoryChanged();
    fixture.detectChanges();

    expect(button.disabled).toBeFalse();
  });

  /**
   * 這是整個功能最容易靜默壞掉的地方。品類沒宣告 igdbRating，
   * 若它跟著送出去，後端 AttributeValidator 直接回 400，而且錯誤訊息與搜尋毫無關聯。
   */
  it('drops attributes the chosen category has not declared', async () => {
    const fixture = await createNewItemWithIgdb(true);
    fixture.componentInstance.categoryId = igdbCategory.id;
    fixture.componentInstance.onCategoryChanged();
    fixture.detectChanges();

    fixture.componentInstance.applyMetadata(witcher, 'prefill');

    expect(Object.keys(fixture.componentInstance.attributes()).sort()).toEqual(['developer', 'igdbId']);
  });

  it('overwrites the name and description in prefill mode', async () => {
    const fixture = await createNewItemWithIgdb(true);
    fixture.componentInstance.categoryId = igdbCategory.id;
    fixture.componentInstance.onCategoryChanged();

    fixture.componentInstance.applyMetadata(witcher, 'prefill');

    expect(fixture.componentInstance.name).toBe('The Witcher 3: Wild Hunt');
    expect(fixture.componentInstance.description).toBe('A story-driven adventure.');
  });

  /** 既有品項的名稱是使用者在庫裡認得的那個，不該被英文原名蓋掉。 */
  it('keeps the name and description untouched in bind mode', async () => {
    const fixture = await createNewItemWithIgdb(true);
    fixture.componentInstance.categoryId = igdbCategory.id;
    fixture.componentInstance.onCategoryChanged();
    fixture.componentInstance.name = '巫師三';
    fixture.componentInstance.description = '我自己寫的心得';

    fixture.componentInstance.applyMetadata(witcher, 'bind');

    expect(fixture.componentInstance.name).toBe('巫師三');
    expect(fixture.componentInstance.description).toBe('我自己寫的心得');
    expect(fixture.componentInstance.attributes()['igdbId']).toBe(1942);
  });
```

檔頭的 import 補上 `FetchedMetadataDto`：

```ts
import { CategoryDto, FetchedMetadataDto } from '../../core/models';
```

- [ ] **Step 3: 跑測試確認失敗**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/features/item-detail/item-detail.component.spec.ts`
Expected: 編譯失敗，`ItemDetailComponent` 沒有 `applyMetadata`。

- [ ] **Step 4: 改 ItemDetailComponent 的類別**

`web/src/app/features/item-detail/item-detail.component.ts`：

檔頭 import 加入：

```ts
import { ProviderService } from '../../core/api/provider.service';
import { FetchedMetadataDto } from '../../core/models';
import { IgdbSearchDialogComponent } from '../../shared/igdb-search-dialog/igdb-search-dialog.component';
```

`@Component` 的 `imports` 陣列加入 `IgdbSearchDialogComponent`。

`viewChild` 加入 `@angular/core` 的 import 清單（該行變成
`import { Component, computed, inject, signal, viewChild } from '@angular/core';`）。

在 `private readonly ingestion = inject(IngestionService);` 之後加入：

```ts
  private readonly providers = inject(ProviderService);
```

在 `readonly fetching = signal(false);` 之後加入：

```ts
  private readonly searchDialog = viewChild(IgdbSearchDialogComponent);

  /** IGDB 未設定時後端不會註冊它，整組入口不渲染。 */
  readonly igdbAvailable = computed(() => this.providers.supports(IGDB_PROVIDER_KEY, 'Search'));
```

並在檔頭 import 加入 `IGDB_PROVIDER_KEY`（與 `ProviderService` 同一行）：

```ts
import { IGDB_PROVIDER_KEY, ProviderService } from '../../core/api/provider.service';
```

把既有的 `fetchMetadata()` 整個換成下面三個方法：

```ts
  openIgdbSearch(): void {
    this.searchDialog()?.open();
  }

  fetchMetadata(): void {
    if (this.busy()) {
      return;
    }

    // 錯誤訊息由 errorInterceptor 顯示，這裡只負責解鎖讓使用者能換一個網址重試。
    this.fetching.set(true);
    this.ingestion
      .fetchByUrl(this.fetchUrl)
      .pipe(finalize(() => this.fetching.set(false)))
      .subscribe({
        next: (metadata) => this.applyMetadata(metadata, 'prefill'),
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }

  /**
   * 兩個外部來源（OpenGraph 網址、IGDB 搜尋）共用的唯一套用路徑。
   *
   * prefill 用於新增品項——名稱與描述本來就是空的，覆寫沒有損失。
   * bind 用於既有品項——名稱是使用者在庫裡認得的那個，描述可能是他自己寫的心得，都不動。
   */
  applyMetadata(metadata: FetchedMetadataDto, mode: 'prefill' | 'bind'): void {
    if (mode === 'prefill') {
      this.name = metadata.name;
      this.description = metadata.description ?? '';
    }

    const merged = { ...this.attributes(), ...this.declaredOnly(metadata.attributes) };

    this.initialAttributes.set(merged);
    this.attributes.set(merged);

    this.notifications.success('已帶入資料，請確認後儲存。');
  }

  /**
   * 品類沒宣告的 key 會被後端 AttributeValidator 擋掉，整筆儲存回 400。
   *
   * 不能倚賴 DynamicFormComponent 代為過濾：表單重建不會觸發 valueChanges，
   * 使用者若套用後直接儲存、中途沒編輯任何欄位，attributes 送出的就是這裡設進去的原值。
   *
   * 這條規則與後端 EnrichCommandHandler.ToEnrichment 是同一份政策，兩處要一起改。
   */
  private declaredOnly(source: Record<string, unknown>): Record<string, unknown> {
    const declared = new Set(this.selectedCategory()?.fields.map((f) => f.key) ?? []);

    return Object.fromEntries(Object.entries(source).filter(([key]) => declared.has(key)));
  }
```

- [ ] **Step 5: 改 ItemDetailComponent 的模板**

在既有的 `<fieldset class="detail__fetch mc-panel">…</fieldset>` **之後**、該 `@if (!itemId())` 區塊的結束大括號**之前**加入：

```html
        @if (igdbAvailable()) {
          <div class="detail__igdb mc-panel">
            <button
              type="button"
              (click)="openIgdbSearch()"
              [disabled]="!categoryId || busy()"
              [title]="categoryId ? '' : '請先選擇品類'"
              data-igdb-open
            >
              從 IGDB 搜尋遊戲
            </button>
            <span class="hint">先選好上方的品類，搜尋結果才知道要填進哪些欄位。</span>
          </div>
        }
```

在模板最後的 `</form>` **之後**（與 `<form>` 平行的位置）加入：

```html
    @if (igdbAvailable()) {
      <app-igdb-search-dialog (select)="applyMetadata($event, itemId() ? 'bind' : 'prefill')" />
    }
```

> 對話框放在 `</form>` 外面是刻意的：`<dialog>` 是 modal，不屬於表單，
> 放進去會讓 `<form method="dialog">` 之類的結構與外層表單糾纏。

`styles` 區塊在 `.detail__fetch` 那行之後加入：

```css
    .detail__igdb { display: flex; gap: 0.75rem; align-items: center; flex-wrap: wrap; }
    .detail__igdb .hint { color: var(--mc-text-muted); font-size: 0.85rem; }
```

- [ ] **Step 6: 跑測試確認通過**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/features/item-detail/item-detail.component.spec.ts`
Expected: `TOTAL: 11 SUCCESS`（既有 6 + 新增 5）

- [ ] **Step 7: 跑全部測試**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: `TOTAL: 92 SUCCESS`

- [ ] **Step 8: Commit**

```bash
git add web/src/app/features/item-detail
git commit -m "feat(web): create items from igdb search results"
```

---

### Task 5：既有品項的重抓與綁定

**Files:**
- Modify: `web/src/app/features/item-detail/item-detail.component.ts`
- Modify: `web/src/app/features/item-detail/item-detail.component.spec.ts`

- [ ] **Step 1: 寫失敗測試**

在 `item-detail.component.spec.ts` 的 `describe` 內加入：

```ts
  const steamItem: ItemDto = {
    id: '65b0000000000000000000aa',
    categoryId: 'physical-games',
    name: 'Team Fortress 2',
    description: null,
    images: [],
    tags: [],
    isShowcased: false,
    source: 'Steam',
    externalRef: { provider: 'steam', externalId: '440', url: null, lastSyncedAt: '2026-07-01T00:00:00Z' },
    acquisition: null,
    locationId: null,
    attributes: {},
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: '2026-07-01T00:00:00Z',
  };

  async function createExistingItem(item: ItemDto) {
    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => item.id } } } },
        { provide: CategoryService, useValue: { list: () => of([igdbCategory]) } },
        { provide: CatalogService, useValue: { get: () => of(item) } },
        { provide: IngestionService, useValue: { search: () => of([]) } },
        { provide: NotificationService, useValue: { success: () => undefined, error: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => true } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();

    return fixture;
  }

  it('offers a refetch button for a steam-synced item', async () => {
    const fixture = await createExistingItem(steamItem);

    expect(fixture.nativeElement.querySelector('[data-igdb-refetch]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-igdb-bind]')).toBeNull();
  });

  it('offers a bind button for an item with no usable external id', async () => {
    const fixture = await createExistingItem({ ...steamItem, source: 'Manual', externalRef: null });

    expect(fixture.nativeElement.querySelector('[data-igdb-bind]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-igdb-refetch]')).toBeNull();
  });

  /**
   * OpenGraph 品項也有 externalRef，但後端會組出 opengraph:xxx 這種 IGDB 反查不了的識別碼，
   * 結果是略過。把它當成可定址，就是給使用者一顆按了沒反應的按鈕。
   */
  it('does not treat an opengraph reference as addressable', async () => {
    const fixture = await createExistingItem({
      ...steamItem,
      source: 'OpenGraph',
      externalRef: { provider: 'opengraph', externalId: 'https://shop/x', url: null, lastSyncedAt: '2026-07-01T00:00:00Z' },
    });

    expect(fixture.nativeElement.querySelector('[data-igdb-bind]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-igdb-refetch]')).toBeNull();
  });

  it('treats an item that already carries the marker as addressable', async () => {
    const fixture = await createExistingItem({
      ...steamItem,
      source: 'Manual',
      externalRef: null,
      attributes: { igdbId: 1942 },
    });

    expect(fixture.nativeElement.querySelector('[data-igdb-refetch]')).toBeTruthy();
  });

  /** 說「完成」會讓使用者以為資料已更新。查無對應時什麼都沒變。 */
  it('reports a lookup miss instead of claiming success', async () => {
    const messages: string[] = [];
    const job: SyncJobDto = {
      id: 'j1', provider: 'igdb', status: 'Succeeded',
      created: 0, updated: 0, failed: 0, skipped: 1,
      error: null, startedAt: '2026-08-01T03:00:00Z', finishedAt: '2026-08-01T03:00:01Z',
    };

    await TestBed.configureTestingModule({
      imports: [ItemDetailComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => steamItem.id } } } },
        { provide: CategoryService, useValue: { list: () => of([igdbCategory]) } },
        { provide: CatalogService, useValue: { get: () => of(steamItem) } },
        { provide: IngestionService, useValue: { search: () => of([]), enrich: () => of(job) } },
        {
          provide: NotificationService,
          useValue: {
            success: (m: string) => messages.push(m),
            error: (m: string) => messages.push(m),
          },
        },
        { provide: ProviderService, useValue: { supports: () => true } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ItemDetailComponent);
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-igdb-refetch]').click();
    fixture.detectChanges();

    expect(messages.join()).toContain('查無對應');
  });
```

檔頭的 import 補上 `ItemDto` 與 `SyncJobDto`：

```ts
import { CategoryDto, FetchedMetadataDto, ItemDto, SyncJobDto } from '../../core/models';
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/features/item-detail/item-detail.component.spec.ts`
Expected: 5 個新測試失敗，`[data-igdb-refetch]` 與 `[data-igdb-bind]` 都是 null。

- [ ] **Step 3: 改類別**

`web/src/app/features/item-detail/item-detail.component.ts`：

在 `readonly fetching = signal(false);` 之後加入：

```ts
  readonly enriching = signal(false);
```

把 `busy` 的定義改成：

```ts
  /** 任一改寫動作進行中就鎖住全部按鈕：同一筆品項不該有並行的改寫。 */
  readonly busy = computed(
    () => this.saving() || this.removing() || this.fetching() || this.enriching(),
  );
```

在 `igdbAvailable` 之後加入：

```ts
  /**
   * 後端 ExternalIdFor 的規則：先看 marker，再退回 externalRef。
   * 必須檢查 provider === 'steam'——OpenGraph 品項也有 externalRef，
   * 但後端會組出 opengraph:xxx 這種 IGDB 反查不了的識別碼，結果只會是略過。
   */
  readonly igdbAddressable = computed(() => {
    const item = this.item();

    return (
      item != null &&
      (item.attributes['igdbId'] != null || item.externalRef?.provider === 'steam')
    );
  });
```

在 `applyMetadata` 之後加入：

```ts
  refetchFromIgdb(): void {
    const id = this.itemId();

    if (!id || this.busy()) {
      return;
    }

    this.enriching.set(true);
    this.ingestion
      .enrich(IGDB_PROVIDER_KEY, [id])
      .pipe(finalize(() => this.enriching.set(false)))
      .subscribe({
        next: (job) => {
          // 誠實比樂觀重要：查無對應時什麼都沒變，說「完成」會讓使用者以為資料已更新。
          if (job.updated === 0 && job.skipped > 0) {
            this.notifications.error('IGDB 查無對應，未變更任何欄位。');
            return;
          }

          this.notifications.success('已從 IGDB 更新。');
          this.reloadItem(id);
        },
        error: IGNORE_HANDLED_BY_INTERCEPTOR,
      });
  }
```

- [ ] **Step 4: 改模板**

在 `@if (itemId(); as id) { … 圖片 … }` 區塊**之前**加入：

```html
      @if (itemId() && igdbAvailable()) {
        <section class="detail__panel mc-panel" data-item-igdb>
          <div class="mc-eyebrow">IGDB</div>
          @if (igdbAddressable()) {
            <button type="button" (click)="refetchFromIgdb()" [disabled]="busy()" data-igdb-refetch>
              {{ enriching() ? '抓取中…' : '重新從 IGDB 抓取' }}
            </button>
          } @else {
            <button type="button" (click)="openIgdbSearch()" [disabled]="busy()" data-igdb-bind>
              從 IGDB 搜尋並綁定
            </button>
            <span class="hint">這筆品項還沒有對應的 IGDB 條目，綁定後才能自動更新。</span>
          }
        </section>
      }
```

`styles` 區塊加入：

```css
    .detail__panel .hint { color: var(--mc-text-muted); font-size: 0.85rem; }
```

- [ ] **Step 5: 跑測試確認通過**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/features/item-detail/item-detail.component.spec.ts`
Expected: `TOTAL: 16 SUCCESS`

- [ ] **Step 6: 跑全部測試**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: `TOTAL: 97 SUCCESS`

- [ ] **Step 7: Commit**

```bash
git add web/src/app/features/item-detail
git commit -m "feat(web): refetch or bind igdb data on existing items"
```

---

### Task 6：設定頁的批次補完面板

**Files:**
- Create: `web/src/app/features/settings/igdb-enrich.component.ts`
- Create: `web/src/app/features/settings/igdb-enrich.component.spec.ts`

比照同資料夾既有的 `data-transfer.component.ts`：設定頁的每個功能區塊各自一個元件。

- [ ] **Step 1: 寫失敗測試**

`web/src/app/features/settings/igdb-enrich.component.spec.ts`：

```ts
import { TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { IngestionService } from '../../core/api/ingestion.service';
import { ProviderService } from '../../core/api/provider.service';
import { NotificationService } from '../../core/notification.service';
import { SyncJobDto } from '../../core/models';
import { IgdbEnrichComponent } from './igdb-enrich.component';

describe('IgdbEnrichComponent', () => {
  const job: SyncJobDto = {
    id: 'j1', provider: 'igdb', status: 'Succeeded',
    created: 0, updated: 12, failed: 0, skipped: 3,
    error: null, startedAt: '2026-08-01T03:00:00Z', finishedAt: '2026-08-01T03:00:09Z',
  };

  // useValue 餵的是假服務，型別不必完全吻合真實簽章。
  async function create(available: boolean, ingestion: unknown, notifications: unknown = { success: () => undefined }) {
    await TestBed.configureTestingModule({
      imports: [IgdbEnrichComponent],
      providers: [
        { provide: IngestionService, useValue: ingestion },
        { provide: ProviderService, useValue: { supports: () => available } },
        { provide: NotificationService, useValue: notifications },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(IgdbEnrichComponent);
    fixture.detectChanges();

    return fixture;
  }

  it('renders nothing when igdb is not configured', async () => {
    const fixture = await create(false, {});

    expect(fixture.nativeElement.querySelector('[data-igdb-enrich]')).toBeNull();
  });

  it('reports how many items were updated, skipped and failed', async () => {
    const messages: string[] = [];
    const fixture = await create(true, { enrich: () => of(job) }, {
      success: (m: string) => messages.push(m),
    });

    fixture.nativeElement.querySelector('[data-igdb-enrich-run]').click();

    expect(messages[0]).toContain('更新 12');
    expect(messages[0]).toContain('略過 3');
    expect(messages[0]).toContain('失敗 0');
  });

  /** 失敗的補完也會留下一筆 job 紀錄，設定頁兩條路徑都要重載那張表。 */
  it('signals completion so the caller can reload the job table', async () => {
    const fixture = await create(true, { enrich: () => of(job) });

    let completed = 0;
    fixture.componentInstance.completed.subscribe(() => (completed += 1));

    fixture.nativeElement.querySelector('[data-igdb-enrich-run]').click();

    expect(completed).toBe(1);
  });

  it('locks the button while the run is in flight', async () => {
    const pending = new Subject<SyncJobDto>();
    const fixture = await create(true, { enrich: () => pending });

    const button: HTMLButtonElement = fixture.nativeElement.querySelector('[data-igdb-enrich-run]');
    button.click();
    fixture.detectChanges();

    expect(button.disabled).toBeTrue();
    expect(button.textContent).toContain('補完中');
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/features/settings/igdb-enrich.component.spec.ts`
Expected: 編譯失敗，找不到 `./igdb-enrich.component`。

- [ ] **Step 3: 實作**

`web/src/app/features/settings/igdb-enrich.component.ts`：

```ts
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

  /** 成功與失敗都要發：失敗的補完同樣會在後端留下一筆 job 紀錄。 */
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
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/features/settings/igdb-enrich.component.spec.ts`
Expected: `TOTAL: 4 SUCCESS`

- [ ] **Step 5: 跑全部測試**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: `TOTAL: 101 SUCCESS`

- [ ] **Step 6: Commit**

```bash
git add web/src/app/features/settings/igdb-enrich.component.ts web/src/app/features/settings/igdb-enrich.component.spec.ts
git commit -m "feat(web): add igdb batch enrichment panel"
```

---

### Task 7：設定頁掛上面板與「略過」欄

**Files:**
- Modify: `web/src/app/features/settings/settings.component.ts`
- Modify: `web/src/app/features/settings/settings.component.spec.ts`

補完 job 的核心數字是「略過」。少了這一欄，使用者看到的是「更新 3、失敗 0」，剩下的 7 筆去哪了無從得知。

- [ ] **Step 1: 既有測試補上 ProviderService stub**

`settings.component.spec.ts` 的每一個 `providers: [...]` 陣列都加上：

```ts
        { provide: ProviderService, useValue: { supports: () => false } },
```

檔頭加入：

```ts
import { ProviderService } from '../../core/api/provider.service';
```

- [ ] **Step 2: 寫失敗測試**

在 `settings.component.spec.ts` 的 `describe` 內加入：

```ts
  it('shows the skipped count in the sync log', async () => {
    const job: SyncJobDto = {
      id: 'j1', provider: 'igdb', status: 'Succeeded',
      created: 0, updated: 12, failed: 1, skipped: 7,
      error: null, startedAt: '2026-08-01T03:00:00Z', finishedAt: '2026-08-01T03:00:09Z',
    };

    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        { provide: IngestionService, useValue: { accounts: () => of([]), jobs: () => of([job]) } },
        { provide: ShareService, useValue: { list: () => of([]) } },
        { provide: TransferService, useValue: {} },
        { provide: NotificationService, useValue: { success: () => undefined } },
        { provide: ProviderService, useValue: { supports: () => false } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();

    const headers = Array.from(fixture.nativeElement.querySelectorAll('th')).map(
      (th) => (th as HTMLElement).textContent,
    );
    const cells = Array.from(fixture.nativeElement.querySelectorAll('tbody td')).map(
      (td) => (td as HTMLElement).textContent,
    );

    expect(headers).toContain('略過');
    expect(cells).toContain('7');
  });
```

檔頭 import 補上 `SyncJobDto`：

```ts
import { SyncJobDto } from '../../core/models';
```

> 若既有的 `settings.component.spec.ts` 已經 import 了 `models` 的其他型別，把 `SyncJobDto` 併進同一行。

- [ ] **Step 3: 跑測試確認失敗**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/features/settings/settings.component.spec.ts`
Expected: `Expected $ to contain '略過'` —— 表頭沒有這一欄。

- [ ] **Step 4: 改模板**

`web/src/app/features/settings/settings.component.ts`：

表頭那一列改成（在「更新」與「失敗」之間插入「略過」）：

```html
            <tr><th>時間</th><th>來源</th><th>狀態</th><th>新增</th><th>更新</th><th>略過</th><th>失敗</th></tr>
```

資料列在 `<td>{{ job.updated }}</td>` 之後插入：

```html
                <td>{{ job.skipped }}</td>
```

空狀態那一列的 `colspan` 由 6 改成 7：

```html
              <tr><td colspan="7">尚無同步紀錄。</td></tr>
```

緊接在同步紀錄那個 `<section>` 的結束標籤之後（也就是 `PUBLIC ACCESS` 那個 `<section>` 之前）加入：

```html
    <app-igdb-enrich (completed)="reloadJobs()" />
```

> 位置在同步紀錄與分享連結之間，因為它與上方的 Steam 同步是同一類事情（把外部資料拉進來），
> 而分享連結是另一件事。

`imports` 陣列加入 `IgdbEnrichComponent`，檔頭加入：

```ts
import { IgdbEnrichComponent } from './igdb-enrich.component';
```

- [ ] **Step 5: 把 reloadJobs 改成 protected**

Angular 的嚴格模板檢查不允許模板存取 `private` 成員。把

```ts
  private reloadJobs(): void {
```

改成

```ts
  protected reloadJobs(): void {
```

- [ ] **Step 6: 跑測試確認通過**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/features/settings/settings.component.spec.ts`
Expected: 既有測試 + 1 個新測試全數通過。

- [ ] **Step 7: 跑全部測試**

Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless`
Expected: `TOTAL: 102 SUCCESS`

- [ ] **Step 8: Commit**

```bash
git add web/src/app/features/settings/settings.component.ts web/src/app/features/settings/settings.component.spec.ts
git commit -m "feat(web): surface skipped counts and mount the enrich panel"
```

---

### Task 8：後端讓 IGDB 封面成為可下載的圖片來源

**Files:**
- Modify: `src/MyCollection.Infrastructure/Imaging/ShowcaseImageDownloader.cs:104-117`
- Create: `tests/MyCollection.Tests/Unit/ShowcaseImageDownloaderTests.cs`

`ShowcaseImageDownloader` 在品項被設為精選且尚無任何圖片時，下載遠端圖片並設為主圖。
目前只認 `headerUrl` 與 `iconUrl`，兩者都是 Steam 給的——所以走 IGDB 搜尋建檔的實體遊戲永遠拿不到封面。

這個 Task 與前七個完全獨立，任何時候都能做。

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/ShowcaseImageDownloaderTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Imaging;

namespace MyCollection.Tests.Unit;

public class ShowcaseImageDownloaderTests
{
    /// <summary>
    /// 實體遊戲走 IGDB 搜尋建檔，沒有 Steam 的 headerUrl。
    /// coverUrl 不被認得的話，那些品項設為精選也永遠沒有圖。
    /// </summary>
    [Fact]
    public void Uses_the_igdb_cover_when_there_is_no_steam_header()
    {
        var item = new Item
        {
            Name = "The Witcher 3",
            Attributes = new BsonDocument { { "coverUrl", "https://images.igdb.com/a.jpg" } }
        };

        ShowcaseImageDownloader.ResolveSourceUrl(item)!.ToString()
            .Should().Be("https://images.igdb.com/a.jpg");
    }

    /// <summary>Steam 的 header 是橫幅、比例貼近卡片，優先於 IGDB 的直式封面。</summary>
    [Fact]
    public void Prefers_the_steam_header_over_the_igdb_cover()
    {
        var item = new Item
        {
            Name = "Team Fortress 2",
            Attributes = new BsonDocument
            {
                { "coverUrl", "https://images.igdb.com/a.jpg" },
                { "headerUrl", "https://cdn.steam/header.jpg" }
            }
        };

        ShowcaseImageDownloader.ResolveSourceUrl(item)!.ToString()
            .Should().Be("https://cdn.steam/header.jpg");
    }

    [Fact]
    public void Falls_back_to_the_icon_when_it_is_the_only_url()
    {
        var item = new Item
        {
            Name = "Portal 2",
            Attributes = new BsonDocument { { "iconUrl", "https://cdn.steam/icon.jpg" } }
        };

        ShowcaseImageDownloader.ResolveSourceUrl(item)!.ToString()
            .Should().Be("https://cdn.steam/icon.jpg");
    }

    [Fact]
    public void Returns_null_when_no_attribute_holds_an_absolute_url()
    {
        var item = new Item
        {
            Name = "手辦",
            Attributes = new BsonDocument { { "coverUrl", "不是網址" } }
        };

        ShowcaseImageDownloader.ResolveSourceUrl(item).Should().BeNull();
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter ShowcaseImageDownloaderTests`
Expected: 編譯失敗，`ResolveSourceUrl` 因為是 `private` 而無法存取。

- [ ] **Step 3: 實作**

`src/MyCollection.Infrastructure/Imaging/ShowcaseImageDownloader.cs`，把

```csharp
    private static Uri? ResolveSourceUrl(Item item)
    {
        foreach (var key in (string[])["headerUrl", "iconUrl"])
```

改成

```csharp
    /// <summary>
    /// 挑第一個能解析成絕對網址的來源。順序即優先序：
    /// Steam 的 header 是橫幅、比例貼近卡片；IGDB 的 cover 是直式封面，
    /// 是實體遊戲唯一的來源；icon 最小，只在前兩者都沒有時才用。
    ///
    /// public 是為了讓這段優先序能被直接測試——它是無狀態的純函式，
    /// 比為了一行改動引入 InternalsVisibleTo 便宜。
    /// </summary>
    public static Uri? ResolveSourceUrl(Item item)
    {
        foreach (var key in (string[])["headerUrl", "coverUrl", "iconUrl"])
```

其餘內容不動。

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter ShowcaseImageDownloaderTests`
Expected: `Passed: 4`

- [ ] **Step 5: 跑全部後端測試**

Run: `dotnet build && dotnet test`
Expected: 建置 0 warnings / 0 errors；`Passed: 451`（447 + 4）

- [ ] **Step 6: Commit**

```bash
git add src/MyCollection.Infrastructure/Imaging/ShowcaseImageDownloader.cs tests/MyCollection.Tests/Unit/ShowcaseImageDownloaderTests.cs
git commit -m "feat(showcase): accept igdb covers as a downloadable image source"
```

---

## 完成後的驗證

- [ ] `cd web && npm test -- --watch=false --browsers=ChromeHeadless` → `TOTAL: 102 SUCCESS`
- [ ] `cd web && npm run build` → 成功，無新增警告
- [ ] `dotnet build` → 0 warnings / 0 errors
- [ ] `dotnet test` → `Passed: 451`
- [ ] `git status --short` 只剩 `M web/angular.json`
- [ ] 八個 Task 各一個 commit

## 手動驗證（需真實 IGDB 憑證）

後端 Task 13 的手動驗證從未執行過，因為當時沒有憑證。設好 `IGDB_CLIENT_ID` / `IGDB_CLIENT_SECRET` 後：

1. 設定頁應出現「IGDB 補完」面板；沒設憑證時應完全看不到
2. 新增品項頁選「實體遊戲」後，「從 IGDB 搜尋遊戲」按鈕啟用；搜 `the witcher 3` 應看到多張封面
3. 挑一筆後，名稱、描述、開發商、發行商、發售日期、類型、平台、評分應填入表單；儲存不應出現 400
4. 對某個 Steam 同步進來的數位遊戲按「重新從 IGDB 抓取」，該品項的 `tags`、`isShowcased`、`name` 不應改變
5. 把一個用 IGDB 建檔的實體遊戲設為精選，稍候重新整理，它應該長出封面圖

## 後續（不在本計畫內）

- 自訂品類的欄位補齊 UI（後端 Task 14 的兩個端點已就緒）
- Url 欄位的圖片預覽（需先引入「這個 Url 是圖片」的欄位宣告）
- 搜尋結果分頁與即時輸入搜尋（後者會撞上 IGDB 的 4 req/sec 限制）
