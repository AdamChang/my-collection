# MyCollection 圖片匯入／匯出設計

日期：2026-08-03
狀態：已實作（`image-transfer` 分支，commit `a2ab903`、`f15a2b0`）
取代：`2026-07-28-collection-import-export-design.md`

## 1. 為什麼改

開發環境已改用 MongoDB Atlas，兩台機器連的是同一個叢集。收藏資料因此天生就是同一份，前一版設計要解決的「把資料從 A 機搬到 B 機」這個問題已經不存在——匯出再匯入一次，等於把資料從自己搬到自己。

但圖片不在資料庫裡。上傳的檔案存在各機器自己的 `data/media`，換一台機器打開就是滿畫面破圖。**唯一還需要搬運的東西是圖檔本身。**

所以功能不是被刪除，而是被收窄到它真正還有意義的那一小塊。

## 2. 前提與推論鏈

整份設計建立在一個前提上，它若不成立則以下大半決策都要重來：

> 共用同一個 Atlas 叢集時，同一個帳號在每台機器上是同一份 user 文件，因此 `ownerId` 相同。

由此推得：

1. `Item` 的 `_id`、`ItemImage.Id` 在每台機器上都相同 → 圖片的儲存路徑 `{ownerId}/{itemId}/{imageId}-{size}.webp` 也相同。
2. 匯入端不需要重組路徑，**把檔案寫回原路徑就完成還原**。
3. 既然路徑不用改，DB 裡的 `ItemImage` 三個路徑欄位本來就指得對 → **匯入完全不需要寫 MongoDB**。
4. 既然不寫 DB、也不刪檔（見 6.3），匯入沒有破壞性 → **匯入前的自動備份整套失去存在理由**。

前一版設計的複雜度幾乎全部來自「兩邊 id 對不上」，這個前提一旦成立，那些複雜度就跟著消失。

## 3. 已確認的產品決策

- 匯入是純檔案還原，一個 Mongo 寫入都沒有。
- zip 內裝 `full`／`card`／`thumb` 三種尺寸的原檔，不重壓。
- 匯出清單從 DB 查（`items` 的 `Images`），不掃目錄。
- **不再排除 Steam 來源的品項**——IGDB 封面下載會讓 Steam 品項也帶本地圖檔。
- 檔案已存在就略過，不覆蓋、不比對內容。
- zip 內路徑的 ownerId 前綴與登入者不符 → 整包拒絕，一個位元組都不寫。
- manifest 保留，但只剩極簡標頭，且寫在最後一個 entry。
- `schemaVersion` 從 2 起跳，讀到 1 明確回報「這是舊的資料封存檔」。
- 序列化改用 `System.Text.Json`。
- 路由改為 `/images/export`、`/images/import`。
- 前端拿掉破壞性確認對話框。
- 資料庫備份不再由本 App 負責，改用 `mongodump` 或 Atlas 快照。

## 4. 封存檔格式

檔名 `mycollection-images-{yyyyMMdd-HHmmss}.zip`。

```
{ownerId}/{itemId}/{imageId}-full.webp
{ownerId}/{itemId}/{imageId}-card.webp
{ownerId}/{itemId}/{imageId}-thumb.webp
...
manifest.json                              ← 最後一個 entry
```

**entry 名就是 storage 的相對路徑。** 這是整個設計的樞紐：匯入端不需要解析語意、不需要對應表、不需要知道什麼是品項，只要把每個 entry 寫到同名路徑即可。副作用是這包 zip 直接 `unzip` 到 `data/media` 也等於完成匯入。

前一版刻意把 `ownerId` 排除在路徑之外（因為兩台機器的 id 不同，帶著只會誤導）；這一版反過來刻意帶上它，因為它現在是可驗證的身分標記。

### 4.1 三種尺寸都打包，不重新生成

前一版只帶 `full`，`card`／`thumb` 由 `IImageProcessor` 在匯入時重壓。這一版三種都原樣帶走：

