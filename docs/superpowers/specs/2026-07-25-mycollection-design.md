# MyCollection — 個人收藏聚合平台 · 設計文件

- **日期**：2026-07-25
- **狀態**：已核准
- **專案路徑**：`F:\VibeCode\MyCollection`

## 1. 背景與目標

建立一個個人收藏管理網站，介面與功能參考開源專案 [Homebox](https://github.com/sysadminsmedia/homebox)，但資料模型與外部整合策略依實際需求重新設計。

收藏品類涵蓋：數位遊戲、實體遊戲片/CD、公仔、啦啦隊商品、其他奢侈品。

### 核心價值優先序

| 序 | 價值 | 說明 |
|---|---|---|
| 1 | **統一入口聚合** | 把散在 Steam/PSN/櫃子裡的東西集中在一個畫面 |
| 2 | **展示分享** | 產生可分享的公開收藏頁 |
| 3 | **盤點搜尋** | 我有什麼、放在哪裡 |
| 4 | **資產估值** | 購入價 vs 現值（第一版不做） |

此優先序決定第一版重心：**多來源匯入 + 統一瀏覽 + 可分享**，估值功能往後排。

## 2. 設計決策

| # | 決策點 | 選擇 | 理由 |
|---|---|---|---|
| 1 | 使用範圍 | 多帳號、資料隔離 | 自用為主但支援註冊登入 |
| 2 | 品類建模 | JSON + Schema（非 EAV、非型別繼承） | 品類清單持續演化，新增品類不該需要發版 |
| 3 | 資料庫 | MongoDB（原生 Driver，不套 EF Core Provider） | 與 JSON + Schema 模型天然契合，多一層抽象只會擋路 |
| 4 | 數位遊戲語意 | 分層 Showcase | 全量同步進庫，但首頁/分享頁只顯示精選 |
| 5 | 資料擷取 | `IMetadataProvider` 框架 | 第一版兩個實作：Steam + OpenGraph |
| 6 | 圖片儲存 | 本機檔案 + `IFileStorage` 抽象 | 未來換 Google Cloud Storage 只需新增實作 |
| 7 | 資料隔離 | 簡化為 `ownerId`（不做 `groups`） | 未來要共享時新增 `groupId` 欄位並遷移 |
| 8 | `attributes` 型別 | `BsonDocument` | 天然支援未來的巢狀結構 |

### 2.1 為何選 JSON + Schema

| | EAV | 型別繼承 (TPH/TPT) | **JSON + Schema** |
|---|---|---|---|
| 新增品類 | 免改 code | 要改 code + migration | 免改 code |
| 查詢能力 | 差（多次 self-join） | 最好 | 好 |
| 型別安全 | 無 | 完整 | schema 層驗證 |
| 前端表單 | 需自建 metadata | 每品類手刻 | schema 直接驅動 |

「啦啦隊商品」「其他奢侈品」這類品類本身就是開放式的，型別繼承會讓每次新增品類變成一次發版。而「統一入口」需要跨品類的共同基底 —— `Item` 核心欄位天然提供牆面與分享頁唯一需要的資料。

**代價**：JSON 內欄位改名/型別變更需要遷移腳本，且無法用 FK 約束。對個人收藏規模（數千筆）可接受。

### 2.2 外部資料來源的現實限制

| 來源 | 狀況 | 決定 |
|---|---|---|
| **Steam** | 官方 Web API 穩定。`GetOwnedGames` 需 API Key + SteamID64 + 個人資料設為公開 | ✅ 第一版做 |
| **PSN** | 無官方公開 API。社群方案靠使用者手動從瀏覽器複製 NPSSO cookie 換 token，會過期、Sony 隨時可能擋 | ⏸ 延後，框架預留 |
| **Google** | 沒有「查商品資訊」的 API。Custom Search API 只回網頁連結、每日 100 次免費配額，無法取得結構化商品資料 | ❌ 不採用 |
| **OpenGraph** | 貼上任意商品 URL 抓 `og:title` / `og:image` / `og:description` | ✅ 第一版做，涵蓋多數手動建檔的填表痛苦 |

未來可插拔的 Provider：Discogs（CD，有官方 API）、IGDB（實體遊戲片，有官方 API）、PriceCharting。啦啦隊商品無任何資料源，維持純手動。

## 3. 技術棧

- **後端**：.NET 10 + ASP.NET Core Minimal API + MongoDB.Driver
- **架構**：Clean Architecture + MediatR (CQRS)
- **驗證**：FluentValidation（含由 category schema 動態產生的規則）
- **DTO**：C# `record`（不可變）
- **圖片處理**：ImageSharp，生成 thumb / card / full 三種尺寸
- **韌性**：`Microsoft.Extensions.Http.Resilience`（retry + circuit breaker）
- **前端**：Angular 20 standalone components + signals
- **測試**：xUnit + FluentAssertions + Moq + Testcontainers (MongoDB)
- **部署**：Docker Compose

## 4. 專案結構

```
MyCollection.Domain          實體、值物件、領域規則（零外部相依）
MyCollection.Application     MediatR Handlers、FluentValidation
                             介面：IItemRepository / IFileStorage
                                   IMetadataProvider / IUserContext
MyCollection.Infrastructure  MongoDB Repositories、LocalFileStorage
                             SteamProvider、OpenGraphProvider、ImageSharp 縮圖
MyCollection.Api             Minimal API endpoints、IExceptionHandler、JWT 認證
MyCollection.Tests           xUnit + Testcontainers
web/                         Angular 20
docker-compose.yml
```

## 5. 資料模型

### 5.1 Collections

| Collection | 用途 |
|---|---|
| `users` | 帳號、密碼雜湊 |
| `categories` | 品類 schema 定義，驅動動態表單與篩選器 |
| `items` | 核心收藏品文件 |
| `externalAccounts` | Steam API Key / SteamID，加密存放 |
| `syncJobs` | 同步歷程與結果，供 UI 顯示 |
| `shareLinks` | 公開分享頁的 slug 與可見範圍設定 |

`locations`（位置階層）第一版不建立，但 `items.locationId` 欄位先保留，之後補上不需改文件結構。

### 5.2 `items` 文件

```jsonc
{
  "_id": ObjectId,
  "ownerId": ObjectId,
  "categoryId": ObjectId,
  "name": "初音ミク 1/8 スケール",
  "description": "...",
  "images": [
    { "path": "...", "cardPath": "...", "thumbPath": "...",
      "isPrimary": true, "order": 0 }
  ],
  "tags": ["Good Smile", "VOCALOID"],
  "isShowcased": true,
  "source": "manual",                       // manual | steam | opengraph
  "externalRef": {
    "provider": "steam", "externalId": "440",
    "url": "...", "lastSyncedAt": ISODate
  },
  "acquisition": {
    "acquiredAt": ISODate,
    "price": { "amount": 12800, "currency": "TWD" },
    "vendor": "GSC 官網"
  },
  "locationId": null,
  "attributes": { "brand": "Good Smile", "scale": "1/8" },  // BsonDocument
  "createdAt": ISODate,
  "updatedAt": ISODate
}
```

`attributes` 以 `BsonDocument` 存放，不使用 `Dictionary<string,string>`，因此天然支援任意深度的巢狀結構。

### 5.3 `categories` 文件

```jsonc
{
  "_id": ObjectId,
  "ownerId": ObjectId,          // null = 系統內建品類
  "name": "公仔",
  "icon": "figure",
  "kind": "physical",           // physical | digital
  "fields": [
    {
      "key": "brand", "label": "廠商", "type": "select",
      "options": ["Good Smile", "ALTER", "MegaHouse"],
      "required": true, "searchable": true, "showOnCard": true
    }
  ]
}
```

`kind` 決定該品類是否顯示實體相關欄位（`locationId`、實體狀態）與是否可被 Provider 同步：`digital` 品類的品項 `locationId` 恆為 `null`。

`type` 第一版支援 `text | number | date | select | bool | url`，並預留 `object | array`（資料層不設限，UI 之後補）。

`fields` 同時餵給三處：**Angular 動態表單、FluentValidation 動態規則、篩選器 UI**。新增「啦啦隊商品」品類完全不需要改 code 或發版。

### 5.4 索引

```
{ ownerId:1, isShowcased:1, updatedAt:-1 }             // 首頁牆面
{ ownerId:1, categoryId:1, updatedAt:-1 }              // 品類瀏覽
{ ownerId:1, tags:1 }                                   // 標籤篩選
{ ownerId:1, "externalRef.provider":1,
             "externalRef.externalId":1 }  UNIQUE       // 同步冪等性
{ name:"text", description:"text" }                     // 全文搜尋
```

最後一個 unique 複合索引是同步冪等性的地基，`UpdateOne(..., upsert:true)` 依賴它避免重複品項。

## 6. 模組與 API

| 模組 | 職責 | 端點 |
|---|---|---|
| **Auth** | 註冊/登入、JWT 簽發、密碼雜湊 | `POST /auth/register`、`/auth/login`、`/auth/refresh` |
| **Catalog** | 品項 CRUD、搜尋、篩選、分頁 | `GET/POST/PUT/DELETE /items` |
| **Schema** | 品類定義 CRUD | `GET/POST/PUT /categories` |
| **Media** | 圖片上傳、縮圖生成、刪除 | `POST /items/{id}/images`、`DELETE /items/{id}/images/{imageId}` |
| **Showcase** | 精選牆查詢（跨品類混合） | `GET /showcase` |
| **Sharing** | 分享連結建立、公開唯讀查詢 | `POST /shares`、`GET /public/{slug}` |
| **Ingestion** | Provider 註冊表、同步、URL 擷取 | `POST /ingest/sync/{provider}`、`POST /ingest/fetch?url=` |

每個模組在 Application 層是一個資料夾，各自的 Command / Query + Handler + Validator，彼此只透過 MediatR 與 Repository 介面互動。

### 6.1 `IMetadataProvider` 契約

```csharp
public interface IMetadataProvider
{
    string Key { get; }                        // "steam" | "opengraph"
    ProviderCapability Capabilities { get; }   // BulkSync | UrlLookup | Search

    Task<IReadOnlyList<ExternalItem>> SyncAsync(
        ExternalAccount account, CancellationToken ct);

    Task<ExternalItem?> FetchByUrlAsync(Uri url, CancellationToken ct);
}

public record ExternalItem(
    string ExternalId,
    string Name,
    string? Description,
    Uri? ImageUrl,
    IReadOnlyDictionary<string, object?> Attributes);
```

DI 註冊為 `IEnumerable<IMetadataProvider>`，Ingestion Handler 以 `Key` 解析。新增 PSN 或 Discogs 等於新增一個類別加一行 `AddScoped`，核心零改動。

### 6.2 Steam 同步資料流

```
使用者觸發 → SyncCommand(provider: "steam")
  ↓ 建立 syncJobs 文件 (status: Running)
  ↓ SteamProvider.SyncAsync()
       GET GetOwnedGames?include_appinfo=1  → 300+ 筆 ExternalItem
  ↓ BulkWrite UpsertOne：
       filter:       { ownerId, externalRef.provider, externalRef.externalId }
       $set:         name, attributes.playtime, attributes.iconUrl,
                     externalRef.lastSyncedAt, updatedAt
       $setOnInsert: isShowcased:false, tags:[], source:"steam",
                     acquisition:null, createdAt
  ↓ 更新 syncJobs (status, created:N, updated:M, failed:K)
```

`$setOnInsert` 是**手動編輯不被覆蓋**的機制：使用者設定的 `isShowcased`、`tags`、`acquisition` 不會被後續同步動到。同步只更新 provider 擁有的欄位。

### 6.3 圖片延遲下載

Steam CDN 的圖片 URL 存進 `attributes.iconUrl` 直接引用，**不在同步時下載**。只有當品項被設為 Showcase 時，才觸發背景下載到本地儲存。

- **好處**：避免 300 次無謂的圖片下載與儲存空間浪費
- **代價**：非 Showcase 遊戲的圖片依賴 Steam CDN 存活

### 6.4 分享頁

`shareLinks` 文件：`{ ownerId, slug, scope, includeCategoryIds[], includePrice, expiresAt }`

- `scope: "showcase"`（預設）只輸出 `isShowcased: true` 的品項
- `scope: "category"` 輸出指定品類的全部品項
- `includePrice: false` 為預設 —— 公開 API **後端根本不投影** `acquisition` 欄位，不是靠前端隱藏
- `GET /public/{slug}` 為匿名端點，走**獨立的唯讀 Handler 與獨立的投影 DTO**，不共用內部 Item DTO，避免內部 DTO 新增欄位時意外洩漏購入價

## 7. 前端結構

```
core/          HttpInterceptor(JWT)、error handling、auth guard
shared/        DynamicFormComponent、ItemCard、ImageUploader
features/
  showcase/    首頁瀑布流牆（虛擬捲動）
  catalog/     完整庫存、篩選側欄、grid/list 切換
  item-detail/ 檢視 + 編輯
  categories/  品類 schema 編輯器
  settings/    Steam 帳號綁定、同步紀錄
  public/      分享頁（獨立 layout，無 auth）
```

`DynamicFormComponent` 吃 `CategoryField[]` 產出 Reactive Form，是這個專案唯一需要仔細設計的前端元件。

## 8. 錯誤處理

全域 `IExceptionHandler` 統一轉換為 RFC 9457 ProblemDetails，各層不寫 try-catch：

| 例外 | HTTP | 說明 |
|---|---|---|
| `ValidationException` | 400 | 欄位錯誤明細放 `errors` |
| `NotFoundException` | 404 | |
| `ForbiddenException` | 403 | `ownerId` 不符時拋出 |
| `ProviderException` | 502 | 外部 API 失敗，訊息含 provider key |
| 其他 | 500 | 只記 log，回應不含堆疊 |

**外部 API 韌性**：Steam 呼叫掛 retry(3, 指數退避) + circuit breaker + 10 秒 timeout。同步失敗不中斷使用者流程 —— 寫入 `syncJobs.status = Failed` 與錯誤訊息，UI 在設定頁顯示並提供重試。部分成功如實記錄（`created:120, updated:80, failed:3`），不做全有全無。

**授權是 Repository 層的強制條件**：所有 Mongo filter 一律由 `Builders<Item>.Filter.Eq(x => x.OwnerId, userContext.UserId)` 起頭，`IUserContext` 注入。這比在 Handler 逐一檢查可靠 —— 忘記過濾的後果是查不到資料，而不是洩漏資料。

## 9. 測試策略

| 層 | 方式 |
|---|---|
| Domain / Validators | 純單元測試，xUnit + FluentAssertions |
| Handlers | Moq 掉 Repository 與 Provider，驗證編排邏輯 |
| Repositories | Testcontainers 起真實 MongoDB，驗證索引與 upsert 語意 |
| Providers | `HttpMessageHandler` 假回應 + 錄下的真實 Steam JSON fixture |
| 動態 Schema | schema 定義 → 驗證規則 → 拒絕/接受非法 attributes |

**必要測試**：同一份 Steam 回應連續跑兩次，第二次 `created == 0`，且手動設定的 `isShowcased`、`tags` 未被改動。

## 10. 部署

```yaml
services:
  api:    # ASP.NET Core (.NET 10)，掛 volume ./data/media
  web:    # nginx 送 Angular 靜態檔 + 反代 /api
  mongo:  # 掛 volume ./data/mongo
```

設定走 `IOptions<T>` + 環境變數。`IFileStorage` 由 `Storage:Provider` 設定切換 `Local` / `Gcs`，未來部署到 Google Cloud 只需改環境變數與掛載 service account，應用程式碼零改動。

## 11. 第一版範圍

**做**：註冊登入 · 品類 schema 編輯器 · 品項 CRUD + 動態表單 · 圖片上傳與縮圖 · 標籤與篩選 · 全文搜尋 · Showcase 牆 · 分享連結 · Steam 同步 · OpenGraph URL 擷取

**明確不做（YAGNI）**：位置階層 UI · 估值曲線與匯率 · 保固到期提醒 · PSN 整合 · Discogs/IGDB · CSV 匯入匯出 · 多人共享 group · 行動 App

## 12. 驗收方式

1. `dotnet test` 全綠，含 Testcontainers 整合測試
2. `docker compose up` 後：註冊帳號 → 建立自訂品類 → 手動新增一隻公仔（含圖片）→ 貼商品 URL 驗證 OpenGraph 自動填表
3. 綁定真實 Steam API Key + SteamID → 觸發同步 → 品項數量正確、全部 `isShowcased: false`
4. 將 3 款遊戲與 1 隻公仔設為 Showcase → 首頁牆面正確混合顯示 → Showcase 遊戲的圖片已下載到本地
5. **再次觸發同步** → `syncJobs` 顯示 `created: 0`，且 Showcase 旗標與標籤未被覆蓋
6. 建立分享連結 → 無痕視窗開啟 → 只看得到 Showcase 品項，且回應 payload 不含 `acquisition` 欄位
