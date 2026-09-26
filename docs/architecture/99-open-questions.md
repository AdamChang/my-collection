# 待確認問題

> 階段 3 定稿（2026-09-26，commit `61b1f5d`）。共 26 題：已回覆或已由程式碼確認 10 題、部分回覆 3 題、未解決 13 題。
> 「提出階段」表示該問題在哪個階段提出；回覆內容以【】標在問題文字前。技術債的處理順序見 `90-tech-debt.md`。

## 優先回覆

以下問題的答案會直接改變技術債的嚴重度或處理順序：

| # | 為什麼優先 |
|---|---|
| Q21 | 若 EXIF／GPS 確實被保留，M2 就是已證實的隱私外洩，應列為第一優先 |
| Q23 | 決定 P2 是否成立；P2 成立與否，決定背景失敗能不能被看見 |
| Q7 | 決定 R1（批次補完卡住）是否已經在正式環境發生 |
| Q8 | 決定 R3 是否需要立即做一次人工清理 |
| Q16 | 決定 S8 的修正方向，也影響 M2 的影響範圍 |
| Q20 | 決定 C5 的方向是「刪除時清檔」還是「刻意保留」 |
| Q3 | 確認開發用金鑰與正式環境不同，排除機密風險 |
| Q19 | 決定 C4 是否需要調整搜尋實作 |

## 全部問題