- 匯入端不必相依 `IImageProcessor`，也沒有 CPU 成本。
- 重壓出來的檔案與來源機器不是同一份 byte，只是視覺上一樣；原樣複製才真的是「還原」。
- `card` 與 `thumb` 相對 `full` 很小，體積代價低。

### 4.2 manifest 內容

```json
{
  "schemaVersion": 2,
  "exportedAt": "2026-08-03T07:00:00Z",
  "ownerId": "66ae…",
  "fileCount": 42,
  "missing": [ { "itemName": "Kind of Blue", "path": "66ae…/…/img1-card.webp" } ]
}
```

**不列檔案清單**——zip 的中央目錄已經是清單，再寫一份只會多出一個必須跟著對齊的事實來源。

`ownerId` 存在的理由是給匯入端一個單一權威來源可比對，不必從每條 entry 路徑各自反推這包是誰的。

`fileCount` 的單位是**檔案**不是圖片張數（一張圖 = 三個檔）。回報路徑上下游統一用檔案數，避免「寫入 3」在不同地方指涉不同東西。

### 4.3 為什麼 manifest 寫在最後

`fileCount` 與 `missing` 都要等所有圖檔處理完才知道。zip 的 entry 順序是自由的，匯入端靠中央目錄定址，所以把 manifest 挪到最後就能在單趟串流中順便回報結果，不必為了預檢而多掃一次磁碟。

`ImageArchiveWriterTests.Writes_the_manifest_last_so_it_can_report_what_actually_went_in` 釘住這個順序。

### 4.4 版本號從 2 起跳

新舊格式結構完全不同，若沿用 1，舊封存檔會通過版本檢查，然後在下游因為缺 `ownerId` 炸出一個沒人看得懂的反序列化錯誤。從 2 起跳讓 `schemaVersion == 1` 成為一個可以明確命名的情況：「這是舊版的『收藏資料』封存檔，新版匯入只接受圖片封存檔。」

### 4.5 序列化改用 System.Text.Json

前一版用 MongoDB Canonical Extended JSON，理由是 manifest 內含 `BsonDocument`（`Item.Attributes`）與 `ObjectId`，一般 JSON 會讓 `Decimal128`／`DateTime` 失真。新 manifest 只有 `int`／`DateTime`／`string`，那個理由連同那些欄位一起消失了。

順帶拔掉的還有 64 MB 大小閘門：它存在的原因是 MongoDB 的 `JsonReader` 對巢狀深度沒有上限，極深巢狀的 JSON 會觸發無法攔截的 `StackOverflowException`。`System.Text.Json` 預設有 64 層深度上限，這個風險不存在。現在只留 1 MB 的記憶體配置護欄（`JsonDocument` 會把整份讀進來）。

## 5. 匯出

`GET /images/export`，需登入，串流 zip。

| 來源 | 條件 |
|---|---|
| items | `OwnerId == me && Images.Length > 0`，依 `_id` 排序 |

介面是 `IImageArchiveRepository`（單一方法 `ListItemsWithImagesAsync`），實作 `MongoImageArchiveRepository`。與 `IItemRepository` 分開，因為那裡的查詢一律分頁帶篩選，這裡要的是全表掃過去。

前一版的 `ITransferRepository` 有 13 個方法（刪除、改指、插入、slug 檢查……），這一版只剩讀取，所以連名字一起換掉。

### 5.1 保留的機制

- **`SyncSafeBufferedStream`**：`ZipArchiveEntry` 的寫入串流沒有覆寫 `DisposeAsync`，關閉 entry 時會對底層 Stream 發出同步 `Write`／`Flush`（dotnet/runtime#107171、#1560，官方標 Future 不會修）。直接包 `HttpResponse.Body` 會在收尾時炸掉。這個 buffer 把同步呼叫吃進記憶體，只在明確呼叫 `FlushBufferedAsync` 時才用非同步 I/O 送出。單元測試用一個拒絕同步 I/O 的假 Stream 守住它。
- **單趟串流**：不落暫存檔、不整包進記憶體，尖峰記憶體只跟單一 entry 大小成正比。

