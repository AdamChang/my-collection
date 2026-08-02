# IGDB 前端整合 — Task 7 與 Task 8 交接文件

**交接日期：** 2026-08-02
**交接對象：** 接手 Task 7、Task 8 的外部 agent
**前置狀態：** Task 1–6 已完成並 commit，全部測試綠燈

這份文件是自足的。你不需要讀 `docs/superpowers/plans/2026-08-01-igdb-frontend.md` 的其他部分，但那份計畫書仍在，Task 7 / 8 的原文在其第 1491–1770 行。**若原計畫書與本文件衝突，以本文件為準**——原計畫書的部分數字與假設已經過時，下面會逐一標明。

---

## 1. 專案概況

| 項目 | 內容 |
|---|---|
| 後端 | .NET 10 / C# / ASP.NET Core，Clean Architecture + DDD + CQRS (MediatR 14) |
| 前端 | Angular 20.3，standalone components + signals，`@if` / `@for` 控制流 |
| 資料 | MongoDB（原生 driver 3.10） |
| 後端測試 | xUnit + FluentAssertions + Moq + Testcontainers |
| 前端測試 | Karma + Jasmine |
| 工作目錄 | `f:\VibeCode\MyCollection`（Windows / PowerShell），Angular app 在 `web/` |
| 分支 | **`mongoAtlas`**（不是 `master`） |

IGDB 是遊戲 metadata provider。這一整套工作是把它接進 app：搜尋建檔、既有品項補完、批次補完。

---

## 2. 現況：Task 1–6 已完成

### 2.1 前端測試基線

**`TOTAL: 125 SUCCESS`**（我親自跑過確認，非採信 agent 回報）

```
cd web
npm test -- --watch=false --browsers=ChromeHeadless
```

> ⚠️ **原計畫書寫 Task 7 完成後應為 `TOTAL: 108`。那個數字早就過時了。**
> 審查過程中補了 22 個計畫書沒有的測試（全部是突變測試抓到的真實缺口）。
> Task 7 的正確期望值是 **126**（125 + 1）。

### 2.2 已完成的 commit（分支 `mongoAtlas`，由舊到新）

| SHA | 訊息 |
|---|---|
| `f83ffa4` | feat(web): add provider discovery, search and enrich api methods |
| `7cc46a5` | feat(web): add provider capability discovery service |
| `4f651a9` | feat(web): add igdb search dialog |
| `1db2ae5` | docs(web): state the real reason reset is safe to call twice |
| `fbe2cc7` | feat(web): create items from igdb search results |
| `7e2dc16` | test(web): close the mutation gaps in the igdb prefill path |
| `789deed` | feat(web): refetch or bind igdb data on existing items |
| `6b0a849` | test(web): lock the refetch button while enrichment is in flight |
| `7805cfd` | fix(web): stop reporting a failed igdb lookup as a successful update |
| `2d0b2c9` | test(web): pin the empty enrichment result to a failure report |
| `1c9d6bf` | feat(web): add igdb batch enrichment panel |
| `542a23e` | test(web): guard the failure path and the unlock after a batch run |
| `72f344b` | test(web): pin the provider key the enrich panel asks for |

### 2.3 你會用到的既有介面

**`web/src/app/core/models.ts`**

```ts
export interface ProviderDto { key: string; capabilities: string; }  // capabilities 是逗號分隔的 flags 字串，例如 "BulkSync, UrlLookup"
export interface SyncJobDto {
  id: string; provider: string; status: string;
  created: number; updated: number; failed: number; skipped: number;
  error: string | null; startedAt: string; finishedAt: string | null;
}
```

**`web/src/app/core/api/provider.service.ts`**

```ts
export const IGDB_PROVIDER_KEY = 'igdb';
export type ProviderCapability = 'BulkSync' | 'UrlLookup' | 'Search';

@Injectable({ providedIn: 'root' })
export class ProviderService {
  supports(key: string, capability: ProviderCapability): boolean;
}
```

建構子會背景抓 `/api/ingest/providers`。**它是 lazy 的（第一次被注入時才探測），這是刻意的**——見 §6.1。

**`web/src/app/features/settings/igdb-enrich.component.ts`**（Task 6 新建，尚未被任何地方使用）

```ts
@Component({ selector: 'app-igdb-enrich', ... })
export class IgdbEnrichComponent {
  readonly completed = output<void>();   // 成功與失敗都會發
}
```

整個模板包在 `@if (available())` 內，IGDB 未設定時完全不渲染。

---

## 3. 硬性限制（違反會造成實質損害）

1. **分支是 `mongoAtlas`。** 不要切分支、不要 rebase、不要 push。
2. **`web/angular.json` 有一個與本工作無關的未提交變更**（Angular CLI 的 analytics UUID）。
   **絕對不要 stage / commit / revert / stash 它。** 它應該從頭到尾都是 `git status --short` 裡唯一的一行。
