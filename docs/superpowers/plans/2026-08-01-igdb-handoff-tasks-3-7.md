# 交接說明：IGDB 整合 Task 3–7

給接手實作 Task 3–7 的 agent。Task 1–2 已完成並通過審查。

---

## 1. 你要做什麼

實作 `docs/superpowers/plans/2026-08-01-igdb-metadata-backend.md` 的 **Task 3 至 Task 7**：

| Task | 產出 | 需要憑證？ |
|---|---|---|
| 3 | `IgdbOptions`、`IgdbFields`（IGDB 欄位定義的唯一來源） | 否 |
| 4 | `TwitchTokenProvider`（client credentials、快取、single-flight） | 否 |
| 5 | `IgdbRateLimiter`（程序層級最小請求間隔） | 否 |
| 6 | `IgdbMapper`（IGDB JSON → `ExternalItem`） | 否 |
| 7 | `IgdbProvider`（`ISearchProvider` 實作） | **是** |

**計畫已寫好每個 Task 的完整程式碼與測試碼**，逐步驟列出，包含每一步要跑的指令與預期輸出。照著做即可，不需要自己設計。

Task 8–14 **不要碰**。

## 2. 先讀這兩份文件

1. `docs/superpowers/plans/2026-08-01-igdb-metadata-backend.md` — 實作計畫，Task 3–7 的段落
2. `docs/superpowers/specs/2026-08-01-igdb-metadata-design.md` — 設計文件與決策理由

計畫寫「做什麼」，spec 寫「為什麼」。遇到計畫某個決定看起來奇怪時，答案通常在 spec。

## 3. 環境

- **路徑**：`f:\VibeCode\MyCollection`
- **分支**：`mongoAtlas`（不是 master，可直接 commit）
- **作業系統**：Windows 11。shell 是 PowerShell 7 為主，也有 Git Bash
- **框架**：.NET 10，方案檔 `MyCollection.slnx`
- **建置**：`dotnet build`
- **測試**：`dotnet test`（從 repo 根目錄）
- **Docker 必須可用** — 整合測試用 Testcontainers 起真的 MongoDB。若 Docker 不可用，改跑 `dotnet test --filter "FullyQualifiedName!~Integration"` 並在回報中明說沒跑整合測試

### 目前基準線

```
HEAD          41bc6c9
測試          317 passed / 0 failed / 0 skipped
dotnet build  0 warnings / 0 errors
```

**任何時候測試數低於 317 或出現失敗，就是你弄壞了東西。**

工作區有一個與本任務無關的未提交變更：`web/angular.json`。**不要 stage、不要 commit、不要還原它。**

## 4. 架構與慣例

Clean Architecture，相依方向 `Domain ← Application ← Infrastructure ← Api`，內層不認識外層。

```
src/MyCollection.Domain/          實體與例外，無外部相依
src/MyCollection.Application/     CQRS handlers、DTO、驗證、對外介面
src/MyCollection.Infrastructure/  MongoDB、檔案儲存、圖片處理、外部 provider
src/MyCollection.Api/             端點、DI 組裝、全域例外處理
tests/MyCollection.Tests/         Unit/ 與 Integration/
```

技術棧：ASP.NET Core Minimal API、MediatR 14、FluentValidation、MongoDB 原生驅動 3.10（**不用 EF Core**）、xUnit + FluentAssertions + Moq + `FakeTimeProvider`（`Microsoft.Extensions.TimeProvider.Testing`）+ Testcontainers。

### 硬性慣例

- **程式碼註解用繁體中文（台灣）**，commit message 用英文。看既有檔案就知道語氣
- **不在每個方法包 try-catch**，有全域例外處理（`src/MyCollection.Api/GlobalExceptionHandler.cs`）
- 所有 I/O **一律 async/await**
- 外部 provider 的失敗一律包成 `ProviderException(providerKey, message, inner?)`
- Response/DTO 用 `record`，request 驗證用 FluentValidation
- Mapping 手動寫，不用 AutoMapper
- 遵循 Microsoft C# Coding Conventions
- 時間一律注入 `TimeProvider`，**不要直接呼叫 `DateTime.UtcNow`**（測試用 `FakeTimeProvider`）