### 5.2 缺檔的處理

DB 有記錄但磁碟上沒檔時，記進 `manifest.missing`，不中斷匯出。前一版靠匯入端比對 manifest 的圖片清單來偵測；這一版 manifest 不列清單，所以改由匯出端當場記錄——否則缺檔會變成無聲消失，使用者要到某天看見破圖才知道。

## 6. 匯入

`POST /images/import`，需登入，`multipart/form-data`。

handler 只相依 `IFileStorage` 與 `IUserContext`。沒有 repository，沒有 MediatR 以外的任何寫入路徑。

ZIP 需要隨機存取中央目錄而 multipart stream 不可 seek，因此仍先落一份暫存檔，成功或失敗都刪除。

### 6.1 寫入前的整包驗證

依序檢查，任一項失敗即 `400`，且尚未寫入任何檔案：

1. 檔案可作為 ZIP 開啟
2. 含 `manifest.json`，且不超過 1 MB
3. `schemaVersion == 2`（讀到 1 給專門的訊息）
4. `manifest.ownerId == 當前登入者`
5. 逐一掃過所有非 manifest 的 entry：必須以 `{ownerId}/` 開頭、以 `.webp` 結尾、路徑片段不含 `..`

第 5 步是**收集與驗證同時進行**，全部通過才進入寫入迴圈。這個順序是有意的：若邊驗證邊寫入，前面幾個合法的 entry 已經落地了。

### 6.2 ownerId 前綴檢查是安全邊界，不是資料檢查

`LocalFileStorage.Resolve` 保證路徑不會逃出 storage root，但**不保證不會寫進別人的 owner 目錄**。少了第 5 步，任何已登入的使用者都能構造一個 zip 覆蓋別人的圖檔。

第 4 步（manifest 的 ownerId）與第 5 步（每條 entry 的前綴）刻意都做：前者給出看得懂的錯誤訊息，後者才是真正的防線。

### 6.3 已存在的檔案一律略過

路徑含 `imageId`，而 `imageId` 是每次上傳新產生的 `ObjectId`——同一個路徑不可能合法地裝著不同內容。所以略過既正確又快，重跑匯入幾乎零成本，中斷後直接重跑即可當續傳。

「不覆蓋」在測試中是用假的 `IFileStorage` 釘住的：它的 `DeleteAsync` 與 `DeleteDirectoryAsync` 一被呼叫就擲例外，「匯入永不刪除」這個性質因此不會被日後無聲改掉。

### 6.4 回應

```
{ written: int, skipped: int, warnings: string[] }
```

`warnings` 來自 `manifest.missing`，每筆一句，最多 20 筆，其餘併成「另有 N 個圖檔在匯出來源上就已遺失」。上限純粹是防止一份壞掉的封存檔洗版。

匯入端對缺檔無能為力，但必須說出來——這是 6.2 之外唯一會出現在回應裡的壞消息。

## 7. 移除的東西

| 移除 | 理由 |
|---|---|
| `IBackupStore`、`LocalBackupStore`、`Storage:BackupRoot`、compose volume | 匯入不再具破壞性，沒有需要復原的東西 |
| `ArchiveValidator` | 沒有實體要驗證了 |
| `CategoryReconciler` | 沒有品類要調和了（前一版最複雜的一塊） |
| `ArchiveMapper`、`ArchiveCategory`／`ArchiveItem`／`ArchiveShareLink` 等磁碟格式型別 | manifest 不再攜帶實體 |
| `ITransferRepository`（13 個方法）、`MongoTransferRepository` | 由單一方法的 `IImageArchiveRepository` 取代 |
| `ArchiveManifestSerializer`（Extended JSON） | 見 4.5 |

淨變更 **+919 / −2177**。

### 7.1 連帶失去的能力：資料庫的離線備份