3. **一律 `git add` 明確路徑**，永遠不要 `git add .` 或 `git add -A`。
4. **不要 `git commit --amend`、不要 `git reset`、不要 `git checkout --` 任何有未提交內容的檔案。**
   這個 session 已經發生過一次 `git checkout --` 毀掉未提交工作的事故，也發生過一次 `--amend` 動到錯誤 commit 的事故（靠 reflog 復原）。
5. Task 7 只改 `settings.component.ts` 與 `settings.component.spec.ts`。
   Task 8 只改 `ShowcaseImageDownloader.cs` 並新增一個測試檔。
6. 環境沒有 prettier / eslint / lint script，只有 `.editorconfig`（2 空格、單引號、結尾換行）。

---

## 4. Task 7：設定頁掛上面板與「略過」欄

**Files:**
- Modify: `web/src/app/features/settings/settings.component.ts`
- Modify: `web/src/app/features/settings/settings.component.spec.ts`

補完 job 的核心數字是「略過」。少了這一欄，使用者看到的是「更新 3、失敗 0」，剩下的 7 筆去哪了無從得知。

### 現況核對（我已驗證，2026-08-02）

- `settings.component.ts:49` 表頭目前是 6 欄：`時間 / 來源 / 狀態 / 新增 / 更新 / 失敗`
- `settings.component.ts:72` 空狀態 `colspan="6"`
- `settings.component.ts:262` 是 `private reloadJobs(): void {`
- `settings.component.ts:14` 的 `imports: [FormsModule, DatePipe, DataTransferComponent]`
- `settings.component.spec.ts` 目前有 **3 個測試、3 個 `providers: [...]` 陣列**

### Step 1：既有測試補上 ProviderService stub

`settings.component.spec.ts` 的**每一個** `providers: [...]` 陣列（共 3 處）加上：

```ts
        { provide: ProviderService, useValue: { supports: () => false } },
```

檔頭加入：

```ts
import { ProviderService } from '../../core/api/provider.service';
```

**為什麼非做不可**：`IgdbEnrichComponent` 會成為 `SettingsComponent` 的子元件，它注入 `ProviderService`，而 `ProviderService` 的建構子會呼叫 `IngestionService.providers()`。既有測試餵的假 `IngestionService` 只有 `accounts()` 與 `jobs()`，真的 `ProviderService` 一建構就會炸。

`supports: () => false` 讓既有 3 個測試的畫面維持原樣（面板不渲染），斷言不受影響。

### Step 2：寫失敗測試

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

檔頭 import 補上 `SyncJobDto`（若已 import `models` 的其他型別，併進同一行）。

> **注意 fixture 的四個數字刻意互異**（`created: 0, updated: 12, failed: 1, skipped: 7`）。
> 這是為了讓「欄位插錯位置」會被抓到。**不要為了方便把它們改成相同的值**——
> Task 6 就吃過這個虧：`created` 與 `failed` 都是 0，導致兩者換位偵測不到。

### Step 3：跑測試確認失敗

```
cd web
npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/features/settings/settings.component.spec.ts
```

Expected：`Expected $ to contain '略過'` —— 表頭沒有這一欄。

### Step 4：改模板

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

### Step 5：把 reloadJobs 改成 protected

Angular 的嚴格模板檢查不允許模板存取 `private` 成員。`settings.component.ts:262`：

```ts
  private reloadJobs(): void {   →   protected reloadJobs(): void {
```

### Step 6–7：跑測試

```
cd web
npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/features/settings/settings.component.spec.ts
npm test -- --watch=false --browsers=ChromeHeadless
```

Expected：單檔 `TOTAL: 4 SUCCESS`；全部 **`TOTAL: 126 SUCCESS`**。

> **原計畫書寫 108，那是舊數字，忽略它。** 若你實測到別的數字，**回報實際數字，不要改測試去湊**。

### Step 8：Commit

```
git add web/src/app/features/settings/settings.component.ts web/src/app/features/settings/settings.component.spec.ts
git commit -m "feat(web): surface skipped counts and mount the enrich panel"
```

### Task 7 的已知風險

**面板掛上去之後，既有 3 個測試的 `ProviderService` stub 會決定畫面。** 若你漏掉任何一處，
症狀不是「面板沒出現」而是 `TypeError: this.ingestion.providers is not a function` 這種看起來與 Task 7 無關的錯誤。

**`(completed)="reloadJobs()"` 在補完失敗時也會觸發。** 這是刻意的——後端在 job 建立之後失敗仍會留下一筆
`Failed` 紀錄，那正是使用者需要看到的東西。詳見 §6.4。

