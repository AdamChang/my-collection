# AGENTS.md

## Agent skills

### Issue tracker

Issues live in this repo's GitHub Issues (`AdamChang/my-collection`), managed via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical triage roles, each label string equal to its name. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context — `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

## 架構速覽

> 由 `docs/architecture/`（2026-09-26，commit `61b1f5d`）萃取。有疑問時以該目錄和程式碼為準。

個人收藏管理系統：以品類定義動態欄位、同步 Steam／PSN 並以 IGDB／Steam 商店補完 metadata、組成精選牆，並用匿名連結分享。目前只有擁有者一人使用，未來改為邀請制。

### 專案結構

- `src/MyCollection.Domain`：Entities（User、Category、Item、ShareLink、ExternalAccount、SyncJob）與 domain exceptions；依賴 `MongoDB.Bson`。
- `src/MyCollection.Application`：MediatR handlers，依功能分資料夾（Auth、Categories、Items、Media、Sharing、Showcase、Ingestion、Transfer），另有 repository／服務介面與 `ValidationBehavior`。
- `src/MyCollection.Infrastructure`：Mongo repositories、Security（JWT／PBKDF2／AES-GCM）、Storage（Local／GCS）、Imaging（ImageSharp）、Providers（Steam／PSN／IGDB／OpenGraph）、Ingestion（InProcess／Cloud Tasks）。
- `src/MyCollection.Api`：Minimal API endpoints（`Endpoints/*`，與 Application 資料夾一對一）、`Program.cs`、`GlobalExceptionHandler`、`HttpUserContext`。
- `tests/MyCollection.Tests`：xUnit。
- `web/`：Angular 20.3 standalone + signals SPA；`core/`（服務、攔截器、`models.ts`）、`features/`（頁面）、`shared/`（元件）。
- `infra/terraform/{bootstrap,runtime}`：GCP；`infra/backup`：mongodump Job；`infra/acceptance`：驗收與還原演練。
- `.github/workflows`：只有手動 `workflow_dispatch` 的正式部署（canary）。

資料庫**只有 MongoDB**（正式環境為 Atlas Free），沒有 EF Core、SQL Server、Redis。

### Build／Test／Run

```bash
dotnet build MyCollection.slnx
dotnet test MyCollection.slnx
dotnet run --project src/MyCollection.Api          # http://localhost:5080；機密放 user secrets
cd web && npm ci && npm start                      # http://localhost:4200，/api 代理到 5080
cd web && npm test -- --watch=false --browsers=ChromeHeadless
docker compose up --build                          # 需要 .env（見 .env.example），不含 Mongo
```

- Karma 需要設定 `CHROME_BIN`，否則會無聲空轉；測試輸出請導向 log 檔，不要接 pipe。
- `infra/backup/Dockerfile` 的建置 context 必須是 repo root：`docker build -f infra/backup/Dockerfile .`。
- `git push`、Docker、`terraform apply`、正式部署交給使用者執行。

### 必須遵守的慣例

**後端分層與 DI**
- 新功能依「Endpoint → MediatR command/query + validator → Application 介面 → Infrastructure 實作」撰寫；validator 與 command 放在同一檔（例：`Application/Items/ItemCommands.cs`）。
- DI 只在 `Application/DependencyInjection.cs` 與 `Infrastructure/DependencyInjection.cs` 註冊；依設定切換實作的寫法照既有的 Storage、Tasks、IGDB 分支。
- 不在各層寫 try-catch：拋 `Domain/Exceptions` 的例外，由 `Api/GlobalExceptionHandler.cs` 統一轉成 ProblemDetails。新增例外型別時要一起補上對應。
- DTO 用 `record`，mapping 手寫（例：`ItemMapper`、`CategoryMapper`）。

**授權**
- 一般資料存取一律經由 repository 的 owner filter（`IUserContext`）；**不要**在 handler 繞過 repository 直接查 `MongoContext`。
- 匿名公開路徑只能用 `IPublicCatalogReader` 與 `PublicItemDto`，不可重用內部 `ItemDto`。`storageLocation` 永不公開（ADR-0008）。
- 背景作業的身分由 `BackgroundUserContext.Set` 提供（`ScopedUserContext` 在存取當下才判斷身分來源）。

