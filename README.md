# MyCollection

個人收藏聚合平台。把散在 Steam、櫃子裡、購物網站上的收藏品集中到一個畫面，並產生可分享的公開收藏頁。

收藏品類涵蓋數位遊戲、實體遊戲片/CD、公仔、啦啦隊商品等。**品類的欄位由使用者自行定義**，新增一種收藏不需要改程式或發版。

---

## 核心設計

**品類即 schema。** 每個品類帶一份 `fields` 定義（欄位鍵、標籤、型別、是否必填、是否可搜尋、是否顯示在卡片上）。這份定義同時驅動三處：後端的動態驗證、前端的動態表單、以及篩選側欄。新增「黑膠唱片」品類只需要在 UI 上定義欄位。

**授權寫在倉儲層。** 每個 MongoDB 查詢的 filter 都以擁有者條件開頭，而不是在 handler 裡零星檢查。漏寫的後果是「查無資料」，不是「別人的資料外洩」——讓錯誤往安全的方向倒。

**公開分享頁用投影白名單。** `MongoPublicCatalogReader` 以 `$project` 明確列出可公開的欄位，`acquisition`（購入價格、通路）在資料庫層就被擋掉，不依賴 DTO 記得不要序列化它。

**同步是冪等的。** Steam 同步以 `(ownerId, provider, externalId)` 為鍵做單次 `BulkWrite` upsert：provider 擁有的欄位用 `$set`，使用者擁有的欄位（Showcase 旗標、標籤、購入資訊）用 `$setOnInsert`。重跑同步不會蓋掉你的手動編輯。

---

## 技術棧

| 層 | 內容 |
|---|---|
| 後端 | .NET 10 · ASP.NET Core Minimal API · Clean Architecture + CQRS（MediatR 14）· FluentValidation |
| 資料庫 | MongoDB 8 原生驅動 3.10（不套 EF Core） |
| 前端 | Angular 20.3 · standalone components + signals（無 NgRx）· TypeScript 5.9 |
| 測試 | xUnit · FluentAssertions · Moq · Testcontainers（真 MongoDB）／ Karma + Jasmine |
| 部署 | Docker Compose · nginx |

---

## 專案結構

```
src/
  MyCollection.Domain/          實體與例外，無外部相依
  MyCollection.Application/     CQRS handlers、DTO、驗證、對外介面
  MyCollection.Infrastructure/  MongoDB、檔案儲存、圖片處理、外部 provider
  MyCollection.Api/             端點、DI 組裝、全域例外處理
tests/
  MyCollection.Tests/           單元 + 整合測試（整合測試會起 MongoDB 容器）
web/
  src/app/core/                 models、auth、interceptors、API 服務
  src/app/shared/               DynamicFormComponent 等共用元件
  src/app/features/             showcase、catalog、item-detail、categories、settings、public
docs/
  superpowers/specs/            設計文件
  superpowers/plans/            5 份實作計畫
```

相依方向是 `Domain ← Application ← Infrastructure ← Api`，內層不認識外層。

---

## 快速開始（Docker）

```bash
cp .env.example .env
```

填入兩把金鑰：

```dotenv
JWT_KEY=<任意足夠長的字串>
SECRET_PROTECTION_KEY=<能解碼成 32 bytes 的 Base64>
```

`SECRET_PROTECTION_KEY` **必須**是 32 bytes，它是加密使用者 Steam API Key 的 AES-GCM 金鑰，長度不對 API 啟動就會失敗。產生方式：

```bash
openssl rand -base64 32
```

```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 }))
```

然後：

```bash
docker compose up -d --build
```

開 <http://localhost:8080>。三個容器：`mongo`（資料存 `./data/mongo`）、`api`、`web`（nginx，靜態檔 + 反代 `/api`）。上傳的圖片存在 `./data/media`。

`.env` 已被 `.gitignore` 排除，不會進版控。

---

## 本機開發

需要一個跑著的 MongoDB。API 啟動時會建立索引，連不上就會 fail-fast 而不是延後到第一次查詢才爆。

```bash
docker run -d --name mycollection-dev-mongo -p 27017:27017 mongo:8.0
```

開兩個終端：

```bash
dotnet run --project src/MyCollection.Api    # http://localhost:5080
```

```bash
cd web && npm install && npm start           # http://localhost:4200
```

前端固定呼叫 `/api/...`，開發時由 `web/proxy.conf.json` 轉發到 `localhost:5080` 並剝掉 `/api` 前綴，部署時由 nginx 做同一件事。**所以後端路由本身沒有 `/api` 前綴**（是 `/items` 而非 `/api/items`）。

