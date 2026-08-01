# MyCollection IGDB 遊戲中繼資料整合設計

日期：2026-08-01
狀態：已通過對話設計審核，待書面規格審閱

## 1. 目標

透過 IGDB 取得遊戲的開發商、發行商、發行日、類型、平台、評分與簡介，補上 Steam API 拿不到的資料，並讓手動建檔的實體遊戲不必逐欄手打。

範圍涵蓋兩條使用流程：

- **搜尋建檔**：新增／編輯品項時以關鍵字搜尋 IGDB，挑一筆帶入表單。
- **補完**：對已同步的 Steam 品項批次補上 IGDB 欄位；也可指定品項單筆重抓。

連帶包含 `IMetadataProvider` 契約的介面拆分（見 §3.2）。

不涵蓋：Twitch 使用者登入、docker-compose 的 TLS 設定、IGDB 圖片落地、實體遊戲的自動名稱比對、IGDB 資料的定期自動重抓。

## 2. 前提修正：不需要 HTTPS

原始需求敘述為「Twitch OAuth 驗證需要 HTTPS 重新導向網址，但 docker-compose 不支援」。這個前提不成立。

IGDB 走 Twitch 的 **Client Credentials grant**——純 server-to-server：

```http
POST https://id.twitch.tv/oauth2/token
  ?client_id=…&client_secret=…&grant_type=client_credentials
→ { access_token, expires_in }      // 約 60 天
```

此流程沒有瀏覽器、沒有使用者同意頁、沒有 callback，Redirect URL 從頭到尾不會被使用。Twitch 開發者主控台在註冊應用程式時要求填 OAuth Redirect URL，那是註冊表單的必填欄位，不是流程需求；填 `http://localhost` 即可，若表單擋非 HTTPS 則填 `https://localhost`——反正不會被呼叫。

**因此 docker-compose 不需要 TLS、反向代理憑證或公開網域。**

唯一真正需要 redirect + HTTPS 的情境是「每個使用者用自己的 Twitch 帳號登入」（Authorization Code flow）。IGDB 的遊戲資料是公開資料，不綁使用者身分，不需要這條路。

## 3. 架構決策與理由

### 3.1 憑證全站共用，不進 ExternalAccount

`ExternalAccount` 是「每人一把、AES-GCM 加密」的設計，服務的是 Steam 那種「使用者用自己的 API key 讀自己的資料」。IGDB 讀的是公開遊戲資料庫，跟使用者身分無關；塞進 `ExternalAccount` 會逼每個使用者各自去 Twitch 註冊一個應用程式。

改以環境變數提供，與 `MONGO_CONNECTION_STRING`、`JWT_KEY` 同一機制：

```yaml
# docker-compose.yml，api service 的 environment 追加
  Igdb__ClientId: ${IGDB_CLIENT_ID:-}
  Igdb__ClientSecret: ${IGDB_CLIENT_SECRET:-}
```

使用 `:-`（可空）而非 `:?`（必填）——**IGDB 是選配功能**。未設定時 `IgdbProvider` 不註冊進 DI，`GET /ingest/providers` 不列出 `igdb`，前端據此隱藏入口。功能是否啟用在啟動時就決定完畢，不會出現「註冊了但呼叫時才炸」。

`.env.example` 同步加上兩個空值與申請說明。

### 3.2 拆成能力介面，移除 Capabilities 屬性

現有 `IMetadataProvider` 混了三種不相干的能力，導致每個 provider 都得實作用不到的樁：`SteamProvider.FetchByUrlAsync` 回 `null`、`OpenGraphProvider.SyncAsync` 直接 throw。IGDB 需要 `SearchAsync` 與 `FetchByExternalIdAsync`，直接加上去會讓三個 provider 共 12 個方法裡有 7 個是假實作。

改為單一能力一個介面：