### TDD 是硬性要求

計畫的每個 Task 都是這個順序，不要跳步：

1. 寫失敗測試（測試碼計畫裡已經給了，照抄）
2. **跑一次確認它真的失敗**，並確認失敗原因是預期的那個
3. 寫最小實作
4. 跑測試確認通過，比對計畫寫的預期數字
5. Commit（訊息計畫裡也給了）

**一個 Task 一個 commit。** 只 stage `src` 與 `tests`。

## 5. Task 1–2 已經給你的東西

### 能力介面（Task 1 產出）

`src/MyCollection.Application/Ingestion/IMetadataProvider.cs` 已經拆好了。你在 Task 7 要實作的是 `ISearchProvider`：

```csharp
public interface IMetadataProvider { string Key { get; } }

public interface ISearchProvider : IMetadataProvider
{
    string MarkerAttributeKey { get; }
    IReadOnlyList<CategoryField> RequiredFields { get; }
    Task<IReadOnlyList<ExternalItem>> SearchAsync(string query, int limit, CancellationToken ct);
    Task<ExternalLookupResult> FetchByExternalIdsAsync(
        IReadOnlyList<string> externalIds, CancellationToken ct);
}
```

`ExternalItem` 與 `ExternalLookupResult` 都已定義在同一個檔案。**目前沒有任何 `ISearchProvider` 的實作，你的 `IgdbProvider` 會是第一個。**

其他相關型別：
- `ProviderKeys.Steam / OpenGraph / Igdb` — provider key 常數，不要寫字面值
- `ProviderCapabilities.Of(provider)` — 由介面推導能力旗標
- `ProviderRegistry.Require<T>(key)` — 強型別解析
- `SyncJob.Skipped`（Task 2 產出）— Task 3–7 用不到，Task 12 才會用

### 可參考的既有實作

寫 `IgdbProvider` 時，`src/MyCollection.Infrastructure/Providers/SteamProvider.cs` 是最貼近的範本：HttpClient 注入、錯誤包裝、私有 record 反序列化、fixture 測試。

## 6. Task 7 的前置條件與唯一未知

### 你需要 Twitch 憑證

