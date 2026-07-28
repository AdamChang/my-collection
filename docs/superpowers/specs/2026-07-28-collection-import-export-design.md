# MyCollection 收藏資料匯入／匯出設計

日期：2026-07-28
狀態：已通過對話設計審核，待書面規格審閱

## 1. 目標

支援在兩台各自獨立部署的機器（家中／辦公室）之間搬移收藏資料。使用者在 App 內匯出一個封存檔，帶到另一台匯入，收藏內容即完全一致。

範圍涵蓋自訂品類、手建品項（含圖片檔案）與公開分享連結。不涵蓋使用者帳號、Steam API Key，以及 Steam 同步產生的品項。

本次不改動既有 API contract、動態 schema 機制與品項操作流程。

## 2. 已確認的產品決策

- 形態為 App 內建功能：API 端點加 Settings 頁 UI，不是開發者腳本。
- 匯入語意是**快照取代**，不是合併 upsert。不做雙向差異比對，不比 `UpdatedAt`。
- 匯出範圍：自訂 category、`Source != Steam` 的 item（含圖片）、ShareLink。
- 不匯出 Steam 同步來的 item（另一台重跑同步即可取得），不匯出 ExternalAccount（以 `SECRET_PROTECTION_KEY` 加密，兩台金鑰不同解不開，明文匯出等同外洩金鑰）。
- 圖片只匯出 `full` 尺寸，`card` 與 `thumb` 於匯入時由 `IImageProcessor` 重新生成。
- 匯入時**保留**本機的 Steam item，不清除。
- 匯入階段二開始前自動產生一份備份。
- 傳輸架構採同步單一 ZIP 串流，不做非同步 job。

## 3. 架構決策與理由

### 3.1 同步串流，不做非同步 job

`GET /api/export` 直接對 `HttpResponse.Body` 開 `ZipArchive`，逐筆寫出，不落暫存檔也不整包進記憶體，因此記憶體耗用與收藏規模無關。

專案已有 `SyncJob` 與 `ShowcaseImageQueue` 的非同步先例可循，但非同步方案會引入 job 狀態機、暫存檔清理與輪詢 UI。本功能是單一使用者手動觸發、一天至多數次的操作，同步串流的簡單性勝過非同步的彈性。若日後資料量真的撐不住，Application 層的 handler 可原封不動搬進背景服務。

代價：串流開始後無法再改 HTTP status code，中途失敗只能斷線。可接受。

### 3.2 manifest 使用 MongoDB Canonical Extended JSON

`Item.Attributes` 是 `BsonDocument`，內容由使用者自定的 category schema 決定，可能含 `DateTime`、`Decimal128`、`Int32`／`Int64`。`System.Text.Json` 會把這些型別壓成 string 或 number，來回一趟即失真。

Canonical Extended JSON 輸出 `{"$oid":…}`、`{"$date":…}`、`{"$numberDecimal":…}`，保證無損 round-trip。整份 manifest 統一用這個序列化器，不與 `System.Text.Json` 混用——混合兩種序列化器只會讓邊界出錯的機率倍增。

輸出對人略不友善，但這是機器對機器的傳輸格式，不預期人工編輯。

### 3.3 系統品類 id 不需重新對應

`SystemCategoryDefinitions` 的四個系統品類使用固定 `ObjectId`（`000000000000000000000001`～`…0004`），兩台機器一致。引用系統品類的 item 直接沿用其 `CategoryId` 即可。

自訂 category 的 `ObjectId` 雖為隨機產生，但因匯入語意是快照取代（先清空再寫入），封存檔內的原 `ObjectId` 可直接沿用，不會撞號。

唯一需要重新對應的是 `OwnerId`，以及由 `OwnerId` 組成的媒體路徑。

## 4. 封存檔格式

副檔名 `.zip`，命名 `mycollection-{yyyyMMdd-HHmmss}.zip`。

```
manifest.json
media/{itemId}/{imageId}.webp      ← 只有 full 尺寸
```

媒體路徑刻意不含 `ownerId`。`ownerId` 是各機器註冊時各自產生的，帶進封存檔只會誤導；匯入端一律以當前登入者的 id 重組成 `{ownerId}/{itemId}/{imageId}-{full|card|thumb}.webp`。

`manifest.json` 頂層結構：

```
{
  "schemaVersion": 1,
  "exportedAt": <UTC>,
  "categories": [ { id, name, icon, kind, fields[], createdAt, updatedAt } ],
  "items": [ {
      id, categoryId, name, description, tags[], isShowcased, source,
      acquisition, attributes,
      images: [ { id, isPrimary, order, file } ],
      createdAt, updatedAt
  } ],
  "shareLinks": [ { slug, scope, includeCategoryIds[], includePrice, expiresAt, createdAt } ]
}
```

`images[].file` 是 zip 內的相對路徑。

`schemaVersion` 於匯入時嚴格比對，不符即整包拒絕。寧可要求重新匯出，也不用猜測的方式吃下舊格式。

## 5. 匯出