```csharp
public interface IMetadataProvider { string Key { get; } }

public interface IBulkSyncProvider : IMetadataProvider
{
    Task<IReadOnlyList<ExternalItem>> SyncAsync(ExternalAccount account, CancellationToken ct);
}

public interface IUrlLookupProvider : IMetadataProvider
{
    Task<ExternalItem?> FetchByUrlAsync(Uri url, CancellationToken ct);
}

public interface ISearchProvider : IMetadataProvider
{
    /// <summary>標記「此品項已綁定本 provider」的 attribute key，也是批次補完的篩選依據。</summary>
    string MarkerAttributeKey { get; }

    /// <summary>此 provider 寫入 attributes 時，目標品類必須宣告的欄位。</summary>
    IReadOnlyList<CategoryField> RequiredFields { get; }

    Task<IReadOnlyList<ExternalItem>> SearchAsync(string query, int limit, CancellationToken ct);

    /// <summary>
    /// 以 "steam:440" / "igdb:1942" 形式的外部識別碼批次反查，內部自行分塊與節流。
    /// 查無對應者不出現在 Found；請求層級失敗者列入 FailedIds（與「查無」語意不同）。
    /// </summary>
    Task<ExternalLookupResult> FetchByExternalIdsAsync(
        IReadOnlyList<string> externalIds, CancellationToken ct);
}

/// <summary>Found 的 key 是傳入的 externalId。三種結果互斥：命中、查無、請求失敗。</summary>
public record ExternalLookupResult(
    IReadOnlyDictionary<string, ExternalItem> Found,
    IReadOnlyList<string> FailedIds);
```

`ProviderCapability` 旗標**不再由 provider 自行宣告**，改由介面推導。留著 `Capabilities` 屬性等於同一個事實有兩處來源，遲早出現「旗標說支援、方法卻沒實作」；推導後這個 bug 類別在型別層面消失：

```csharp
public static class ProviderCapabilities
{
    public static ProviderCapability Of(IMetadataProvider p) =>
        (p is IBulkSyncProvider  ? ProviderCapability.BulkSync  : ProviderCapability.None)
      | (p is IUrlLookupProvider ? ProviderCapability.UrlLookup : ProviderCapability.None)
      | (p is ISearchProvider    ? ProviderCapability.Search    : ProviderCapability.None);
}
```

`ProviderRegistry` 的能力多載改為泛型，回傳強型別：

```csharp
public T Require<T>(string key) where T : class, IMetadataProvider =>
    Require(key) as T
    ?? throw new ProviderException(key, $"Provider '{key}' does not support {typeof(T).Name}.");
```

`ProviderCapability` enum 本身保留，`GET /ingest/providers` 仍回傳旗標供前端決定顯示哪些入口。

修正成本現在只有兩個 provider；等 Discogs、PSN 進來就是四五個。淨結果是刪掉的程式碼多於新增的：兩個假實作、兩個 `Capabilities` 屬性、三個「不支援」測試。

### 3.3 只做精準比對，不做名稱猜測

- **數位遊戲**（Steam 同步）有 Steam appid，可精準反查 IGDB id，零歧義。
- **實體遊戲**（手動建檔）沒有外部 id，只剩名稱。「Final Fantasy VII」在 IGDB 有原版、重製版、國際版與各種移植，名稱比對必然猜錯，且錯了難以發現。

因此**批次補完只處理有 Steam appid 的品項**；實體遊戲一律走搜尋建檔，由使用者從搜尋結果自行挑選，挑選當下即綁定 `igdbId`。綁定後之後重抓都精準。

替代方案「名稱搜尋結果進待確認佇列」被排除：確認佇列做的事與建檔時挑選是同一件事，只是時機晚了，卻要多做一整套確認 UI。

「名稱搜尋取第一筆」被排除：會安靜產生錯誤資料，違反專案既有原則（`$setOnInsert` 保護手動編輯、公開頁白名單投影——都是「寧可少做也不要做錯」）。

### 3.4 Token 存記憶體，不落地

Twitch app access token 有效期約 60 天。以記憶體快取即可，重啟成本是一次額外請求，換來零狀態管理。

