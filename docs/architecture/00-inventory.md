# 專案盤點（階段 1）

> 範圍：整個 repo，排除 `tests/` 與 `node_modules/`。只讀結構性檔案，業務邏輯留待階段 2。
> 盤點日期：2026-09-26（commit `61b1f5d`）

## 系統一句話

個人收藏品管理系統：使用者登入後管理分類與收藏項目（含圖片），可從 Steam / PSN / IGDB / OpenGraph 匯入與補完 metadata，並以公開連結分享收藏。
【推論】依據：endpoint 群組 `/categories`、`/items`、`/ingest`、`/shares`、`/public/{slug}`（`src/MyCollection.Api/Endpoints/*.cs`）與 provider 實作（`src/MyCollection.Infrastructure/Providers/`）。

## 方案結構

方案檔：`MyCollection.slnx`（新版 XML solution 格式）。共用建置設定：`Directory.Build.props` → `net10.0`、`Nullable=enable`、`TreatWarningsAsErrors=true`、`InvariantGlobalization=true`。

| 專案 | 類型 | TargetFramework | 職責（一句話） | 依賴的專案 |
|------|------|-----------------|----------------|------------|
| `MyCollection.Domain` | Class library | net10.0 | Entities（`Category`、`ExternalAccount`、`Item`、`ShareLink`、`SyncJob`、`User`）與 domain exceptions | — （僅 `MongoDB.Bson` 3.12.0） |
| `MyCollection.Application` | Class library | net10.0 | CQRS handlers（MediatR 14.2.0）、FluentValidation 12.1.1、repository／外部服務介面 | Domain |
| `MyCollection.Infrastructure` | Class library | net10.0 | Mongo repositories、JWT/PBKDF2/AES-GCM、GCS/Local 檔案儲存、ImageSharp、外部 metadata providers、Cloud Tasks | Application |
| `MyCollection.Api` | ASP.NET Core Web（Minimal API） | net10.0 | 組態、認證、CORS、endpoint 對映、健康檢查、啟動時建 index 與 seed | Infrastructure |

證據：各 `src/*/*.csproj` 的 `ProjectReference` / `PackageReference`。