| # | 狀態 | 問題 | 來源／證據 | 提出階段 |
|---|---|---|---|---|
| Q1 | 已回覆 | 【已回覆：ADR-0011，正式環境使用 GCP 上的 Atlas Free，Network Access 為 `0.0.0.0/0`，每日 `mongodump` 保留 30 天】正式環境 MongoDB 由誰託管（Atlas？自建？）、版本與備援設定為何？`docker-compose.yml` 與 Terraform 皆未建立 Mongo 本體，只有 Secret Manager 的 `mongo_connection_string`。 | `docker-compose.yml`、`infra/terraform/runtime/*.tf` | 1 |
| Q2 | 未解決 | 使用者技術堆疊宣稱 Angular 21+，但 `web/package.json` 為 `@angular/core` ^20.3.0。是否有升級計畫？ | `web/package.json` | 1 |
| Q3 | 未解決 | `appsettings.Development.json` 內 `Jwt:Key`、`SecretProtection:Key` 為非空值並受 Git 追蹤（值 `<REDACTED>`）。是否確認僅為本機開發用、且與正式環境金鑰不同？ | `src/MyCollection.Api/appsettings.Development.json` | 1 |
| Q4 | 未解決 | 沒有 push / PR 觸發的 CI workflow，測試只在手動部署時跑。是否刻意如此（例如在本機 hook 執行）？　【階段 3 補註：已列為 `90-tech-debt.md` P4】 | `.github/workflows/*.yml`（僅 `workflow_dispatch`） | 1 |
| Q5 | 程式碼已確認 | `EnrichJobRunner` 同時在 `AddApplication` 與 `AddInfrastructure` 註冊，是否為有意？　【階段 3 補註：兩處都是 Scoped，行為沒有差異，只剩「保留哪一處」的決定，見 `90-tech-debt.md` R12】 | `src/MyCollection.Application/DependencyInjection.cs`、`src/MyCollection.Infrastructure/DependencyInjection.cs` | 1 |
| Q6 | 部分釐清 | `ForwardedHeadersOptions` 清空 `KnownIPNetworks`/`KnownProxies`（信任任何來源的 `X-Forwarded-*`，限 1 層）。是否確認 API 只經 Cloud Run 前端入口、不會被直接存取？　【階段 3 補註：在 Cloud Run 上取 1 跳是正確的；風險在自架的 compose 直接暴露 5080，見 P11】 | `src/MyCollection.Api/Program.cs` | 1 |
| Q7 | 未解決 | Steam／IGDB 批次補完的候選清單沒有依 `externalRef.provider` 過濾，查無對應或失敗的品項也不會寫 marker，可能讓批次永遠卡在同一批（見 `15-module-ingestion.md` R1）。這是已知行為嗎？ | `Infrastructure/Mongo/MongoItemRepository.cs` → `ListEnrichmentCandidatesAsync` | 2 |
| Q8 | 未解決 | 正式環境是否出現過停在 `Running`、無法重試的 syncJobs？有人工清理流程嗎？ | `IngestionOperationExecutor.ExecuteAsync`、`RetrySyncJobCommandHandler` | 2 |
| Q9 | 已回覆（見 Q12） | `/auth/register` 在正式環境是否開放註冊？這會決定 `/ingest/fetch`（SSRF）的實際曝險。　【階段 3 補註：由 Q12 的回覆涵蓋：程式碼中註冊是開放的，使用者只有擁有者一人】 | `Api/Endpoints/AuthEndpoints.cs`、`Infrastructure/Providers/OpenGraphProvider.cs` | 2 |
| Q10 | 未解決 | 補完 `limit` 上限 200 有實際使用情境嗎？以 Steam 商店 1.5 秒間隔換算約 300 秒，等於 Cloud Run 的逾時。 | `EnrichCommandHandler`、`infra/terraform/runtime/services.tf` | 2 |
| Q11 | 未解決 | PSN 同步使用寫死的行動 App client 憑證與非官方 API，是否評估過服務條款風險？ | `Infrastructure/Providers/Psn/PsnProvider.cs` | 2 |
| Q12 | 已回覆 | 【已回覆 2026-09-26：目前只有自己使用，未來改為邀請制。開放註冊（I2）與邀請制的方向不符，應列入技術債】系統預期的使用者規模是什麼：只有你自己、邀請制，還是公開服務？認證端點目前完全沒有速率限制，註冊也完全開放（見 `11-module-identity.md` I1、I2）。 | `Api/Endpoints/AuthEndpoints.cs`、`Application/Auth/RegisterCommand.cs` | 2 |
| Q13 | 部分回覆（見 Q26） | 同一帳號只保存一組 refresh token，多裝置會互相登出。這是刻意的設計嗎？　【階段 3 補註：Q26 回覆「很少多裝置同時登入」，I11 維持低；多分頁問題另列 W1（高）】 | `Domain/Entities/User.cs` | 2 |
| Q14 | 未解決 | 前端 token 存在 `localStorage`，而且 nginx 沒有設定 CSP。是否考慮改用 HttpOnly cookie，或至少補上 CSP？ | `web/src/app/core/auth.service.ts`、`web/nginx.conf` | 2 |
| Q15 | 已回覆 | 【已回覆 2026-09-26：不符合預期，欄位是否公開應由使用者決定；S2 升為高，列入技術債】公開分享頁會回傳整份 attributes（使用者自訂欄位與 provider 欄位全部公開）。是否需要欄位層級的公開控制？ | `Infrastructure/Mongo/MongoPublicCatalogReader.cs` → `BaseProjection` | 2 |
| Q16 | 未解決 | 公開媒體端點允許讀取原尺寸圖（`-full.webp`），但公開 DTO 只提供 card 與 thumb。這是刻意設計嗎？ | `Application/Media/MediaQueries.cs` → `ContainsPath` | 2 |
| Q17 | 未解決 | 精選圖片下載失敗後沒有重試，也沒有手動重新產生的入口。ADR-0011 說「允許重新產生」，實際預期怎麼觸發？ | `Infrastructure/Imaging/ShowcaseImageDownloader.cs`、`Application/Items/ItemCommands.cs` | 2 |
| Q18 | 已回覆 | 【已回覆 2026-09-26：後端改為逐欄位合併寫入】編輯品項會刪除它的未宣告屬性，與 ADR-0012 §三「撤回宣告不刪值」衝突（`12-module-catalog.md` C1）。修正方向偏好後端逐鍵合併，還是讓 PUT 保留請求中未提及的未宣告鍵？ | `web/src/app/features/item-detail/item-detail.component.ts` → `toPayload`；`Infrastructure/Mongo/MongoItemRepository.cs` → `UpdateAsync` | 2 |
| Q19 | 未解決 | 全文搜尋使用預設語言的 text index，中文名稱可能搜不到部分字串（C4）。實際使用時遇過嗎？ | `Infrastructure/Mongo/MongoIndexInitializer.cs`（`tx_items_text`） | 2 |
| Q20 | 未解決 | 刪除品項時不會刪除圖片檔（C5）。這是刻意保留，還是遺漏？ | `Application/Items/ItemCommands.cs` → `DeleteItemCommandHandler` | 2 |
| Q21 | 未解決（優先） | 上傳圖片後，輸出的 WebP 可能保留原始 EXIF（包括 GPS），而 full 尺寸圖可以透過公開分享匿名取得（`13-module-media-transfer.md` M2）。是否可以用一張帶 GPS 的手機照片實測？ | `Infrastructure/Imaging/ImageSharpProcessor.cs` | 2 |
| Q22 | 已回覆 | 【已回覆 2026-09-26：不需要；M5 列入技術債，建議正式環境停用或移除匯出／匯入端點】正式環境改用 GCS 之後，還需要圖片匯出／匯入嗎？匯入端點目前沒有 body 大小上限，也不檢查 entry 數量（M5）。 | `Api/Endpoints/ImageTransferEndpoints.cs` | 2 |
| Q23 | 部分回覆（優先） | 【部分回覆 2026-09-26：使用者提供的 ERROR 紀錄 logName 為 `run.googleapis.com/requests`，是 Cloud Run 平台產生的 request log（OPTIONS 預檢回 500：「no available instance」），不是應用程式 stdout，所以 P2 仍未驗證；待查 `logName` 為 `…/run.googleapis.com%2Fstdout` 且 `severity>=ERROR` 的紀錄。該紀錄另成觀察 P13】API 的應用程式 log 使用預設 console formatter 寫到 stdout，Cloud Logging 可能沒有 severity，因此 canary 的 `severity>=ERROR` 與告警都看不到 `LogError`（`17-module-platform-infra.md` P2）。在 Logs Explorer 查 `mycollection-api` 的 `severity>=ERROR`，能否看到應用程式自己記錄的例外？ | `Api/Program.cs`（沒有 `AddJsonConsole`）、`.github/scripts/rollout-cloud-run.sh` | 2 |
| Q24 | 已回覆 | 【已回覆 2026-09-26：計畫把圖片備份到 Google Drive；P1 維持列入技術債，直到備份上線】media bucket 沒有版本控管，Terraform 也沒有宣告 soft delete，備份 Job 只 dump Mongo（P1）。實際的 soft delete 設定是什麼？圖片需要備份嗎？ | `infra/terraform/runtime/storage.tf`、`docs/deployment/production-operations.md` | 2 |
| Q25 | 已回覆 | 【已回覆 2026-09-26：使用者確認符合授權條款】MediatR 14 的授權警告以「個人非營利用途」為由被靜音。這個前提是否確認過符合 MediatR 的授權條款？ | `Api/Program.cs` → `AddFilter("LuckyPennySoftware.MediatR.License", ...)` | 2 |
| Q26 | 已回覆 | 【已回覆 2026-09-26：會開多個分頁，但很少在不同裝置同時登入；W1 升為高】你平常會同時開多個分頁，或在手機和電腦上都登入嗎？session 沒有跨分頁同步，後端又只保留一組 refresh token，多開時會互相登出（`16-module-web.md` W1）。 | `web/src/app/core/auth.service.ts`、`Application/Auth/*` | 2 |