- `SemaphoreSlim` 做 single-flight，避免並發時打出多次 token 請求。
- 距到期 **5 分鐘內**視為過期，主動換新。
- IGDB 回 **401** 時 `Invalidate()` 並**重試一次**——涵蓋「Twitch 端提前撤銷 token」這個以時間算不出來的情況。重試僅限一次，避免憑證真的錯誤時無限迴圈。

```csharp
// Infrastructure/Providers/Igdb/ITwitchTokenProvider.cs
internal interface ITwitchTokenProvider
{
    Task<string> GetAsync(CancellationToken ct);
    void Invalidate();
}
```

### 3.5 系統品類內建 IGDB 欄位，不做執行期追加

`AttributeValidator` 會擋掉品類未宣告的 attribute key，所以寫入前必須確保目標品類宣告了 IGDB 那組欄位。

「實體遊戲」與「數位遊戲」都是**系統品類**（`SystemCategoryDefinitions`，固定 ObjectId，`OwnerId == null`），而 `MongoCategoryRepository.UpdateAsync` 對系統品類擲 `ForbiddenException`——任何執行期的「追加欄位」端點都會在最需要它的兩個品類上失敗。

因此 IGDB 欄位直接寫進 `SystemCategoryDefinitions`。`SystemCategorySeeder` 每次啟動以 `$set` 覆寫整份 `Fields`，既有部署重啟即自動補齊，不需要遷移腳本。系統品類的 schema 本來就由程式定義、使用者不可編輯，這與「品類即 schema」並不衝突——不可編輯是刻意的設計。

兩個品類**已宣告** `developer`、`publisher`、`releaseDate`（標籤為「發售日期」），只需新增 `igdbId`、`genres`、`platforms`、`igdbRating`、`coverUrl` 五個 key。沿用既有標籤，不另立同義欄位。

自訂品類不在此列。使用者若在自訂品類使用 IGDB，功能**優雅降級**：只填該品類已宣告的欄位，其餘丟棄；要完整資料就自行在品類編輯器加欄位。執行期的 `missing-fields` / `ensure-fields` 端點列為可選（§4.3）。

欄位定義由 provider 透過 `ISearchProvider.RequiredFields` 提供，`SystemCategoryDefinitions` 與該屬性共用同一份靜態定義，不散落兩處。

### 3.6 補完沿用 SyncJob，新增 Skipped 計數

補完結果寫進既有的 `SyncJob`（`Provider = "igdb"`），前端在同一個同步歷程列表即可看到，不必另做一套進度 UI。

`SyncJob` 需要新增 `Skipped` 欄位。「IGDB 查無此遊戲」不是失敗而是正常結果，混進 `Failed` 會讓使用者誤以為出事。

## 4. 功能規格

### 4.1 搜尋建檔

```http
GET /ingest/search?provider=igdb&q=witcher%203&limit=20  →  ExternalItemDto[]
```

前端在新增／編輯品項頁放一顆**次要**按鈕「從 IGDB 帶入」，開 modal 搜尋、挑一筆、預填表單。使用者仍可在存檔前修改任何欄位。

這顆按鈕在**所有品類**都出現，不做品類過濾。目前只有一個 `ISearchProvider`，品類層級的來源設定實質上只是布林值，卻要動 `Category` 實體、DTO 與品類編輯器 UI。噪音成本僅為公仔等品類多一顆用不到的按鈕。等第二個搜尋型 provider（如 Discogs）出現、真的變成「選哪一個來源」時再加。

建出的品項 `Source = ItemSource.Manual`，**不新增 `ItemSource.Igdb`**。IGDB 沒有「你的收藏」概念，不會有後續同步覆蓋它；標成 IGDB 來源會讓 `$setOnInsert` 那套保護機制的語意錯亂。來源資訊靠 `igdbId` attribute 保留。

### 4.2 補完

```http
POST /ingest/enrich/igdb   { itemIds?: string[], limit?: number }   →  SyncJobDto
```

- **不給 `itemIds`**（批次）：查 `externalRef.provider == "steam"` 且 attributes 沒有 `igdbId` 的品項，取 `limit` 筆（預設 50、上限 200）。可重複執行至清空。
- **給 `itemIds`**（單筆／重抓）：不論是否已有 `igdbId` 都重抓，用於 IGDB 資料更新後刷新。

