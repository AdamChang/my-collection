# 技術債清單

> 階段 3 整合文件。資料基準：commit `61b1f5d`，2026-09-26。
> 共 78 項：高 9、中 17、低 52。ID 沿用各模組文件（I＝Identity、C＝Catalog、M＝Media、S＝Showcase、R＝Ingestion、W＝Web、P＝Platform），完整證據與推論依據請見原文件。
> **本文只寫建議方向，不含修改後的程式碼。** 路徑省略 `src/MyCollection.` 與 `web/src/app/` 前綴。

## 嚴重度定義

| 等級 | 定義 |
|---|---|
| 高 | 目前正常使用就會發生資料遺失或隱私外洩；或者匿名者可直接利用，影響可用性或安全 |
| 中 | 需要特定條件才會觸發，但觸發後會造成資料錯誤、服務中斷，或讓問題變得無法察覺 |
| 低 | 可維護性、一致性、邊界情況，或需要多重條件才會成立 |

嚴重度已依使用者的回覆調整：
- Q12：目前只有自己使用，未來改為邀請制。
- Q15：欄位是否公開應由使用者決定。
- Q18：改為逐欄位合併寫入。
- Q22：不需要圖片匯出與匯入。
- Q24：計畫把圖片備份到 Google Drive。
- Q26：會開多個分頁。

## 根因主題

多數項目可以歸到五個根因。**先處理根因，比逐項修補划算。**

| 主題 | 涵蓋項目 | 說明 |
|---|---|---|
| A. 讀取 → 修改 → 整份覆寫，沒有並發控制 | C1、C2、M1、R11、C10、W9 | Q18 已決定方向：改為逐欄位合併寫入，陣列用 `$push`／`$pull`，必要時加入版本比對 |
| B. 匿名與開放的入口 | I1、I2、R4、S1、S2、S8、M2、M3、S4、M5 | 開放註冊讓「匿名」與「已登入」幾乎等價；公開路徑沒有速率限制或快取，資料的公開範圍也超出設計 |
| C. 單一實例承載所有工作 | P13、R2、M3、M4、S3、P6 | `max 1` 加上 300 秒逾時與 CPU 節流，長請求、大記憶體工作、回應後的背景工作與冷啟動，都會直接變成使用者看得到的失敗 |
| D. 失敗無聲 | P2、R3、S3、W3、W4、W8 | 背景錯誤沒有 severity、沒有告警，前端又把錯誤顯示成「沒有資料」 |
| E. Session 模型 | W1、I4、I5、I11、I3 | 每個帳號一組 refresh token，換發不是原子操作，前端沒有跨分頁同步，也沒有伺服器端登出 |

## 建議處理順序

這裡只排順序與理由，實作細節留給各項目。

1. **先止血（資料遺失與隱私）**：C1、M1（主題 A），M2（先依 Q21 實測），S2。這四項都會在日常使用中默默損壞或外洩資料，而且發現時已無法回復。
2. **縮小攻擊面**：I2（改為邀請制）→ I1（速率限制）→ R4（限制 SSRF 目標）→ M5 與 M4（依 Q22 移除匯出與匯入）。I2 一關，R4、M3、S4 的曝險都會跟著下降。
3. **公開路徑的效能**：S1。這條是匿名路徑，與使用者規模無關。
4. **看得見失敗**：P2（先依 Q23 驗證）→ R3 → P13 統計。沒有這一步，其他問題的修正效果也無法觀察。
5. **Session**：W1 → I5 → I4。W1 在多分頁使用下每天都會發生。
6. **背景工作韌性**：R1、R2、S3、P1（依 Q24 的 Google Drive 備份計畫）。
7. 其餘中、低項目，視情況併入相關修改一起處理。

## 高