---

## 5. Task 8：後端讓 IGDB 封面成為可下載的圖片來源

**Files:**
- Modify: `src/MyCollection.Infrastructure/Imaging/ShowcaseImageDownloader.cs`（`ResolveSourceUrl`，約第 104 行）
- Create: `tests/MyCollection.Tests/Unit/ShowcaseImageDownloaderTests.cs`

`ShowcaseImageDownloader` 在品項被設為精選且尚無任何圖片時，下載遠端圖片並設為主圖。
目前只認 `headerUrl` 與 `iconUrl`，兩者都是 Steam 給的——所以走 IGDB 搜尋建檔的實體遊戲永遠拿不到封面。

**這個 Task 與前七個完全獨立**，與 Task 7 無先後關係，可以先做也可以平行做（但兩者的 commit 要分開）。

### 現況核對（我已驗證，2026-08-02）

```csharp
    private static Uri? ResolveSourceUrl(Item item)
    {
        foreach (var key in (string[])["headerUrl", "iconUrl"])
        {
            if (item.Attributes.TryGetValue(key, out var value)
                && value.IsString
                && Uri.TryCreate(value.AsString, UriKind.Absolute, out var uri))
            {
                return uri;
            }
        }

        return null;
    }
```

### Step 1：寫失敗測試

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

### Step 2：跑測試確認失敗

```
dotnet test --filter ShowcaseImageDownloaderTests
```

Expected：編譯失敗，`ResolveSourceUrl` 因為是 `private` 而無法存取。

### Step 3：實作

把

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

**其餘內容不動。**

### Step 4–5：跑測試

```
dotnet test --filter ShowcaseImageDownloaderTests
dotnet build
dotnet test
```

Expected：`Passed: 4`；建置 0 warnings / 0 errors；全部測試 **`通過: 451`**。

> 後端基線 **447**，我在 2026-08-02 親自跑過確認（`已通過! - 失敗: 0，通過: 447`，15 秒）。
> 這個數字剛好與原計畫書一致。若你實測到別的數字，**回報實際數字，不要改測試去湊**。

### Step 6：Commit

```
git add src/MyCollection.Infrastructure/Imaging/ShowcaseImageDownloader.cs tests/MyCollection.Tests/Unit/ShowcaseImageDownloaderTests.cs
git commit -m "feat(showcase): accept igdb covers as a downloadable image source"
```

---

## 6. 這個 session 累積的教訓（請照做，這些都是踩過的坑）

### 6.1 `ProviderService` 的探測必須維持 lazy

`/ingest/providers` 有 `.RequireAuthorization()`。`App` 元件在使用者登入**之前**就會建構，而登入用的是
`router.navigateByUrl()`（SPA，不重載頁面）。`ProviderService` 是 singleton、只探測一次，且 `catchError`
會吞掉 401——**所以任何把探測提前到 bootstrap（`APP_INITIALIZER` / `provideAppInitializer` / `app.ts` 的
`inject()`）的「最佳化」都會讓 `providers` 永久停在 `[]`，登入後 IGDB 入口再也不會出現，而且完全無聲。**

這個 session 犯過一次這個錯並已還原。不要重蹈。

同理：**不要把 IGDB 入口放進公開分享路由 `p/:slug`**——它是不設防的，會再次踩到 401 陷阱。

### 6.2 測試 stub 必須觀察引數

**這個 session 抓到過三次同一個模式**：stub 寫成零參數 arrow（`supports: () => true`、`enrich: () => of(job)`），
結果 provider key、capability、itemIds 全都沒被釘住——把實作改成問完全不同的 provider，測試照樣全綠。

repo 現行慣例（`item-detail.component.spec.ts`、`igdb-enrich.component.spec.ts`）：

```ts
{ provide: ProviderService, useValue: {
    supports: (key: string, capability: string) =>
      key === IGDB_PROVIDER_KEY && capability === 'Search',
} },
```

Task 7 的 `supports: () => false` 是例外——那三處是要讓面板**不渲染**，回傳常數 false 就是全部意圖，
沒有引數需要觀察。

### 6.3 突變測試是唯一可靠的檢查手段

跨 Task 4–6 共實跑 **47 個突變，其中 21 個一開始是存活的**。反覆出現的缺陷型態只有一種：

> **測試名稱宣稱守 A，斷言實際量的是 B。**

幾個代表：
- 測試名說涵蓋 `limit`，把實作寫死成 `20` 照樣全綠
- `enrich(KEY, [id])` 改成 `enrich(KEY)` 全綠 —— 那會去補完 50 筆使用者沒選的品項
- 模板拿掉 `itemId() &&` 全綠 —— 新增品項頁會多冒出一顆不要求先選品類的按鈕
- `notifications.error` 改成 `success` 全綠 —— 測試把兩個通道推進同一個陣列
- `initialAttributes.set(merged)` 整行刪掉全綠 —— 使用者挑了遊戲卻看到空白欄位