每筆處理步驟：

1. 取得 IGDB id：
   - 品項 attributes 已有 `igdbId`（搜尋建檔而來，或先前補完過）→ **直接使用**，不查 Steam
   - 否則以 `item.ExternalRef.ExternalId` 作為 Steam appid → `"steam:440"`
   - 兩者皆無（手動建檔且未綁定過）→ `Skipped++`。這類品項應走搜尋建檔綁定，補完不猜
2. 整批送進 `FetchByExternalIdsAsync`，由 provider 分塊（每塊 10 筆）與節流
3. 映射成 attributes（見 §5）
4. `$set` 只寫 IGDB 擁有的 key。**不碰** `name`（Steam 的名稱是使用者在庫裡認得的那個）、**不碰** `tags` / `isShowcased` / `acquisition` / `images` / `createdAt`
5. `description` **僅在目前為空時**寫入 IGDB `summary`——使用者寫過的心得不該被英文簡介蓋掉
6. 查無結果 → `Skipped++`，不寫入任何欄位

批次使用 IGDB 的多值查詢（一次帶 10 個 appid），50 筆約 5–10 個請求，遠低於 4 req/sec 限制。仍加一道 `SemaphoreSlim` + 250ms 間隔的節流閘：速率超標的懲罰是整段時間被擋，代價不對稱。

批次為**逐筆容錯**：單一批次請求失敗只影響該批的 10 筆，記入 `Failed`，其餘照跑。與 Steam 同步「單次 `BulkWrite` 部分成功如實記錄」的既有作法一致。

### 4.3 自訂品類的欄位補齊（可選）

系統的「實體遊戲」與「數位遊戲」已內建 IGDB 欄位（§3.5），兩條主要流程都不需要這組端點。以下僅服務「使用者自訂品類 + 想用 IGDB」這個尚未出現的情境：

```http
GET  /categories/{id}/missing-fields?provider=igdb  →  CategoryField[]
POST /categories/{id}/ensure-fields  { provider: "igdb" }  →  CategoryDto
```

`ensure-fields` 只追加缺少的 key，**不覆寫使用者改過的 `Label`**，也不重複加已存在的欄位；對系統品類回傳 403（由 `ICategoryRepository.UpdateAsync` 既有守衛負責）。

**此項為可選範圍**，不做也不影響 §4.1 與 §4.2。

## 5. 欄位映射

| attribute key | 標籤 | 型別 | IGDB 來源 |
|---|---|---|---|
| `igdbId` | IGDB ID | Number | `id`。綁定用，UI 隱藏 |
| `developer` | 開發商 | Text | `involved_companies` 中 `developer == true` 的第一筆 `company.name` |
| `publisher` | 發行商 | Text | 同上，`publisher == true` |
| `releaseDate` | 發售日期 | Date | `first_release_date`（Unix 秒 → `DateTime`） |
| `genres` | 類型 | Text | `genres.name` 逗號串接，如「角色扮演, 冒險」 |
| `platforms` | 發行平台 | Text | `platforms.abbreviation` 逗號串接 |
| `igdbRating` | IGDB 評分 | Number | `total_rating`，0–100 |
| `coverUrl` | IGDB 封面網址 | Url | `https://images.igdb.com/igdb/image/upload/t_cover_big/{cover.image_id}.jpg` |

`summary` 寫進 `Item.Description`，不佔 attribute。**內容為英文**——IGDB 的中文資料極少，已確認接受。

`developer`、`publisher`、`releaseDate` 三個 key 兩個系統遊戲品類**已經宣告**，沿用其既有標籤（「發售日期」而非「發行日」），不另立同義欄位。

`platforms`（IGDB 的「這款遊戲發行於哪些平台」）與系統品類既有的 `platform`（「我這一份收藏在哪個平台／商店」）語意不同，兩者並存，標籤分別為「發行平台」與「平台」／「平台／商店」。