到 [dev.twitch.tv/console/apps](https://dev.twitch.tv/console/apps) 註冊應用程式（Twitch 帳號需先啟用兩階段驗證）：

- **OAuth Redirect URL 填 `http://localhost`** — 這個欄位只是註冊表單的必填項。IGDB 走 client credentials（server-to-server），流程沒有瀏覽器、沒有 callback，這個網址從頭到尾不會被使用。**不需要 HTTPS、不需要公開網域、不需要改 docker-compose**
- Category 選 Application Integration

取得 Client ID 與 Client Secret。若使用者尚未提供，**先問，不要猜或跳過 Task 7**。

### 整份設計唯一無法從文件確定的部分

Steam appid → IGDB game id 的查法。IGDB 文件對 `external_games.category` 是否已被 `game_type` 取代講得不清楚。

**Task 7 開頭要求你先用真憑證各打一次以下兩種查詢，確認哪一種可用**，再據以實作，並把真實回應錄成 fixture：

```bash
# 候選 A：external_games 端點（穩定多年）
curl -X POST 'https://api.igdb.com/v4/external_games' \
  -H "Client-ID: $IGDB_CLIENT_ID" -H "Authorization: Bearer $TOKEN" \
  -d 'fields game,uid; where category = 1 & uid = ("440","620"); limit 500;'

# 候選 B：games 端點的 external 欄位
curl -X POST 'https://api.igdb.com/v4/games' \
  -H "Client-ID: $IGDB_CLIENT_ID" -H "Authorization: Bearer $TOKEN" \
  -d 'fields name,external.steam; where external.steam = ("440","620"); limit 500;'
```

計畫以候選 A 撰寫。若實測是候選 B 可用，只需改 `IgdbProvider.ResolveSteamAsync` 一個方法與 fixture 內容，其餘不受影響——這正是計畫刻意把它隔離在單一方法後面的原因。

**實測結果請回報**，不論哪個可用。這會回頭修正設計文件 §7。

## 7. 容易踩的地雷

1. **`TwitchTokenProvider` 與 `IgdbRateLimiter` 必須是 singleton。**
   前者若是 scoped/transient，快取每次都是空的，等於每個請求都跟 Twitch 換一次 token；後者若非 singleton，每個請求各自一份節流器，等於沒有節流。
   因此 `TwitchTokenProvider` **不能**用 `AddHttpClient<T>`（那是 transient），要注入 `IHttpClientFactory` 並用具名 client。這在 Task 13 的 DI 註冊才會做，但 Task 4 的建構子簽章現在就要對。

2. **`IgdbFields.All` 與 `Create()` 的差別。**
   `CategoryField` 是可變類別。`All` 是共用快照，`Create()` 每次回傳新實例。要交給別處持有或寫進資料庫的一律用 `Create()`，否則會被改到。Task 3 有一個測試專門驗證這件事。

3. **`AttributeValidator` 會擋掉品類未宣告的 attribute key**（見 `src/MyCollection.Application/Items/AttributeValidator.cs`）。這是 `IgdbFields` 存在的理由。Task 3–7 不直接受影響，但這解釋了為什麼欄位定義要集中。

4. **`IgdbMapper` 缺席的欄位要省略 key，不要寫 null。** Task 6 有測試驗證。

5. **Task 7 Step 3 會修改 `tests/MyCollection.Tests/Fixtures/StubHttpMessageHandler.cs`**（加上記錄 request body 與 headers，`SendAsync` 改成 async）。這個檔案是 `SteamProviderTests` 與 `OpenGraphProviderTests` 共用的。改完務必重跑那兩個測試類別，不要只跑 `IgdbProviderTests`。

6. **APIcalypse 查詢的字串以雙引號界定、分號斷句。** 使用者輸入必須把 `"`、`;`、換行拿掉，否則可以改寫整段查詢。Task 7 的 `Sanitize` 方法負責，有測試涵蓋。

7. **不要提前實作 Task 8–14 的東西。** 特別是不要寫 `MongoItemEnrichWriter`、`EnrichCommand`、DI 註冊，也不要動 `SystemCategoryDefinitions`。Task 3–7 結束時 `IgdbProvider` 還沒有註冊進 DI，這是正常的。

## 8. 回報時要包含什麼

每個 Task 完成後：

- 實際的測試輸出摘要（貼真的輸出，不要轉述），以及全量 `dotnet test` 的數字
- 改動的檔案清單，包含計畫沒列到但你不得不改的（Task 1 就發生過，計畫漏了 4 個呼叫端）
- commit SHA
- 任何與計畫不符之處

計畫的檔案清單與預期測試數是我根據當時的程式碼推導的，**可能不完整**。以編譯器和實際測試結果為準，不要為了符合計畫的數字去改測試。發現計畫錯了就回報，那是有價值的發現。

若你判斷某個 Task 的做法有問題，**先講再做**，不要默默改設計。

## 9. 完成後的驗收

- [ ] `dotnet build` 0 errors、無新增 warnings
- [ ] `dotnet test` 全綠，總數 ≥ 317 加上你新增的測試數
- [ ] Task 3–7 各自一個 commit
- [ ] `web/angular.json` 仍為未提交狀態且內容未被更動
- [ ] `IgdbProvider` 尚未註冊進 DI（Task 13 才做），`/ingest/providers` 仍不會列出 `igdb`
- [ ] Steam appid 反查的實測結果已回報