| ID | 項目 | 證據位置 | 影響 | 建議方向 |
|---|---|---|---|---|
| C1 | 編輯品項會靜默刪除未宣告的屬性，違反 ADR-0012 §三 | `features/item-detail/item-detail.component.ts` → `toPayload`、`declaredOnly`；`Infrastructure/Mongo/MongoItemRepository.cs` → `UpdateAsync`；`AttributeValidator.Validate` | 撤回某個欄位宣告後，只要編輯過該品項，舊值就永久消失，重新宣告也救不回來；過程沒有任何提示 | 依 Q18：後端只 `$set`／`$unset` 請求中出現的已宣告鍵，未宣告鍵原樣保留；前端不再需要 `declaredOnly` 過濾 |
| M1 | 多選上傳會遺失圖片，並留下孤兒檔 | `Application/Media/ImageCommands.cs` → `UploadItemImageCommandHandler`；`item-detail.component.ts` → `uploadImages`（並行發送）；`MongoItemRepository.UpdateAsync`（`$set images`） | 日常操作（上傳元件預設允許多選）只會留下其中一張，其他檔案佔用 GCS，沒有錯誤訊息 | 併入主題 A：上傳用 `$push`、刪除用 `$pull`、設主圖用條件更新；前端改成依序上傳作為過渡；以 `DeleteDirectoryAsync`（M7）清理既有的孤兒檔 |
| M2 | 【推論，待 Q21 實測】輸出的 WebP 可能保留 EXIF／GPS，而原尺寸圖可匿名讀取 | `Infrastructure/Imaging/ImageSharpProcessor.cs` → `ProcessAsync`、`ResizeAsync`；`Application/Media/MediaQueries.cs` → `ContainsPath`（S8） | 照片本身洩漏拍攝地點，繞過 ADR-0008「存放位置永不公開」的設計意圖 | 先實測；若屬實，處理時清除 EXIF、XMP、IPTC，並對既有圖片做一次性重處理；同時決定 S8（公開路徑是否開放原尺寸圖） |
| S2 | 公開頁回傳整份 attributes | `Infrastructure/Mongo/MongoPublicCatalogReader.cs` → `BaseProjection`；`Application/Sharing/*` → `GetPublicShareQueryHandler` | 自訂欄位（序號、備註、購買管道）與 provider 欄位全部匿名可讀，與 ADR-0008「新欄位不會自動外流」的精神不一致 | 依 Q15：在品類欄位定義上加入「公開」旗標（預設不公開），公開投影只輸出有這個旗標的鍵；補一份 ADR |
| S1 | 每個公開圖片請求都重撈整個分享範圍，而且 `no-store` | `Application/Media/MediaQueries.cs` → `OpenPublicMediaQueryHandler.Handle`；`MongoPublicCatalogReader.ListItemsAsync`；`Api/Endpoints/MediaEndpoints.cs` | 一次頁面載入等於 1+N 次全量查詢；匿名訪客可以消耗 Atlas Free 配額，並塞滿唯一的實例 | 以「slug → owner + scope + 圖片路徑」做單筆存在查詢；允許短時間的 public cache；搭配 I1 的速率限制 |
| I2 | 開放註冊，註冊後即可使用所有功能 | `Application/Auth/RegisterCommand.cs`；`features/auth/login.component.ts` | 任何人都能取得帳號，進而使用 SSRF（R4）、上傳（M3）、佔用儲存空間；與 Q12 的邀請制方向不符 | 關閉公開註冊，改為邀請碼或白名單 email；前端移除註冊模式 |
| I1 | 認證端點沒有速率限制 | `Api/Endpoints/AuthEndpoints.cs`；全 repo 沒有使用 `AddRateLimiter` | 暴力破解密碼；PBKDF2 210k 次迭代造成 CPU 型 DoS，而且只有一個實例 | 對 `/auth/*` 與 `/public/*` 加上速率限制；分區鍵要考慮 P11（ForwardedHeaders 的信任設定） |
| R1 | 批次補完可能永遠卡在同一批品項 | `MongoItemRepository.ListEnrichmentCandidatesAsync`；`Application/Ingestion/EnrichJobRunner.cs` → `ExternalIdFor` | 查無對應或 provider 不符的品項每次都會被選中，補完永遠推進不了（取決於資料組成） | 候選清單依 provider 可定址性過濾；查無對應時寫入「已嘗試」標記，或以時間戳做退避 |
| W1 | 多分頁（與多裝置）會互相登出（Q26 升為高） | `core/auth.service.ts`（沒有監聽 `storage` 事件，`logout` 會 `removeItem`）；`Domain/Entities/User.cs`（單一 refresh token） | 使用者日常多開分頁時，每 30 分鐘左右就可能被登出，一個分頁還會清掉其他分頁的 session | 前端監聽 `storage` 事件同步 session，並跨分頁協調換發（例如 BroadcastChannel 或 Web Locks）；logout 前確認儲存的是否仍是自己的 token；後端配合 I5 |