**做法**：改一行實作 → 跑測試 → 確認變紅 → **還原並用 `git diff` 驗證位元組相同**（不要肉眼比對）。
不要只用推理判斷測試強不強。

Task 7 建議至少跑這三個：
- 把新的 `<td>{{ job.skipped }}</td>` 插到 `failed` 之後（欄位錯位）
- 把 `<app-igdb-enrich (completed)="reloadJobs()" />` 的 `(completed)` 綁定拿掉
- `colspan` 維持 6

Task 8 建議至少跑這兩個：
- 把 `coverUrl` 放到 `headerUrl` **之前**（優先序反了）
- 把 `coverUrl` 加在 `iconUrl` **之後**

### 6.4 註解寫錯比沒有註解更糟

這個 session 修過三次事實錯誤的註解。最嚴重的一次：註解說 opengraph 品項「結果只會是略過」，
實際上進的是 `failed`——而程式碼的 miss 判斷只認 `skipped`，於是**一個真實的 bug 被錯誤的註解掩護著**。
下一個讀的人會照著錯的理由行動。

寫註解前先去讀你引用的那段程式碼，不要憑印象。

### 6.5 `@if` + `viewChild.required` 的時序陷阱

`@if` 的內容要等**下一次變更偵測**才具現化。所以「翻開關 + 在同一個 handler 裡呼叫子元件方法」
必定拿到 `undefined`，而 `viewChild.required` 會丟 `NG0951: Child query result is required but no value is available`。

`item-detail.component.ts` 的 `<app-igdb-search-dialog>` **刻意不包 `@if`**，只用 `@if` 擋觸發按鈕。
Task 7 的 `<app-igdb-enrich>` 沒有這個問題（它自己內部用 `@if`，外面不需要），但如果你想「最佳化」成
`@if (igdbAvailable()) { <app-igdb-enrich /> }`，先確認沒有人用 `viewChild` 抓它。

### 6.6 `enrich()` 的空陣列等於批次模式

```ts
enrich(provider: string, itemIds?: string[]): Observable<SyncJobDto>
```

送出的 body 是 `{ itemIds: itemIds ?? null }`。**空陣列不會走到 `?? null`**，而後端判斷的是 `Count > 0`
——所以 `[]` 與 `null` 都落在批次分支，一次補完 50 筆。要指定品項就一定要給非空陣列。

---

## 7. 全部完成後的驗證清單

- [ ] `cd web && npm test -- --watch=false --browsers=ChromeHeadless` → `TOTAL: 126 SUCCESS`
- [ ] `cd web && npm run build` → 成功，無新增警告
- [ ] `dotnet build` → 0 warnings / 0 errors
- [ ] `dotnet test` → 現行基線 + 4
- [ ] `git status --short` **只剩 ` M web/angular.json`**
- [ ] Task 7 一個 commit、Task 8 一個 commit，兩者分開

---

## 8. 手動驗證（需真實 IGDB 憑證）

後端的手動驗證從未執行過，因為這個 session 全程沒有憑證。設好 `IGDB_CLIENT_ID` / `IGDB_CLIENT_SECRET` 後：

1. 設定頁應出現「IGDB 補完」面板；沒設憑證時應完全看不到
2. 新增品項頁選「實體遊戲」後，「從 IGDB 搜尋遊戲」按鈕啟用；搜 `the witcher 3` 應看到多張封面
3. 挑一筆後，名稱、描述、開發商、發行商、發售日期、類型、平台、評分應填入表單；儲存不應出現 400
4. 對某個 Steam 同步進來的數位遊戲按「重新從 IGDB 抓取」，該品項的 `tags`、`isShowcased`、`name` 不應改變
5. 把一個用 IGDB 建檔的實體遊戲設為精選，稍候重新整理，它應該長出封面圖（**這條需要 Task 8**）

---

## 9. 不在範圍內的後續工作

- 自訂品類的欄位補齊 UI（後端的兩個端點 `MissingProviderFieldsQuery` / `EnsureProviderFieldsCommand` 已就緒）
- Url 欄位的圖片預覽（需先引入「這個 Url 是圖片」的欄位宣告）
- 搜尋結果分頁與即時輸入搜尋（後者會撞上 IGDB 的 4 req/sec 限制）
- 全站導入 `takeUntilDestroyed`（目前 `web/src/app` 零命中，`IgdbEnrichComponent` 是第一個該收的點——
  批次補完最多 50 次 IGDB 反查，是全站最長的操作，中途離開設定頁的機率比其他地方高）
