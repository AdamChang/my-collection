# 收藏匯入／匯出 — 第一段執行報告（Tasks 1–6）

日期：2026-07-29
分支：`feat/collection-import-export`
計畫：`docs/superpowers/plans/2026-07-28-collection-import-export.md`
規格：`docs/superpowers/specs/2026-07-28-collection-import-export-design.md`

## 1. 交付狀態

以下數字是在本機直接執行取得，非代理人回報：

```
dotnet build MyCollection.slnx   → 0 警告，0 錯誤
dotnet test  MyCollection.slnx   → 289 通過，0 失敗
git status                        → clean
```

基準 `0643812` 起算 17 個 commit。

**匯出功能完整可用。** `GET /export`（瀏覽器端經 nginx 為 `/api/export`）串流回一個 ZIP，內含 Canonical Extended JSON 的 manifest 與每張圖的 full 尺寸。需登入，查詢一律以擁有者過濾。

**附帶修補一個既有漏洞。** 匿名的 `GET /media/{**path}` 原本能讀出 media root 底下任何檔案，現已限定 `.webp`。

尚未實作：匯入端（Tasks 7–12）、部署設定（Task 13）、前端 UI（Task 14）。

## 2. Commit 序列

| SHA | 說明 |
|---|---|
| `5852bb2` | `IFileStorage.DeleteDirectoryAsync` |
| `f3ccc4d` | 補上路徑區段比對的契約說明 |
| `38e306d` | `/media` 限定 `.webp` |
| `fae42d3` | 封存檔模型與 Canonical Extended JSON 序列化 |
| `1e76a42` | `Read` 對惡意／損毀輸入的強化 |
| `417d9ce` | 計畫同步（Task 3 審查結果） |
| `74bc8d5` | `ITransferRepository` 與 Mongo 實作 |
| `1f8fbed` | 改以來源品類過濾 repoint、對齊 filter 風格 |
| `956a967` | 計畫同步（repoint 簽章） |
| `51108cb` | `ArchiveWriter` 與 `ArchiveMapper` |
| `bfbec42` | `ExternalRef` 納入封存檔格式 |
| `1a59adb` | 計畫同步（匯入端還原 `ExternalRef`） |
| `5f5a7ac` | 修正 Kestrel 同步 I/O 崩潰 |
| `cbf2c13` | 釋放緩衝、修正過度宣稱的註解 |
| `5603b52` | 計畫記錄進度與後續任務必須遵守的事實 |
| `7149e6a` | `GET /export` 端點 |
| `5488fe8` | 補上 ShareLink 擁有者過濾的測試覆蓋 |

## 3. 審查發現的缺陷

九個實質缺陷，全部是原始計畫的錯誤或規劃時未預見的問題。這是評估兩階段審查是否值得的主要依據。

### 3.1 計畫要求「建立」一個已存在的測試檔（Task 1）

`tests/MyCollection.Tests/Unit/LocalFileStorageTests.cs` 已存在且有 9 個測試。計畫寫「建立」並附上完整檔案內容，照做會覆蓋掉既有覆蓋率。

**根因**：規劃時用 `ls | head -40` 列目錄被截斷，沒看到該檔。
**處置**：改為附加，既有測試逐行保留（審查者以 `git show` 比對確認為純新增）。

### 3.2 白名單測試是假陽性（Task 2）

計畫提供的兩個版本都無效：偽造副檔名指向的檔案本來就不存在，修補前後都回 404，測試會綠但證明不了任何事。

**處置**：改為先透過 `_factory.Services` 取得 `IFileStorage`，把真實的 `.zip` 種進 storage root 再請求。修補前實測回 `200 OK` 並吐出檔案內容——確認漏洞為真。

### 3.3 例外契約與文件不符（Task 3）

審查者對實際的 `MongoDB.Bson 3.10.0` 實測 `BsonDocument.Parse`：

| 輸入 | 實際擲出 |
|---|---|
| 空白／亂碼 | `FormatException`（與文件相符） |
| **截斷的 JSON** | **`InvalidOperationException`** |
| 極深巢狀 | `StackOverflowException`，**無法攔截，直接終止行程** |

計畫在 Task 10 寫的 `catch (Exception e) when (e is FormatException or ArgumentException)` 會漏掉最常見的檔案損毀情形。

**處置**：新增 `InvalidArchiveException`，`Read` 內部統一包裹實際的例外家族，呼叫端只需處理一種。深度限制無法在此防禦，改為在文件明示，並由 Task 10 的匯入端以 `MaxManifestBytes = 64 MB` 在讀取前擋下。