端點 `GET /api/export`，需登入。回應 `Content-Disposition: attachment`，串流 zip。

三個查詢都在 repository 層帶 `ownerId` filter，遵循專案「授權寫在倉儲層」的既有慣例，新增 export 專用查詢方法，不在 handler 內過濾。

| 集合 | 條件 |
|---|---|
| categories | `OwnerId == me`（系統品類 `OwnerId == null`，自動排除） |
| items | `OwnerId == me && Source != Steam`（`OpenGraph` 來源視為手建，須匯出） |
| shareLinks | `OwnerId == me` |

**圖片遺失不由匯出端處理。** manifest 依 DB 記錄寫出，檔案不存在就是 zip 內少一個 entry，由匯入端偵測並降級為 warning。這讓匯出維持單趟串流，不必為了預檢而把每個檔案開兩次。

匯出 handler 的輸出目標抽成 `Stream` 參數，供第 7 節的自動備份共用同一支實作。

## 6. 匯入

端點 `POST /api/import`，需登入，`multipart/form-data`。

ZIP 需要隨機存取 central directory，而 multipart stream 不可 seek，因此先將上傳內容落成一份暫存檔，處理結束後刪除（成功或失敗皆刪）。

### 6.1 階段一：驗證，完全不寫入

檢查項目：

- 檔案可作為 ZIP 開啟，且含 `manifest.json`
- `schemaVersion == 1`
- 各實體必填欄位齊備
- 每個 `item.categoryId` 指向的 category 存在：封存檔內的自訂品類，或四個系統固定 id 之一
- 每個 item 的 `attributes` 通過既有的 `AttributeValidator`

任一項失敗即回 `400` 並附完整錯誤清單。DB 與 media 未被修改，可安全重試。

### 6.2 階段二：套用

順序如下。設 `S` = 存活的 Steam item 仍引用的 `categoryId` 集合。

```
1. 刪 items where OwnerId == me && Source != Steam
   → 同時刪各自的 media 目錄 {ownerId}/{itemId}/
2. 刪 shareLinks where OwnerId == me
3. 逐一判定本機自訂 category（OwnerId == me）是否刪除
   ├ id 在封存檔中          → 刪除（第 4 步會以封存檔版本重新寫入）
   ├ id 不在封存檔 且 ∉ S   → 刪除
   └ id 不在封存檔 且 ∈ S   → 於封存檔中尋找同名 category
       ├ 找到 → 將那些 Steam item 的 CategoryId 改指過去，刪除本機這個
       └ 沒有 → 保留，列入 warning
4. 寫入封存檔的 categories（沿用原 ObjectId）
5. 寫入封存檔的 items（沿用原 ObjectId），每張圖：
   從 zip 讀 full → IImageProcessor 重新生成三尺寸
   → 存為 {我的 ownerId}/{itemId}/{imageId}-{full|card|thumb}.webp
6. 寫入 shareLinks
```

### 6.3 第 3 步「同名改指」的必要性

兩台機器各自執行 Steam 同步時，`SyncCommand.EnsureDigitalCategoryAsync` 會各自建立一個 id 不同的自訂「數位遊戲」品類（該方法的 `OrderBy(x => x.OwnerId is null)` 會優先選用自訂品類而非系統品類）。

若無此步，每來回匯入一次就多累積一個同名品類。以名稱對應是此處唯一可用的錨點——`ObjectId` 天生對不上。

本次不修改 `EnsureDigitalCategoryAsync` 的選取邏輯，該行為不在本功能範圍內。

第 3 步只做刪除判定，不寫入；所有寫入集中在第 4 至 6 步，避免同一份資料在兩處被修改。

item 以 insert 寫入而非 upsert。第 1 步已刪除所有非 Steam item，理論上僅存的撞號來源是本機某個 Steam item 的 `ObjectId` 恰好與封存檔內某筆相同——`ObjectId` 由各機器獨立產生，實務上不會發生，若真的發生會拋 duplicate key 並落入 6.6 的中途失敗處理。不為此加額外的偵測邏輯。

### 6.4 媒體刪除的邊界

不可整個刪除 `{ownerId}/` 目錄：Steam item 的 showcase 快取圖也位於該目錄下，而 Steam item 依決策須保留。因此只逐 item 刪除 `{ownerId}/{itemId}/`。

需新增 `IFileStorage.DeleteDirectoryAsync(string relativePrefix)`。逐檔刪除只能清掉 DB 有記錄的檔案，孤兒檔會永久殘留；且未來換成 Google Cloud Storage 時，prefix 批次刪除是自然對應的操作。

### 6.5 ShareLink slug 衝突

`Slug` 為全域唯一。若封存檔中的 slug 已被其他使用者佔用，重新產生一個新 slug，並列入回應的 warnings。

### 6.6 原子性限制

MongoDB transaction 需要 replica set，而 `docker-compose.yml` 部署的是單機 standalone 實例，因此**階段二不是原子的**。中途失敗（例如 `IImageProcessor` 遇到損毀圖檔）會留下半殘狀態。