**MongoDB**
- `MongoConventions.Register()` 必須早於任何序列化。欄位名稱是 camelCase，enum 存字串。
- 寫入 `DateTime` 前必須是 `Kind=Utc`，否則 `UtcOnlyDateTimeSerializer` 會拋例外。
- 更新用 `$set`／`UpdateOne`，不要用 `ReplaceOne`，因為 `IgnoreExtraElements` 會讓未映射的欄位遺失。
- **新寫入邏輯要逐欄位合併**：陣列用 `$push`／`$pull`，不要「讀取 → 修改 → 整份 `$set`」（Q18 決議，見 tech debt C1、M1）。
- 新索引加在 `MongoIndexInitializer`，必須冪等；啟動時的變更不會隨 canary 回滾復原。

**品類 schema 與 ingestion**
- 欄位 key 是身分（ADR-0012）：改名只能走 rename API；撤回宣告不應刪除品項上的值。
- Sync 只擁有 provider 欄位；`name` 由 enrich 擁有（ADR-0001）；多個 provider 共寫的欄位用 `FillOnlyIfAbsent`。
- Provider 能力由它實作的介面推導，不另設旗標。外部失敗拋 `ProviderException`（502）；查無對應記為 Skipped。
- 正式環境 enrich 與 sync 都走 Cloud Tasks，API 回應時狀態是 `Running`；前端用 `IngestionService.awaitJob` 輪詢。

**前端**
- API 呼叫只放在 `core/api/*.service.ts`，URL 以 `API_BASE` 組成；型別維護在 `core/models.ts`，與後端 record 手動同步。
- 錯誤由 `errorInterceptor` 顯示 toast；元件的 error 回呼使用 `IGNORE_HANDLED_BY_INTERCEPTOR`。
- 私有圖片（`/media/...`）必須透過 `AuthenticatedMediaDirective` 載入，不可用 `<img src>` 或 CSS `url()` 直接引用。
- 篩選與頁籤狀態以 URL 為真實來源（ADR-0010、ADR-0009）；互斥的寫入動作用 `busy` computed 鎖住。

**一般**
- 註解、文件、commit message 使用繁體中文；識別字維持英文。註解要寫「為什麼」。
- 機密不進版控：本機用 user secrets 或 `.env`，正式環境用 Secret Manager。Terraform 不放個人值。

### 高風險區域

修改以下區域前，請先讀 `docs/architecture/90-tech-debt.md` 的對應項目：

- **C1／M1／C2**：品項更新與圖片上傳會整份覆寫（`MongoItemRepository.UpdateAsync`、`item-detail.component.ts`）。未宣告的屬性和並行上傳的圖片會遺失。
- **M2**（推論，待實測）：`ImageSharpProcessor` 沒有清除 EXIF／GPS，而原尺寸圖可經由公開分享匿名讀取。
- **S1／S2**：公開媒體每個請求都重撈整個分享範圍；公開頁會回傳整份 attributes。
- **I1／I2／R4**：認證端點沒有速率限制、註冊開放、`/ingest/fetch` 是 SSRF 入口。
- **R1**：批次補完的候選清單沒有依 provider 過濾，可能永遠卡在同一批。
- **W1**：session 沒有跨分頁同步，加上後端只有一組 refresh token，多分頁時會互相登出。
- **Cloud Run 限制**：API 最多 1 個實例、請求逾時 300 秒、回應後 CPU 會被節流。不要把長時間工作或「回應之後的行程內背景工作」放進請求路徑（R2、S3、P13）。

### 架構文件

- `docs/architecture/00-inventory.md`：盤點（專案、API、設定鍵、外部整合）
- `docs/architecture/01-overview.md`：總覽、部署拓樸、模組地圖、ADR 索引
- `docs/architecture/02-data-flow.md`：五條關鍵流程與資料儲存對照
- `docs/architecture/03-cross-cutting.md`：認證授權、錯誤、log、設定、背景工作、並發
- `docs/architecture/11`～`17-module-*.md`：各模組深讀（identity、catalog、media-transfer、showcase-sharing、ingestion、web、platform-infra）
- `docs/architecture/90-tech-debt.md`：技術債與處理順序
- `docs/architecture/99-open-questions.md`：待確認問題
- 維運：`docs/deployment/production-operations.md`、`mongodb-backup-restore-runbook.md`