### 3.4 `schemaVersion` 檢查時機過晚（Task 3）

原設計由 `ArchiveValidator` 檢查版本，但那發生在反序列化**之後**。全域 `IgnoreExtraElementsConvention(true)` 之下，v1 讀 v2 封存檔會「成功」並靜默丟棄不認得的欄位。

**處置**：`Read` 改為先解析成 `BsonDocument`、檢查 `schemaVersion`、再反序列化。`ArchiveValidator` 的版本檢查隨之成為死碼並移除。

### 3.5 磁碟格式共用 Domain 子型別（Task 3）

頂層型別已解耦（`ArchiveCategory` 與 `Category` 分開），但 `ArchiveCategory.Fields` 是 `List<CategoryField>`、`ArchiveItem.Acquisition` 是 `Acquisition`（內含 `Money`），全是 Domain 類別。Domain 改一個欄位，就會無聲改變所有已寫出封存檔的讀法，繞過 `SchemaVersion` 這個存在目的正是仲裁此事的機制。

這在本功能特別要緊：整個需求就是在兩台可能版本不同的機器之間搬資料。

**處置**：新增 `ArchiveCategoryField`、`ArchiveAcquisition`、`ArchiveMoney`。列舉（`CategoryKind`、`FieldType`、`ItemSource`、`ShareScope`）維持共用——新增列舉成員是相容變更。

### 3.6 `RepointItemsAsync` 的讀寫競態（Task 4）

原簽章收一份呼叫端事先讀出的 item id 清單。MongoDB 單機無 transaction，該清單必然是舊快照；Steam 同步若在讀取與寫入之間插入新品項，它不會被改指，接著來源品類被刪除就留下孤兒引用。

**處置**：改為 `RepointItemsAsync(fromCategoryId, toCategoryId, ct)`，在執行當下以來源品類過濾活資料。此舉同時簡化了 Task 7 的 `CategoryReconciler`——不再需要把品項按品類分組。

在匯入流程中安全，因為非 Steam 品項在步驟 1 已刪除，屆時留在該品類的只剩 Steam 品項。

### 3.7 封存檔帶 `Source` 卻不帶 `ExternalRef`（Task 5）

`ArchiveItem` 有 `Source` 欄位但沒有 `ExternalRef`。目前無實害——查證後確認 `ItemSource.OpenGraph` 在整個 codebase 中從未被賦值（`OpenGraphProvider` 只用於依網址抓中繼資料預填表單），所以可匯出的品項全是 `Source = Manual` 且 `ExternalRef` 為 null。

但這是內部不一致的格式：日後若把 OpenGraph 接成會建品項，匯出會保留 `Source` 卻靜默丟棄賦予它意義的來源連結。且當時補欄位是免費的（真實世界尚未產生任何封存檔），日後補則需要提升 `SchemaVersion`。

**處置**：新增 `ArchiveExternalRef` 及雙向對應。

### 3.8 同步 I/O 會在 `HttpResponse.Body` 上崩潰（Task 5）

**本段最重要的發現。**

`ArchiveManifestSerializer.Write` 使用同步的 `StreamWriter.Write`，`ArchiveWriter` 以 `using var` 釋放 `ZipArchive`（同步 `Dispose` 負責寫中央目錄）。Kestrel 預設 `AllowSynchronousIO = false`，兩處都會擲 `InvalidOperationException: Synchronous operations are disallowed`。

這正是 `ArchiveWriter` 文件宣稱的主要用途，Task 6 一接上就會 100% 崩潰。

當時全部四個單元測試都寫進 `MemoryStream`，而它無條件容忍同步 I/O——**這個失敗對既有測試套件是結構性隱形的**。

更關鍵的是：**第一次開的處方不足以修好**。改成 async 寫入加 `await using` 之後仍然拋出。實作者以反射查證得出根因：`ZipArchiveEntry` 的寫入串流（內部 `WrappedStream`）從未覆寫 `DisposeAsync`，因此 `await using` 一律退回基底 `Stream.DisposeAsync()`，也就是同步 `Dispose()`——與使用 `Open()` 或 `OpenAsync()` 無關。這是 .NET 執行期已知且未修的限制（`dotnet/runtime#107171`、`#1560`，皆標記為 Future）。

**處置**：`ArchiveWriter` 內新增私有的 `SyncSafeBufferedStream`，把函式庫發出的同步呼叫吃進記憶體緩衝，只在 writer 自己呼叫 `FlushBufferedAsync` 時才以非同步 I/O 送到目的地。每個 entry 關閉後即沖掉，尖峰額外記憶體約等於單一 entry。