開發用金鑰在 `appsettings.Development.json`，明確標示為 dev-only。`appsettings.json` 的金鑰欄位一律留空，正式環境用 `Jwt__Key`、`SecretProtection__Key` 環境變數覆寫。

API 文件在 <http://localhost:5080/openapi/v1.json>（僅 Development）。

---

## 測試

```bash
dotnet test                                                  # 259 個
cd web && npm test -- --watch=false --browsers=ChromeHeadless # 35 個
```

整合測試用 Testcontainers 起真的 `mongo:8.0`，第一次跑會拉映像。需要 Docker 在跑。前端測試需要 Chrome。

專案開了 `TreatWarningsAsErrors`。不要用 `NoWarn` 或 `#pragma warning disable` 繞過警告。

---

## 設定

| 區段 | 鍵 | 說明 |
|---|---|---|
| `Mongo` | `ConnectionString`、`Database` | |
| `Jwt` | `Key`、`Issuer`、`Audience`、`AccessTokenMinutes`、`RefreshTokenDays` | `Key` 是 HMAC 簽章金鑰 |
| `Storage` | `Provider`、`LocalRoot` | 第一版僅實作 `Local`；`IFileStorage` 介面預留了換成 GCS 的空間 |
| `SecretProtection` | `Key` | Base64 的 32 bytes，加密外部帳號憑證 |
| `Steam` | `BaseAddress`、`TimeoutSeconds` | |

環境變數用雙底線對應階層：`Mongo__ConnectionString`、`Jwt__Key`。

---

## API 概觀

所有端點都不帶 `/api` 前綴（由 proxy／nginx 補上）。預設需要 Bearer token，匿名端點只有四個：`POST /auth/register`、`POST /auth/login`、`POST /auth/refresh`、`GET /media/{**path}`、`GET /public/{slug}`、`GET /health`。

| 群組 | 端點 | |
|---|---|---|
| 認證 | `POST /auth/register`、`/auth/login`、`/auth/refresh` | 匿名 |
| | `GET /auth/me` | 需 token |
| 品類 | `GET/POST /categories`、`PUT/DELETE /categories/{id}` | |
| 品項 | `GET /items`、`GET /items/tags`、`GET/PUT/DELETE /items/{id}`、`POST /items` | |
| 圖片 | `POST /items/{itemId}/images`、`DELETE .../{imageId}`、`POST .../{imageId}/primary` | |
| 媒體 | `GET /media/{**path}` | 匿名 |
| Showcase | `GET /showcase` | |
| 分享 | `GET/POST /shares`、`DELETE /shares/{id}` | |
| | `GET /public/{slug}` | 匿名 |
| 匯入 | `GET /ingest/providers`、`POST /ingest/sync/steam`、`GET /ingest/jobs`、`POST /ingest/fetch` | |
| 外部帳號 | `GET/POST /external-accounts`、`DELETE /external-accounts/{provider}` | |
| 健康檢查 | `GET /health` | 匿名 |

分享頁的公開端點是 `/public/{slug}` 而非 `/shares/public/{slug}`——它刻意註冊在 `/shares` 群組之外，因為那個群組整組 `RequireAuthorization()`。

品項查詢支援依 schema 屬性篩選：`GET /items?attr.brand=GSC&attr.scale=1/8`。

兩個 provider：`steam`（`POST /ingest/sync/steam`，需先在設定頁綁 API Key 與 SteamID）與 `opengraph`（`POST /ingest/fetch`，貼商品網址自動帶入名稱與描述，不支援批次同步）。分享頁的前端網址是 `/p/{slug}`。

錯誤一律回 RFC 9457 ProblemDetails，由單一 `IExceptionHandler` 產生，不在各處 try-catch。

---

## 第一版不做

位置階層 UI · 估值曲線與匯率 · 保固到期提醒 · PSN 整合 · Discogs/IGDB · CSV 匯入匯出 · 多人共享 group · 行動 App · 虛擬捲動（先用「載入更多」，資料量到數千筆再說）

---

## 文件

設計決策與資料模型見 `docs/superpowers/specs/2026-07-25-mycollection-design.md`。

實作過程拆成 5 份計畫放在 `docs/superpowers/plans/`，執行中發現的環境陷阱與計畫錯誤都已回填進去（例如 nginx `location` 的比對優先序、Angular 測試中 promise 與 observable 銜接需要等待 microtask、`DynamicFormComponent` 的 `[value]` 不可綁動態狀態）。要改動對應區域前值得先讀。