## 中

| ID | 項目 | 證據位置 | 影響 | 建議方向 |
|---|---|---|---|---|
| C2 | 表單儲存會覆寫背景寫入的結果（lost update） | `Application/Items/ItemCommands.cs` → `UpdateItemCommandHandler`；`MongoItemRepository.UpdateAsync` | Steam 補完、sync 或精選圖片下載的成果，會被舊表單蓋掉 | 主題 A；另外加入 `updatedAt` 或版本比對，不符合時回 409 |
| C3 | Date 屬性在一次編輯後會從 BSON DateTime 變成字串並失去時間 | `shared/dynamic-form/dynamic-form.component.ts` → `toControlValue`、`coerce`；`Application/Common/BsonJson.cs` | 同一欄位的型別混雜，時間資訊遺失，日後的排序與範圍查詢會錯 | 後端依欄位型別正規化寫入；區分「日期」與「日期時間」；對既有資料做一次正規化 |
| C4 | 【推論】text index 沒有指定語言，中文搜尋幾乎無效 | `Infrastructure/Mongo/MongoIndexInitializer.cs`（`tx_items_text`） | 搜尋部分中文名稱找不到結果 | 先實測（Q19）；改用 regex 包含搜尋並設資料量上限，或評估 Atlas Search |
| M3 | 圖片解碼沒有像素上限 | `ImageSharpProcessor.ProcessAsync`（沒有 `DecoderOptions`）；`runtime/services.tf`（512Mi） | 一張小檔可能讓唯一實例 OOM（與 P13 疊加） | 解碼前先讀取圖片資訊並限制寬高或像素數；設定 ImageSharp 的記憶體上限 |
| M4 | 匯出受 300 秒逾時限制，可能產生殘缺的 zip | `Api/Endpoints/ImageTransferEndpoints.cs`；`Application/Transfer/ImageArchiveWriter.cs` | 產生打不開的檔案，而且不會報錯 | 依 Q22：正式環境移除匯出端點；圖片備份改由 P1 的 Google Drive 計畫負責 |
| M5 | 匯入端點沒有大小與內容限制 | `ImageTransferEndpoints`（`UnlimitedRequestBody`）；`Application/Transfer/ImportImageArchiveCommand.cs`；`Api/Program.cs`（`MultipartBodyLengthLimit = long.MaxValue`） | 記憶體型 `/tmp` 會吃掉容器記憶體；zip bomb 可以佔用儲存空間 | 依 Q22：移除端點，並一併還原全域 multipart 上限、清理 `web/nginx.conf` 的 `client_max_body_size`（P9） |
| S3 | 精選圖片下載只觸發一次，佇列在記憶體中，失敗不重試 | `Infrastructure/Imaging/ShowcaseImageQueue.cs`、`ShowcaseImageDownloader.cs` → `ExecuteAsync`；`ItemCommands.cs`（只有 `becameShowcased` 時觸發） | `cpu_idle`、縮到 0 時工作會延後或遺失；沒有重新產生的入口（Q17） | 改走 Cloud Tasks 等持久化佇列；提供「重新產生精選圖片」的操作；記錄失敗狀態 |
| R2 | 長時間作業會超過 300 秒逾時；lease 只有 5 秒且不續約 | `Application/Ingestion/IngestionOperationExecutor.cs`（`LeaseDuration`）；`EnrichCommandHandler`（limit 上限 200）；`runtime/services.tf` | 整批白做後重試又從頭開始；可能與舊的 delivery 並行 | 降低單批上限，讓預期耗時遠小於逾時；改為分段寫入並記錄進度；lease 長度要大於單段耗時，或定期續約 |
| R3 | 作業可能永遠停在 `Running` | `IngestionOperationExecutor`（`ResetForRetryAsync`）；`RetrySyncJobCommandHandler`（只允許 Failed） | UI 無法重試；沒有告警 | 以 lease 或 `startedAt` 逾時判定為 Failed 的清理機制；允許重試逾時的 Running 作業；搭配 P2 加上告警 |
| R4 | `/ingest/fetch` 是 SSRF 入口 | `Infrastructure/Providers/OpenGraphProvider.cs` → `FetchByUrlAsync`；`FetchByUrlQueryValidator` | 可以探測 Cloud Run 可連到的內部位址 | 解析 DNS 後拒絕私有、link-local、metadata 網段；限制 redirect 並在每一跳重新檢查；錯誤訊息不回傳上游內容；搭配 I2 |
| I3 | token 存在 localStorage，而且沒有 CSP | `core/auth.service.ts`；`web/nginx.conf`、`index.html` | 一旦出現 XSS，refresh token 會被長期盜用 | 先補 CSP 與基本安全 header（P9）；長期評估 refresh token 改用 HttpOnly cookie（需一併處理 CORS 與 CSRF） |
| I4 | 沒有登出或撤銷 token 的 API | `Api/Endpoints/AuthEndpoints.cs` | 登出後伺服器端的 token 仍然有效，外洩時無法止損 | 新增登出端點清除 refresh token hash；前端 logout 時呼叫 |
| I5 | refresh 換發不是原子操作，也沒有偵測重複使用 | `Application/Auth/*` → `RefreshCommandHandler`；`MongoUserRepository.SetRefreshTokenAsync` | 並行換發時兩次都會成功；被盜用的 token 無法察覺 | 以「`_id` + 舊 hash」做條件更新並檢查結果；評估引入 token family 與重用偵測；與 W1、I11 一起設計 |
| W2 | 內部精選頁的 Stats 背景圖在有本機圖片時會 401 | `shared/showcase-sections/stats-section.component.ts`（`[style.background-image]`）；`showcase-display-item.ts` → `coverImageUrl` | 設為精選的遊戲（圖片已被下載到本機）在成就頁籤會變成黑底 | 改用 `AuthenticatedMediaDirective`（已支援 backgroundImage） |
| P1 | 媒體沒有備份與版本控管 | `infra/terraform/runtime/storage.tf`；`docs/deployment/production-operations.md` | 誤刪或 bug 造成的圖檔損失無法回復；Mongo 還原後會指向不存在的圖片 | 依 Q24 的 Google Drive 備份計畫；同時考慮在 bucket 上開啟 soft delete 或 versioning 作為短期保護 |
| P2 | 【推論，待 Q23 驗證】app log 沒有 severity | `Api/Program.cs`（沒有 JSON console）；`.github/scripts/rollout-cloud-run.sh` | canary 看不到應用程式錯誤；背景失敗完全無聲 | 改為輸出含 `severity` 欄位的結構化 log；補上以 log 為基礎的背景失敗告警 |
| P13 | 正式環境出現「no available instance」500 | Cloud Run request log（Q23 樣本）；`runtime/services.tf`（`max_instance_count = 1`） | 精選牆的 CORS 預檢失敗，首頁載不出來 | 先統計頻率與時間點；評估放寬到 2、把重工作移出請求路徑（R2、M3、M4）、前端對暫時性失敗重試 |