`genres` 與 `platforms` 維持逗號串接的 Text，**不寫入 `Item.Tags`**。`Tags` 是使用者擁有的欄位（受 `$setOnInsert` 保護），provider 不該碰它。

`coverUrl` 與 Steam 同步寫入的 `headerUrl` **兩者並存**：IGDB 封面是直式書封（600×800）適合 Showcase 牆，Steam header 是橫式（460×215）適合列表。

刻意排除：`storyline`（與 summary 重複且冗長）、`screenshots` / `videos`（已有自建媒體上傳機制）、`similar_games` / `franchises`（個人收藏情境無查詢價值）、`age_ratings`（台灣分級不在 IGDB 資料內）。

IGDB 封面**不落地**，只存 URL，與 Steam `headerUrl` 一致。若日後要落地則走既有的 `ShowcaseImageQueue`，不另做機制。

## 6. 錯誤處理

沿用既有的 `ProviderException` 與全域例外處理，不在 handler 包 try-catch。

| 情況 | 處理 |
|---|---|
| Twitch token 請求失敗 | `ProviderException("igdb", …)`，作業標 `Failed` |
| IGDB 回 401 | `Invalidate()` token → 重試一次；再失敗才擲例外 |
| IGDB 回 429 | 擲 `ProviderException`。已有節流閘，撞到 429 表示設定有問題，該顯眼而非安靜退避 |
| IGDB 回其他 4xx／5xx | `ProviderException`，帶上狀態碼 |
| 逾時（10 秒） | 同上 |
| 批次中單筆查無結果 | `Skipped++`，繼續 |
| IGDB 未設定 | Provider 不註冊 → `Require<ISearchProvider>("igdb")` 擲 `NotFoundException` → 404。前端本就不顯示入口 |

## 7. 已知不確定處

**Steam appid → IGDB id 的查法，實作時必須對真實 API 確認。** 兩個候選：

```apicalypse
# 舊式：external_games 端點（穩定多年，但 category 欄位傳出正被 game_type 取代）
POST /v4/external_games
fields game, uid; where category = 1 & uid = ("440","620");

# 新式：games 端點的 external 欄位（IGDB changelog 提及）
POST /v4/games
fields name, external.steam, …; where external.steam = ("440","620");
```

這是整份設計中唯一無法從文件確定的部分。因應方式：**全部封裝在 `IgdbProvider.FetchByExternalIdAsync` 一個方法內**。實作時先用真實憑證各打一次確認何者可用，再錄成 fixture。查法變更只需改這一個方法，映射、補完與寫入邏輯皆不受影響。

## 8. 必要測試

| 測試 | 型態 | 重點 |
|---|---|---|
| `TwitchTokenProviderTests` | Unit | 快取命中不重打、到期前 5 分鐘換新、`Invalidate` 後重取、10 個並發只打 1 次 |
| `IgdbProviderTests` | Unit（`StubHttpMessageHandler` + fixture） | 搜尋結果映射、`first_release_date` Unix→`DateTime`、`involved_companies` 取 developer/publisher、cover URL 組裝、查無結果回 null、401 重試一次、429/500 → `ProviderException` |
| `ProviderRegistryTests` | Unit | `Require<T>` 型別相符回實例、不符擲 `ProviderException`；`ProviderCapabilities.Of` 推導正確 |
| `EnrichCommandHandlerTests` | Unit（Moq 假 provider） | `Skipped` 計數、不覆寫 `name`、`description` 僅在空時寫、`itemIds` 與批次兩條路徑 |
| `MongoItemEnrichWriterTests` | Integration（Testcontainers 真 Mongo） | `$set` 只碰 IGDB 欄位；`tags` / `isShowcased` / `acquisition` / `images` / `createdAt` 原封不動 |
| `IgdbRateLimiterTests` | Unit（`FakeTimeProvider`） | 首次立即放行、未達間隔時擋住、時間推進後放行 |
| `SystemCategoryDefinitionsTests` | Unit | 兩個遊戲品類宣告了 `IgdbFields` 的全部 key |
| `EnsureCategoryFieldsTests` | Unit（可選） | 缺欄位偵測、已存在欄位不重複加、不覆寫使用者改過的 `Label` |