階段一的嚴格驗證是主要防線；第 7 節的自動備份是失敗後的復原手段。兩者都不構成原子性保證，UI 必須明確揭露這一點。

### 6.7 回應摘要

成功時回傳：匯入的 categories／items／images 筆數，以及 warnings 清單（manifest 列出但 zip 內缺少的圖片、被改號的 slug、因仍被 Steam item 引用而保留的孤兒品類）。

## 7. 匯入前自動備份

階段一驗證通過後、階段二開始前，以第 5 節的 export handler 產生一份備份：

```
{BackupRoot}/{ownerId}/pre-import-{yyyyMMdd-HHmmss}.zip
```

保留策略以使用者為單位：每個 `ownerId` 目錄各自只保留最近 3 份，超過則刪除該目錄下最舊者。

### 7.1 備份不得經過 IFileStorage

新增獨立設定 `Storage:BackupRoot`，Docker 掛載 `./data/backups`。備份檔案**不寫入 media root，也不經過 `IFileStorage`**。

原因：`MediaEndpoints.cs` 的 `GET /media/{**path}` 標記為 `AllowAnonymous`（公開分享頁需要匿名讀取圖片）。若備份寫在 media root 底下，其路徑幾乎可被猜出——`ownerId` 從公開分享頁的圖片 URL 即可取得，目錄名為固定字串，時間戳範圍有限。那等於把整份收藏資料庫掛在匿名端點上。

### 7.2 取用方式

備份檔案位於 host 的 `./data/backups/{ownerId}/`，使用者直接取檔，再以既有的匯入功能還原。

**不提供下載端點。** 開放端點就必須重做一次授權設計，而使用者本人已在該台機器前。

### 7.3 附帶的防禦性修補

為 `GET /media/{**path}` 加上副檔名白名單，僅放行 `.webp`。

備份已移出 media root，但該匿名端點目前能讀出 media root 底下任何檔案，而本功能會使該目錄下的內容增多。此修補為一行判斷，且風險由本功能直接觸發，不屬於無關重構。

## 8. 端點與設定變更

| 項目 | 變更 |
|---|---|
| `GET /api/export` | 新增，需登入，串流 zip |
| `POST /api/import` | 新增，需登入，multipart，單獨解除 Kestrel request body 大小限制 |
| `GET /media/{**path}` | 加副檔名白名單（僅 `.webp`） |
| `web/nginx.conf:8` | `client_max_body_size` 由 `12m` 提高至 `2g` |
| `docker-compose.yml` | api 服務新增 `Storage__BackupRoot: /app/data/backups` 與對應 volume `./data/backups` |
| `IFileStorage` | 新增 `DeleteDirectoryAsync(string relativePrefix)` |

`MediaEndpoints` 現有的單張圖片 10 MB 上限不變，匯入端點不套用該限制。

## 9. 前端

加在既有的 `web/src/app/features/settings/settings.component.ts`，不新增 feature 目錄。

**匯出**：一顆按鈕直接觸發下載。

**匯入**：選檔 → 破壞性確認對話框 → 上傳 → 顯示回應摘要。

確認對話框必須明確列出將被刪除的內容（本機手建品項筆數與其圖片、自訂品類、分享連結），並說明已自動備份與「非原子操作」的風險。

上傳期間鎖住按鈕並顯示進度，沿用專案既有的 loading 慣例。

## 10. 錯誤處理

| 類別 | 回應 | 資料狀態 |
|---|---|---|
| 檔案格式錯誤（非 zip、缺 manifest） | `400` + 錯誤說明 | 未變動，可安全重試 |
| 驗證失敗（schemaVersion、必填、schema 不符） | `400` + 完整錯誤清單 | 未變動，可安全重試 |
| 階段二中途失敗 | `500` + 已完成的步驟 | 半殘，須以備份還原後重試 |

沿用專案既有的全域例外處理，不在 handler 內逐處 try-catch。

## 11. 測試

**單元測試**

- manifest 序列化 round-trip，重點覆蓋 `Attributes` 內的 `Decimal128`、`DateTime`、`Int64` 無損還原
- 第 6.2 節 category 判定規則的四個分支
- ShareLink slug 衝突改號
- 備份保留策略：超過 3 份時刪除最舊者

**整合測試（Testcontainers 真 MongoDB）**

- 完整 export → 清庫 → import → 比對資料一致
- `ownerId` 換人後媒體路徑正確重組，且圖片可經 `/media` 讀取
- zip 內缺少圖片時降級為 warning，而非整包失敗
- 驗證失敗後 DB 確實未被修改
- 匯入後 Steam item 仍存在，且孤兒 category 依規則改指或保留

## 12. 明確不做的事

- 合併／差異比對匯入
- 非同步 job 與進度輪詢
- 備份下載端點與備份管理 UI
- 匯出 ExternalAccount 或任何加密祕密
- 修改 `EnsureDigitalCategoryAsync` 的品類選取邏輯
- 跨使用者匯入（封存檔一律匯入到當前登入者名下）