### 主要套件
- Api：`Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12、`Microsoft.AspNetCore.OpenApi` 10.0.12、`Microsoft.OpenApi` 2.12.2（釘選）
- Infrastructure：`MongoDB.Driver` 3.12.0、`Google.Cloud.Storage.V1` 4.15.0、`Google.Cloud.Tasks.V2` 3.6.0、`Google.Apis.Auth` 1.76.0、`Microsoft.Extensions.Http.Resilience` 10.10.0（Polly）、`SixLabors.ImageSharp` 3.1.12、`AngleSharp` 1.8.2、`System.IdentityModel.Tokens.Jwt` 8.23.0

### 啟動管線
`src/MyCollection.Api/Program.cs`：
1. `MongoConventions.Register()` 必須先於任何 BSON 序列化（camelCase 慣例）。
2. `AddApplication()` → `AddInfrastructure(configuration)`。
3. `IUserContext` 以 `ScopedUserContext` 包 `BackgroundUserContext` + `HttpUserContext`，存取當下才決定來源。
4. `AddProblemDetails()` + `GlobalExceptionHandler`（全域例外處理）。
5. CORS policy `Web`（來源：`Cors:AllowedOrigins`）、ForwardedHeaders（`ForwardLimit=1`，信任任意 proxy）。
6. JWT Bearer（`MapInboundClaims=false`、`ClockSkew=30s`）。
7. `FormOptions.MultipartBodyLengthLimit = long.MaxValue`（為匯入端點放寬）。
8. 啟動時 `MongoIndexInitializer.EnsureIndexesAsync` + `SystemCategorySeeder.SeedAsync`，完成後 `StartupHealthState.MarkReady()`。
9. 健康檢查：`/health/live`、`/health/startup`（未就緒回 503）、`/health` → redirect。

### DI 註冊位置
- `src/MyCollection.Application/DependencyInjection.cs` → `AddApplication`：MediatR（含 `ValidationBehavior<,>` pipeline）、validators、`EnrichJobRunner`（Scoped）。
- `src/MyCollection.Infrastructure/DependencyInjection.cs` → `AddInfrastructure`：所有 repository 與外部服務。
  - Singleton：`MongoContext`、`TimeProvider.System`、`IPasswordHasher`、`ITokenService`、`IFileStorage`、`IImageProcessor`、`ISecretProtector`、`IShowcaseImageQueue`、`SteamStoreRateLimiter`、`IIngestionTaskDispatcher`、`ICloudTaskAuthenticator`、IGDB 的 `ITwitchTokenProvider` / `IgdbRateLimiter`
  - Scoped：所有 `Mongo*Repository`、`SyncJobRunner`、`EnrichJobRunner`、`IngestionOperationExecutor`、`ProviderRegistry`、`IMetadataProvider`（多重註冊）
  - 設定驅動的實作切換：`Storage:Provider` = `Local` | `Gcs`；`Tasks:Provider` = `CloudTasks` | `InProcess`；`Igdb` 未設定憑證時整組不註冊。
- 觀察：`EnrichJobRunner` 在 Application 與 Infrastructure 兩處皆 `AddScoped`（重複註冊，階段 2 確認是否有意）。

### 對外 API（Minimal API）

| 群組 | 路由 | 授權 | 證據 |
|------|------|------|------|
| Auth | `POST /auth/register`、`/auth/login`、`/auth/refresh`；`GET /auth/me` | Anonymous（`/auth/me` 另計） | `Endpoints/AuthEndpoints.cs` |
| Categories | `GET/POST /categories`、`PUT/DELETE /categories/{id}`、`GET /{id}/missing-fields`、`POST /{id}/ensure-fields`、`POST /{id}/fields/{key}/rename` | RequireAuthorization | `Endpoints/CategoryEndpoints.cs` |
| Items | `GET /items`、`/items/tags`、`/items/platforms`、`GET/PUT/DELETE /items/{id}`、`POST /items` | RequireAuthorization | `Endpoints/ItemEndpoints.cs` |
| Media | `POST /items/{itemId}/images`、`DELETE .../{imageId}`、`POST .../{imageId}/primary`；`GET /media/{**path}` | RequireAuthorization | `Endpoints/MediaEndpoints.cs` |
| Media（公開） | `GET /public/{slug}/media/{**path}` | 【推論】匿名，由 slug 驗證 | `Endpoints/MediaEndpoints.cs` |
| Showcase | `GET /showcase?page&pageSize` | RequireAuthorization | `Endpoints/ShowcaseEndpoints.cs` |
| Sharing | `GET/POST /shares`、`DELETE /shares/{id}`；`GET /public/{slug}` | 前者需授權，`/public/{slug}` AllowAnonymous | `Endpoints/ShareEndpoints.cs` |
| Ingestion | `GET /ingest/providers`、`POST /ingest/sync/{provider}`、`POST /ingest/enrich/{provider}`、`GET /ingest/jobs`、`POST /ingest/jobs/{jobId}/retry`、`POST /ingest/fetch`、`GET /ingest/search` | RequireAuthorization | `Endpoints/IngestionEndpoints.cs` |
| External accounts | `GET/POST /external-accounts`、`DELETE /external-accounts/{provider}` | RequireAuthorization | `Endpoints/IngestionEndpoints.cs` |
| Internal | `POST /internal/tasks/ingestion` | 自行以 `ICloudTaskAuthenticator` 驗 OIDC token | `Endpoints/IngestionEndpoints.cs` |
| Transfer | `GET /images/export`、`POST /images/import` | RequireAuthorization | `Endpoints/ImageTransferEndpoints.cs` |

### 設定鍵（`appsettings*.json`，值已省略）
- `appsettings.json`：`Logging:*`、`AllowedHosts`、`Mongo:ConnectionString`、`Mongo:Database`、`Jwt:Issuer|Audience|Key|AccessTokenMinutes|RefreshTokenDays`、`Storage:Provider|LocalRoot`、`Tasks:Provider|Location|Queue`、`SecretProtection:Key`、`Steam:BaseAddress|TimeoutSeconds`、`Igdb:ClientId|ClientSecret`（機密欄位值為空字串；Mongo 預設指向 localhost）
- `appsettings.Development.json`：`Jwt:Key`、`SecretProtection:Key` —— **兩者皆有非空值且受 Git 追蹤**，值 `<REDACTED>`（見 `99-open-questions.md` Q3）
- 程式碼另讀取但 json 未列：`Cors:AllowedOrigins`（`Program.cs`）、`Storage:Bucket`、`Tasks:ProjectId|HandlerUrl|Audience|ServiceAccountEmail`、`Psn:*`、`Steam:StoreBaseAddress`（`Infrastructure/DependencyInjection.cs`）

## 前端

| 應用 | Angular 版本 | 架構（standalone / NgModule） | 主要路由 |
|------|--------------|-------------------------------|----------|
| `web/` | `@angular/core` ^20.3.0（`@angular/build:application`，測試 Karma + Jasmine） | Standalone，`app.config.ts` + functional interceptors，全部路由 `loadComponent` lazy load；仍用 `zone.js`（`provideZoneChangeDetection`） | `login`、`p/:slug`（公開分享）、受 `authGuard` 保護：`''`（showcase）、`catalog`、`items/new`、`items/:id`、`categories`、`settings` |

- 無 UI 元件庫（`dependencies` 僅 Angular 核心套件、rxjs、zone.js）。證據：`web/package.json`。
- HTTP interceptors 順序：`loadingInterceptor` → `authInterceptor` → `errorInterceptor`（`web/src/app/app.config.ts`）。
  - `auth.interceptor.ts`：附加 Bearer；401 時呼叫 `AuthService.refresh()` 一次後重送。
  - `error.interceptor.ts`：非 401 錯誤轉為使用者訊息（讀 ProblemDetails `detail`/`title`）。
- API base：執行期設定 `window.__MYCOLLECTION_CONFIG__.apiBase`，預設 `/api`（`web/src/app/core/api-base.ts`）；容器啟動時由 `web/runtime-config.template.js` 以 `API_BASE_URL` 產生 `/config.js`。無 `environment.*.ts` 檔。
- API client：`web/src/app/core/api/*.service.ts`（catalog、category、ingestion、provider、share、transfer），前端型別集中在 `web/src/app/core/models.ts`。
- 狀態：`AuthService` 使用 signals（`computed`）。【推論】其他 feature 亦以 signals + service 管理，階段 2 確認。
- 部署：`web/Dockerfile`（node:24-alpine build → nginx:alpine，EXPOSE 8080）、`web/nginx.conf`（SPA fallback `try_files ... /index.html`）；本機開發用 `web/proxy.conf.json`。

## 資料儲存

| 儲存 | 用途 | 存取位置（證據） |
|------|------|------------------|
| SQL Server | **未使用** | 全 repo（排除 bin/node_modules）搜尋 `EntityFramework`/`SqlClient`/`DbContext` 無結果 |
| PostgreSQL | **未使用** | 搜尋 `Npgsql` 無結果 |
| MongoDB | 唯一主資料庫 | `Infrastructure/Mongo/MongoContext.cs`：collections `users`、`categories`、`items`、`shareLinks`、`externalAccounts`、`syncJobs` |
| Redis | **未使用** | 搜尋 `redis` 無結果 |
| 物件儲存 | 圖片檔（Local 檔案系統或 GCS） | `Infrastructure/Storage/LocalFileStorage.cs`、`GcsFileStorage.cs`；Terraform `google_storage_bucket.media` |

### MongoDB indexes（`Infrastructure/Mongo/MongoIndexInitializer.cs`）
- `users`：`ux_users_email`（unique）、`ix_users_refreshTokenHash`（sparse）
- `categories`：`ix_categories_owner`（ownerId+name）
- `items`：`ix_items_showcase`、`ix_items_category`、`ix_items_tags`、`ux_items_externalRef`（unique）、`tx_items_text`（text: name+description）
- `shareLinks`：`ux_shareLinks_slug`（unique）
- `externalAccounts`：`ux_externalAccounts_owner_provider`（unique）
- `syncJobs`：`ix_syncJobs_owner_startedAt`
- 無 EF Migrations；schema 演進靠啟動時建 index + `SystemCategorySeeder`，以及 `MongoCategoryFieldRenamer`（欄位改名）。

觀察：`MyCollection.Domain` 直接依賴 `MongoDB.Bson`，Application 也有 `Common/BsonJson.cs`，持久化技術滲入內層（階段 2 評估）。

## 外部整合

| 系統 | 協定 | 呼叫位置 |
|------|------|----------|
| Steam Web API | HTTPS（`api.steampowered.com`），Polly 標準韌性 + 重試 3 次 | `Providers/SteamProvider.cs`、`SteamOptions.cs` |
| Steam Store | HTTPS（`store.steampowered.com`），**刻意不重試**，自訂節流 | `Providers/SteamStoreClient.cs`、`SteamStoreRateLimiter.cs` |
| PlayStation Network | HTTPS OAuth（`ca.account.sony.com`）+ Trophy API（`m.np.playstation.com`），不自動 redirect | `Providers/Psn/PsnProvider.cs`、`PsnOptions.cs` |
| IGDB（Twitch OAuth） | HTTPS（`api.igdb.com/v4`、`id.twitch.tv`），選配 | `Providers/Igdb/IgdbProvider.cs`、`TwitchTokenProvider.cs`、`IgdbRateLimiter.cs` |
| 任意網頁 OpenGraph | HTTPS 抓 HTML（AngleSharp 解析），回應上限 2 MB | `Providers/OpenGraphProvider.cs` |
| 外部圖片下載 | HTTPS（showcase 圖片快取） | `Imaging/ShowcaseImageDownloader.cs` |
| Google Cloud Storage | GCS client | `Storage/GcsFileStorage.cs` |
| Google Cloud Tasks | Tasks client 推送，回呼 `/internal/tasks/ingestion`（OIDC） | `Ingestion/CloudTasksIngestionTaskDispatcher.cs`、`GoogleCloudTaskAuthenticator.cs` |

## 背景工作

| 名稱 | 觸發方式 | 位置 |
|------|----------|------|
| `ShowcaseImageDownloader` | `BackgroundService`，消費 `ShowcaseImageQueue`（unbounded `Channel<ObjectId>`，行程內） | `Infrastructure/Imaging/ShowcaseImageDownloader.cs`、`ShowcaseImageQueue.cs` |
| `IngestionTaskWorker` | `BackgroundService`，僅 `Tasks:Provider=InProcess` 時註冊；消費 `InProcessIngestionTaskDispatcher` 的 unbounded channel | `Infrastructure/Ingestion/IngestionTaskWorker.cs` |
| Ingestion operation（雲端） | Cloud Tasks queue `mycollection-ingestion` → HTTP `POST /internal/tasks/ingestion` → `IngestionOperationExecutor` | `infra/terraform/runtime/tasks.tf`、`Endpoints/IngestionEndpoints.cs` |
| MongoDB 每日備份 | Cloud Scheduler `mycollection-mongo-backup-daily` → Cloud Run Job `mycollection-mongo-backup`（`mongodump --gzip` 上傳 GCS） | `infra/terraform/runtime/backups.tf`、`infra/backup/entrypoint.sh` |

## 基礎設施與部署

- 本機：`docker-compose.yml` → `api`（5080:8080，Local storage 掛 `./data/media`）+ `web`（8080:8080）。**compose 不含 MongoDB 服務**，連線字串由環境變數注入（`<REDACTED>`）。
- 容器：`src/MyCollection.Api/Dockerfile`（sdk:10.0 → aspnet:10.0，非 root `$APP_UID`）、`web/Dockerfile`、`infra/backup/Dockerfile`（Debian + mongo-tools，非 root）。
- 雲端（GCP，Terraform）：
  - `infra/terraform/bootstrap/`：必要 API、Workload Identity Federation（GitHub）、billing budget。
  - `infra/terraform/runtime/`：Cloud Run v2 services `api`/`web`（公開 invoker）、Artifact Registry、GCS buckets `media`/`backups`、Cloud Tasks queue、Secret Manager（mongo URI、JWT key、protection key、IGDB secret）、Monitoring alerts（5xx、備份失敗）。
- CI/CD（`.github/workflows/`）：兩支皆 **只有 `workflow_dispatch`**
  - `deploy-production.yml`：後端測試 → 前端測試與建置 → 推 immutable image → `.github/scripts/rollout-cloud-run.sh` canary（API、Web）→ 記錄部署 artifact。
  - `deploy-web-production.yml`：僅前端建置與 canary。
  - `.github/scripts/smoke-api.sh`：API smoke test（【推論】由 rollout 腳本呼叫，階段 2 確認）。
- 驗收與演練：`infra/acceptance/phase7-acceptance.ps1`、`restore-drill*.{ps1,sh}`。
- 相關既有文件：`CONTEXT.md`、`docs/adr/`（12 檔）、`docs/deployment/`、`docs/specs/`、`docs/superpowers/`（本階段未深讀）。

## 建議模組切分

依「垂直業務切片」切分（Application 本身即按 feature 資料夾組織，Endpoints 亦一對一），前端與基礎設施各自成模組。每個模組同時涵蓋 Api endpoint → Application handler → Infrastructure 實作。

1. **Identity & Security**：`AuthEndpoints`、`HttpUserContext`、`Application/Auth`、`Application/Common/{IUserContext,BackgroundUserContext,ITokenService,IPasswordHasher,ISecretProtector}`、`Infrastructure/Security`、`MongoUserRepository`。理由：JWT + refresh rotation + 使用者上下文切換（背景 vs HTTP）是所有模組的授權基礎，且有 `ScopedUserContext` 延遲判斷的特殊設計。
2. **Catalog（Categories + Items）**：`CategoryEndpoints`、`ItemEndpoints`、`Application/{Categories,Items}`、`Mongo{Category,Item}Repository`、`MongoCategoryFieldRenamer`、`SystemCategory*`、`Domain/Entities/{Category,Item}`。理由：分類定義動態欄位、Item attributes 依之驗證（`AttributeValidator`），兩者強耦合。
3. **Media & Transfer**：`MediaEndpoints`、`ImageTransferEndpoints`、`Application/{Media,Transfer}`、`Infrastructure/{Imaging/ImageSharpProcessor,Storage}`、`MongoImageArchiveRepository`。理由：共用 `IFileStorage` 與圖片處理，匯出/匯入為大檔案路徑（multipart 上限解除）。
4. **Showcase & Sharing**：`ShowcaseEndpoints`、`ShareEndpoints`、`Application/{Showcase,Sharing}`、`ShowcaseImageQueue/Downloader`、`Mongo{ShareLink,PublicCatalogReader}`。理由：唯讀展示 + 匿名公開存取，是主要的授權邊界風險點。
5. **Ingestion（同步／補完／外部 provider）**：`IngestionEndpoints`、`Application/Ingestion`（17 檔）、`Infrastructure/{Ingestion,Providers}`、`Mongo{ExternalAccount,SyncJob,ItemSyncWriter,ItemEnrichWriter}`。理由：最複雜區塊——外部 API、速率限制、背景執行雙路徑（InProcess / Cloud Tasks）、冪等與重試。
6. **Web（Angular 前端）**：`web/`。理由：獨立部署單元，需對照後端 DTO 檢查型別漂移。
7. **Platform & Infra**：`Program.cs`、`GlobalExceptionHandler`、Mongo 基礎（`MongoContext`、`MongoConventions`、`MongoIndexInitializer`）、`infra/`、`.github/`、Dockerfiles、compose。理由：橫切關注點與部署拓樸，階段 3 的 overview / cross-cutting 主要素材。

> 若希望縮小：可將 4 併入 2（Catalog），或將 3 併入 2；Ingestion 建議維持獨立。