## 低

| ID | 項目 | 證據位置 | 影響 | 建議方向 |
|---|---|---|---|---|
| I6 | 註冊會洩漏 email 是否存在 | `MongoUserRepository.InsertAsync`（409 訊息） | 帳號列舉 | 邀請制上線後自然消失；在那之前統一回應訊息 |
| I7 | 登入失敗回 403 | `LoginCommandHandler`、`RefreshCommandHandler` | 語意錯誤，外部監控可能誤判 | 認證失敗改回 401，並確認前端攔截器的排除規則 |
| I8 | JWT／保護金鑰設定錯誤到第一次使用才失敗 | `Api/Program.cs`；`JwtOptions`；`AesGcmSecretProtector` | 服務看似健康，實際無法登入 | Options 驗證搭配 `ValidateOnStart` |
| I9 | 登入密碼沒有長度上限 | `LoginCommandValidator` | 放大 I1 的 CPU 消耗 | 與註冊一致，限制 128 |
| I10 | 外部憑證密文沒有 AAD 與金鑰版本 | `Infrastructure/Security/AesGcmSecretProtector.cs` → `Protect` | 密文可被搬移；金鑰無法漸進輪替 | 以 owner 與 provider 作為 AAD；密文加上 key id 前綴 |
| I11 | 每個帳號只有一個 session | `Domain/Entities/User.cs` | 多裝置互相登出（Q26：很少發生） | 與 W1、I5 一起評估是否改為多 session 集合 |
| I12 | `FixedUserContext` 是死碼 | `Application/Common/BackgroundUserContext.cs` | 閱讀負擔 | 移除 |
| C5 | 刪除品項不刪圖片檔 | `ItemCommands.cs` → `DeleteItemCommandHandler` | 孤兒檔持續佔用空間；與刪除在隱私上的預期不符（Q20） | 刪除 DB 後以 `DeleteDirectoryAsync`（M7）盡力清除 |
| C6 | 改欄位型別或必填時不遷移既有值 | `UpdateCategoryCommandHandler`；ADR-0012 §二 | 舊品項下次儲存時回 400，前端可能無反應 | 變更前統計不相容的品項數並提示，或禁止修改既有欄位的型別 |
| C7 | 同步依名稱找「數位遊戲」品類 | `Application/Ingestion/SyncJobRunner.cs` → `GetDigitalCategoryAsync` | 同名的自訂品類會接走同步資料 | 改用固定的 `DigitalGameId` |
| C8 | 可搜尋的 Number、Bool、Date 欄位篩不到資料 | `MongoItemRepository.SearchAsync`；`categories.component.ts` | 自訂品類的篩選永遠為空 | 依欄位型別轉換篩選值，或只允許 Text 與 Select 設為可搜尋 |
| C9 | 前端 `ItemDto.source` 少了 `Psn` | `core/models.ts`；`Domain/Entities/Item.cs` | 型別低報 | 補上；中期考慮由 OpenAPI 產生型別（`16` DTO 對照） |
| C10 | 刪除品類與改名沒有並發保護 | `DeleteCategoryCommandHandler`；`MongoCategoryRepository.UpdateAsync` | 多分頁操作時可能產生孤兒品項或 schema 回退 | 併入主題 A 的版本比對 |
| C11 | 搜尋沒有 debounce | `features/catalog/catalog.component.ts` → `applySearch` | 每個字元都查詢，消耗 Atlas 配額 | 以 debounce 後再導航 |
| M6 | 檔案與 DB 的操作順序在失敗時留下不一致 | `UploadItemImageCommandHandler`、`DeleteItemImageCommandHandler` | 孤兒檔或破圖 | 刪除時先更新 DB 再盡力刪檔 |
| M7 | `DeleteDirectoryAsync` 沒有呼叫端 | `Application/Common/IFileStorage.cs` | 已有的清理能力沒有接上 | 接到 C5 與孤兒清理流程 |
| M8 | 前端把整份匯出檔收進記憶體 | `core/api/transfer.service.ts` | 大量收藏時佔用記憶體 | 依 Q22 隨匯出功能一起移除 |
| M9 | 上傳元件的 `busy` 從未被設定 | `shared/image-uploader/image-uploader.component.ts` | 沒有上傳中的提示，加劇 M1 | 由父元件傳入上傳狀態 |
| S4 | 背景下載沒有大小上限，URL 來自可編輯的 attributes | `ShowcaseImageDownloader.DownloadAsync` | 記憶體耗用、blind SSRF | 限制下載大小與 content-type；套用 R4 的目標過濾 |
| S5 | 可以建立已過期的分享；過期資料不清理 | `CreateShareLinkCommandValidator` | 資料殘留 | 驗證 `ExpiresAt > now`；加上 TTL index |
| S6 | 公開圖片路徑暴露 ownerId | 圖片路徑格式 `{ownerId}/{itemId}/...` | 洩漏內部 id 與帳號建立時間 | 公開路徑改用不透明的代號，或由 slug 對應 |
| S7 | 公開頁直接載入第三方圖片 | `shared/showcase-sections/showcase-display-item.ts` → `coverImageUrl` | 訪客的 IP 與 Referer 外流 | 公開頁只使用本機圖片；與 S3 一起處理 |
| S8 | 公開路徑可讀原尺寸圖 | `MediaQueries.cs` → `ContainsPath` | 超出 DTO 設計的公開範圍；放大 M2 | 決定是否只允許 card 與 thumb（Q16） |
| S9 | Category 範圍的分享不檢查品類 | `CreateShareLinkCommandValidator` | 可能建出空連結 | 建立時檢查品類存在，且屬於自己或系統品類 |
| S10 | 精選牆一次抓完全部精選品項 | `features/showcase/showcase.component.ts` → `fetchPage` | 品項多時首次載入變慢 | 刻意設計（ADR-0009）；數量成長後改由後端回傳各頁籤的計數 |
| R5 | 例外原文寫入 `syncJobs.error` 與 502 detail | `SyncJobRunner`、`EnrichJobRunner`；`Api/GlobalExceptionHandler.cs` | 內部細節暴露到 UI | 只對已知的 provider 錯誤回傳訊息，其餘改為一般訊息加上 log |
| R6 | Steam key 放在 query string | `SteamProvider.SyncAsync` | 若關閉 URI 遮蔽或自行記錄 URI 就會外洩 | 維持預設遮蔽；code review 時禁止記錄 `RequestUri` |
| R7 | 寫死 PSN client 憑證、使用非官方 API | `Infrastructure/Providers/Psn/PsnProvider.cs` | 可能突然失效；服務條款風險（Q11） | 接受風險並在 UI 上區分「NPSSO 過期」與「API 變更」 |
| R8 | 韌性層會重試 POST | `Infrastructure/DependencyInjection.cs`（PSN、Twitch） | 一次性的 code 被重送，誤報 NPSSO 過期 | 對不安全方法關閉重試 |
| R9 | 重試不經過節流器 | `IgdbProvider.SendAsync` | 短時間超出 IGDB 的速率限制 | 把節流改成 DelegatingHandler 放進 pipeline |
| R10 | `MaxAttempts` 與佇列設定重複 | `IngestionOperationExecutor`；`runtime/tasks.tf` | 兩邊不一致時會加劇 R3 | 改為從設定讀取，並在文件中註明兩邊的關係 |
| R11 | syncJobs 更新使用 ReplaceOne | `MongoSyncJobRepository.UpdateAsync` | 並行時 last-writer-wins | 改用欄位層級的 `$set`（主題 A） |
| R12 | `EnrichJobRunner` 重複註冊 | 兩個 `DependencyInjection.cs` | 註冊位置不一致（Q5） | 只保留一處 |
| R13 | 註解與實際行為不一致 | `EnrichCommandHandler` XML 註解；sync 一律回 202 | 誤導維護者 | 更新註解；區分 200 與 202 |
| W3 | 精選頁錯誤時顯示「還沒有精選」 | `showcase.component.ts` → `fetchPage` | 使用者誤以為資料遺失 | 區分錯誤狀態與空狀態 |
| W4 | `ProviderService` 只探測一次，失敗就視為沒有 provider | `core/api/provider.service.ts` | 功能入口消失到重新整理為止 | 失敗時保留「未知」狀態並允許重試 |
| W5 | 刪除品類後 dialog 沒有關閉 | `features/categories/categories.component.ts` → `remove` | 畫面殘留空的 modal | 成功後呼叫 `dismiss()` |
| W6 | `locationId` 是半死的欄位 | `ItemWritePayload`；`UpdateItemCommandHandler` | 以 API 設定的值會被 UI 清成 null | 移除欄位，或補上 UI |
| W7 | 拼貼牆可能出現重複的 track key | `shared/showcase-sections/collage-section.component.ts` → `replaceRandomSlot` | NG0955 警告、DOM 被錯誤重用 | 挑選下一件時跳過畫面上已有的品項 |
| W8 | 公開頁把所有錯誤都當成「不存在」 | `features/public/public-share.component.ts` | 訪客與擁有者都被誤導 | 只有 404 顯示「不存在」，其他錯誤顯示「暫時無法載入」 |
| W9 | `DynamicFormComponent` 沒有取消舊的訂閱 | `shared/dynamic-form/dynamic-form.component.ts` | 少量記憶體無法回收；間接促成 C1 | 用 effect cleanup 取消訂閱；重建表單後主動發出一次目前的值 |
| W10 | 沒有 CSP（前端面向） | `index.html` | 見 I3 | 見 I3、P9 |
| P3 | 啟動時的索引與 seed 不隨回滾復原 | `Api/Program.cs` | 回滾後舊版讀到新的 schema；冷啟動較慢 | 維持向後相容的變更原則；評估移到部署步驟 |
| P4 | 沒有 PR CI；Web 部署不跑測試 | `.github/workflows/*` | 未經驗證的變更進入 master 或正式環境 | 新增 PR 觸發的 test workflow；Web workflow 補上 `npm test`（Q4） |
| P5 | 同一 workflow 依序部署 API 與 Web，可能只成功一半 | `.github/workflows/deploy-production.yml` | 前後端版本錯配；維運文件描述錯誤 | 明確定義相容性原則；修正 `production-operations.md` |
| P6 | 【推論】canary 期間有兩個實例 | `runtime/services.tf`；`rollout-cloud-run.sh` | 節流器與記憶體佇列被分成兩份 | 接受並記錄；S3 改走持久化佇列後影響會縮小 |
| P7 | 【推論】workflow 取消時流量卡在 40/60 | `.github/scripts/rollout-cloud-run.sh` | 下次部署被擋，需要手動處理 | 同時 trap `TERM`／`INT`；在文件中寫明手動回復步驟 |
| P8 | `/health/startup` 不可能回 503 | `Api/Program.cs` | 名實不符；兩支 health 都不檢查 Mongo | 修正文件說明，或讓 startup 檢查真正反映依賴狀態 |
| P9 | nginx 設定殘留，沒有安全 header | `web/nginx.conf`、`web/Dockerfile` | 誤導閱讀者；缺少 CSP（I3） | 移除殘留設定；加上 CSP、`X-Frame-Options`、`Referrer-Policy` 等；評估以非 root 執行 |
| P10 | 受追蹤的檔案中有個人識別資料 | `infra/terraform/runtime/variables.tf`；`infra/acceptance/phase7-acceptance.ps1` | PII 進入 git 歷史 | 改用 `TF_VAR_*` 或不受追蹤的 tfvars；驗收腳本改用參數 |
| P11 | ForwardedHeaders 信任任何來源 | `Api/Program.cs` | 自架時可偽造來源 IP；會成為 I1 的繞過點 | 依部署環境設定 KnownNetworks；Cloud Run 上維持 1 跳 |
| P12 | Actions 以 tag 釘版；deployer 有 `run.admin` | `.github/workflows/*`；`infra/terraform/bootstrap/wif.tf` | 供應鏈與權限範圍略寬 | 改以 commit SHA 釘版；評估改用 `run.developer` 加上必要的 actAs |