新增 fixture：`igdb-search-witcher.json`、`igdb-external-steam.json`、`twitch-token.json`。沿用既有 `Fixtures/*.json` + `CopyToOutputDirectory` 作法，使用**真實錄下的回應**而非手寫。

既有測試的連帶修改：`SteamProviderTests` 刪除 `FetchByUrl_is_not_supported`、`OpenGraphProviderTests` 刪除 `Sync_is_not_supported`、`ProviderRegistryTests` 改寫能力檢查案例。

## 9. 明確不做

- Twitch Authorization Code flow／使用者登入 Twitch
- 改 docker-compose 加 TLS 或反向代理憑證（原始前提，見 §2）
- IGDB 封面圖落地
- `screenshots` / `videos` / `similar_games` / `franchises` / `age_ratings`
- 實體遊戲的名稱模糊比對與確認佇列
- 品類層級的「預設中繼資料來源」設定
- IGDB 資料的定期自動重抓（開發商與發行日不會變，手動觸發即可）

## 10. 檔案結構

| 檔案 | 職責 |
|---|---|
| `Application/Ingestion/IMetadataProvider.cs` | **改**：拆為 4 個介面，移除 `Capabilities` |
| `Application/Ingestion/ProviderCapabilities.cs` | **新**：從介面推導旗標 |
| `Application/Ingestion/ProviderRegistry.cs` | **改**：`Require<T>` |
| `Application/Ingestion/SearchQuery.cs` | **新**：`GET /ingest/search` |
| `Application/Ingestion/EnrichCommand.cs` | **新**：批次／單筆補完編排 |
| `Application/Ingestion/IItemEnrichWriter.cs` | **新**：只寫 provider 欄位的 bulk update |
| `Application/Categories/EnsureProviderFieldsCommand.cs` | **新**（可選）：`missing-fields` 與 `ensure-fields` |
| `Infrastructure/Mongo/SystemCategoryDefinitions.cs` | **改**：兩個遊戲品類加 5 個 IGDB 欄位 |
| `Infrastructure/Providers/Igdb/IgdbFields.cs` | **新**：IGDB 欄位的唯一定義來源 |
| `Infrastructure/Providers/Igdb/IgdbRateLimiter.cs` | **新**：程序層級最小請求間隔 |
| `Infrastructure/Providers/Igdb/IgdbOptions.cs` | **新** |
| `Infrastructure/Providers/Igdb/TwitchTokenProvider.cs` | **新**：token 快取與 single-flight |
| `Infrastructure/Providers/Igdb/IgdbProvider.cs` | **新**：`ISearchProvider` 實作 |
| `Infrastructure/Providers/Igdb/IgdbMapper.cs` | **新**：IGDB JSON → `ExternalItem` |
| `Infrastructure/Mongo/MongoItemEnrichWriter.cs` | **新** |
| `Infrastructure/Providers/SteamProvider.cs` | **改**：改實作 `IBulkSyncProvider`，刪樁與 `Capabilities` |
| `Infrastructure/Providers/OpenGraphProvider.cs` | **改**：改實作 `IUrlLookupProvider`，刪樁與 `Capabilities` |
| `Application/Ingestion/SyncCommand.cs` | **改**：`Require<IBulkSyncProvider>` |
| `Application/Ingestion/FetchByUrlQuery.cs` | **改**：`Require<IUrlLookupProvider>` |
| `Api/Endpoints/IngestionEndpoints.cs` | **改**：`/search`、`/enrich/igdb`；`/providers` 改用 `ProviderCapabilities.Of` |
| `Api/Endpoints/CategoryEndpoints.cs` | **改**（可選）：`missing-fields`、`ensure-fields` |
| `Domain/Entities/SyncJob.cs` | **改**：新增 `Skipped` |
| `web/…/item-form` | **改**：IGDB 搜尋 modal 與欄位確認對話框 |
| `web/…/settings.component.ts` | **改**：批次補完按鈕 |
| `docker-compose.yml`、`.env.example` | **改**：兩個環境變數 |