舊的資料匯出同時也是「一鍵把收藏抓成離線檔」的唯一手段。Atlas 免費層（M0／Flex）沒有自動備份快照，拿掉它之後這件事沒有替代品。

**這是明知的取捨**：備份不是本 App 的職責。需要時用 `mongodump`，或升級到有快照的 Atlas 層級。

## 8. 端點與設定變更

| 項目 | 變更 |
|---|---|
| `GET /export` → `GET /images/export` | 改路由 |
| `POST /import` → `POST /images/import` | 改路由 |
| `docker-compose.yml` | 移除 `Storage__BackupRoot` 與 `./data/backups` volume |
| `StorageOptions` | 移除 `BackupRoot` |

路由刻意不掛在 `/media` 底下：那個前綴上有一個 `AllowAnonymous` 的 catch-all（`GET /media/{**path}`），讓需要授權的路由緊鄰它，路由優先序雖然正確（字面段優先於 catch-all），但日後很容易看錯。

`UnlimitedRequestBody`（解除 Kestrel 對匯入端點的 body 上限）與 nginx 的 `client_max_body_size 2g` 都維持不變。

## 9. 前端

`web/src/app/features/settings/image-transfer.component.ts`（由 `data-transfer.component.ts` 改名）。

流程簡化為：說明 → 匯出按鈕 → 選檔 → 開始匯入 → 結果。

**拿掉破壞性確認對話框。** 前一版那個 `role="alertdialog"` 的紅框列出將被刪除的內容並警告非原子操作；現在匯入只新增檔案、不碰資料庫，保留紅色警告只會讓使用者對這類警告麻痺。元件測試直接斷言 DOM 內沒有 `[role=alertdialog]`。

結果顯示「寫入 N 個圖檔，略過 M 個（這台機器上已經有了）」與警告清單。

## 10. 測試

**單元（18）**

- 匯出：三尺寸都進 zip 且 entry 名為 storage 路徑、manifest 是最後一個 entry、manifest 記錄 ownerId 與時間、缺檔進 `missing` 且不計入 `fileCount`、無圖時只剩 manifest、透過拒絕同步 I/O 的 Stream 不擲例外
- 匯入：寫入缺的檔、既有檔案內容不被動到、他人的封存檔整包拒絕、越界路徑（`../` 與別人的前綴）在寫入前拒絕且零寫入、非 `.webp` 拒絕、舊版 v1 給專門訊息、缺 manifest／缺 ownerId／非 zip 各自拒絕、缺檔轉成 warning、超過 20 筆時尾巴收斂

**整合（7，Testcontainers 真 MongoDB）**

核心情境是「第二台機器」：`ApiFactory` 每次建立都分配新的 `Storage:LocalRoot`，所以開第二個 factory 就天然是一台資料齊全、圖片全缺的機器。

- 匯出封裝 DB 記錄的三個路徑
- **第二台機器匯入後圖片可經 `/media` 讀取**（匯入前 404、匯入後 200，`Written=3`）
- 同一包再匯入一次 `Written=0 / Skipped=3`
- 他人帳號的封存檔回 400
- 空檔案回 400
- 兩個端點都需要授權

**前端（7 個元件測試 + 2 個服務測試）**

一鍵匯入且無 alertdialog、寫入／略過數回報、警告列示、失敗保留已選檔案、換檔清除舊結果、進行中鎖住按鈕、下載檔名為 `mycollection-images-*.zip`。

## 11. 明確不做的事

- 差異匯出（只帶對方缺的圖）——需要先知道對方有什麼，成本遠高於全量帶走後略過
- 掃目錄找孤兒檔一併帶走——刪圖時已一併刪檔，孤兒檔在此專案罕見
- 匯入時清理「DB 有記錄但兩邊都沒檔」的 `ItemImage`——那是資料修復，不是搬運
- 非同步 job 與進度輪詢
- 資料庫本身的匯出／備份（見 7.1）
- 跨帳號匯入