**曾評估但否決的替代方案**：在端點設定 `IHttpBodyControlFeature.AllowSynchronousIO = true`（微軟對此情境的官方 workaround）。否決理由是它讓約束外洩——每個 HTTP 呼叫端都必須記得開這個旗標，忘記就是執行期崩潰。緩衝方案把問題封裝在 writer 內，Task 6 的端點因此不需要為此做任何事。

**回歸測試**：以一個拒絕同步 `Write`／`Flush` 的 stub stream 釘住，並經實作者確認在還原成同步版本時會變紅。

### 3.9 ShareLink 擁有者過濾無測試覆蓋（Task 6）

三個匯出整合測試都沒建立分享連結，但 `ArchiveWriter` 每次都會寫出 `manifest.ShareLinks`。`ListOwnShareLinksAsync` 的擁有者過濾因此完全未被測到——而漏寫 filter 就洩漏他人資料，正是本專案「授權寫在倉儲層」這條規則存在的原因。

**處置**：擴充既有兩個測試。實作者以突變測試證明有效性：把 `OwnShareLinks` 換成 `FilterDefinition<ShareLink>.Empty` 拿掉過濾，確認 `Export_excludes_other_users_data` 變紅並指出洩漏的 slug，再還原。

## 4. 執行中確立的設計決策

後續任務必須遵守。這些也已摘要在計畫檔的「目前進度」一節。

| 項目 | 決策 |
|---|---|
| manifest 寫入 | `WriteAsync(Stream, ArchiveManifest, CancellationToken)`，非同步 |
| manifest 讀取 | `Read` 維持同步。匯入端先把 entry 複製進 `MemoryStream`，碰不到 Kestrel 串流 |
| 版本檢查 | 在 `Read` 內、反序列化之前 |
| 例外 | MongoDB.Bson 的各種例外統一為 `InvalidArchiveException`；Task 10 需在 `GlobalExceptionHandler` 對應到 400 |
| 磁碟格式型別 | 類別自有（`ArchiveCategoryField` 等），列舉共用 |
| 對應 | 雙向集中於 `ArchiveMapper` |
| repoint | 以來源品類過濾，不收 id 清單 |
| 同步 I/O | 由 `ArchiveWriter` 內部處理，端點不得重複 workaround |
| 路由 | 後端**無** `/api` 前綴，nginx 的 `proxy_pass http://api:8080/` 會剝除 |

`ArchiveWriter` 的記憶體特性也一併修正為誠實描述：圖片逐張串流、不會全部常駐；manifest 中繼資料與收藏規模成正比；同步安全緩衝再加上單一 entry 的量。原本「耗用與收藏規模無關」的說法從來就不完全成立。

## 5. 用量

約 **1.69M** subagent tokens，六個任務。

| Task | 實作 | 兩輪審查 | 修正輪 | 小計 |
|---|---|---|---|---|
| 1 | 54k | 90k | 61k | 205k |
| 2 | 47k | 102k | — | 149k |
| 3 | 55k | 128k | 85k | 268k |
| 4 | 69k | 101k | 78k | 248k |
| 5 | 58k | 133k | 345k | 536k |
| 6 | 77k | 109k | 98k | 284k |

Task 5 的異常來自 §3.8：同步 I/O 問題來回三輪，其中一輪還因為第一次的處方不足而必須重新診斷。

**評估**：inline 執行大約是此數字的三到四分之一。但 §3.3、§3.8、§3.9 屬於「自己審自己會漏掉」的類型——尤其 §3.8 需要有人實際建 stub stream 並反射查證執行期行為，不是靠直覺能發現的。§3.8 若漏掉，會在 Task 6 以一個難以歸因的 500 浮現。

## 6. 下一段的硬約束

**Task 11 與 Task 12 必須在同一個工作階段內完成。**

Task 11 讓 `POST /import` 上線，那是一個會刪除使用者資料的端點；Task 12 的往返整合測試才是證明它正確的依據。停在兩者之間等於在系統裡留下一個未經驗證的破壞性端點。

若判斷用量撐不到 Task 12 結束，就不要開始 Task 11——停在 Task 10 之後，那裡是乾淨的（匯入元件都已寫好但無端點可觸發）。

## 7. 建議

第二段（Tasks 7–10）建議改用 inline 執行。那一段是純新增、無端點、不可觸發的程式碼，且計畫中的實作已因本段的審查結果同步修正過，探索空間小，審查的邊際價值低於前面。把 subagent 的預算留給第三段（Tasks 11–12），端點在那裡才真的上線。
