# IGDB 前端整合設計

後端 14 個 Task 已完成（`docs/superpowers/plans/2026-08-01-igdb-metadata-backend.md`）。
本文件規範前端如何接上這些端點，以及一項連帶的後端改動。

前置設計：`docs/superpowers/specs/2026-08-01-igdb-metadata-design.md`。

---

## 1. 目標

讓使用者能夠：

1. **搜尋建檔** — 新增遊戲品項時以關鍵字搜尋 IGDB，選一筆帶入表單
2. **批次補完** — 對 Steam 同步進來、尚未帶 IGDB 資料的品項一次補齊
3. **單筆重抓／綁定** — 對既有品項更新 IGDB 資料；未綁定的品項可透過搜尋綁定

## 2. 後端已就緒的端點

| 端點 | 用途 |
|---|---|
| `GET /ingest/providers` | 列出實際註冊的 provider 與能力旗標 |
| `GET /ingest/search?provider=igdb&q=…&limit=…` | 關鍵字搜尋，回 `FetchedMetadataDto[]` |
| `POST /ingest/enrich/igdb` | 補完。body 可省略；帶 `itemIds` 為單筆模式 |

`GET /categories/{id}/missing-fields` 與 `POST /categories/{id}/ensure-fields`（後端 Task 14）**本次不接**，見 §9。

## 3. 已定案的決定

| # | 決定 | 理由 |
|---|---|---|
| 1 | 範圍為搜尋建檔 + 補完 | 自訂品類的欄位補齊服務的是尚未出現的情境 |
| 2 | 先選品類才能搜尋，attributes 依品類 schema 過濾 | 後端 `AttributeValidator` 拒絕未宣告的 key |
| 3 | 搜尋 UI 用原生 `<dialog>` 對話框 | 同系列遊戲名稱高度相似，封面是唯一能一眼分辨的線索 |
| 4 | 既有品項依狀態顯示單一按鈕 | 消除「按了沒反應」的失敗模式 |
| 5 | `coverUrl` 納入 `ShowcaseImageDownloader` 的來源候選 | 實體遊戲沒有 `headerUrl`，否則封面只是一串文字 |
| 6 | IGDB 未設定時完全隱藏 | 與後端「沒憑證就整組不註冊」同一立場 |

## 4. 架構

### 4.1 新增檔案

| 檔案 | 職責 |
|---|---|
| `web/src/app/core/api/provider.service.ts` | 抓一次 `/ingest/providers` 存進 signal；`supports(key, capability)` |
| `web/src/app/shared/igdb-search-dialog/igdb-search-dialog.component.ts` | 對話框：輸入關鍵字、呼叫搜尋、封面網格、`(select)` 吐出選中的 DTO |
| `web/src/app/features/settings/igdb-enrich.component.ts` | 設定頁的批次補完面板 |

對話框刻意**不知道**品類、不知道是新增還是綁定、不寫任何東西回表單。它只做「搜尋、讓使用者挑、把挑中的吐出去」。
套用語意全部留在 `ItemDetailComponent`，因為那才是知道自己處於哪種模式的地方。

`igdb-enrich.component.ts` 獨立成檔而非塞進 `settings.component.ts`，比照同資料夾既有的 `data-transfer.component.ts`。

### 4.2 修改檔案

| 檔案 | 改動 |
|---|---|
| `web/src/app/core/models.ts` | `SyncJobDto` 補 `skipped: number`；新增 `ProviderDto` |
| `web/src/app/core/api/ingestion.service.ts` | 新增 `providers()`、`search()`、`enrich()` |
| `web/src/app/features/item-detail/item-detail.component.ts` | 掛對話框、統一套用路徑、既有品項的狀態相依按鈕 |
| `web/src/app/features/settings/settings.component.ts` | 同步紀錄加「略過」欄、掛 `<app-igdb-enrich>` |
| `src/MyCollection.Infrastructure/Imaging/ShowcaseImageDownloader.cs` | 來源候選加 `coverUrl`；`ResolveSourceUrl` 改為 `public` 以便測試 |

`ItemDetailComponent` 目前 311 行，改完約 380 行。它是專案最大的元件，但改動集中在「套用外部中繼資料」這一件事，
且既有的 OpenGraph `fetchMetadata()` 會一併收斂進同一個 `applyMetadata()`——淨結果是兩個來源共用一條路徑，不另外拆檔。

### 4.3 型別

```ts
export interface ProviderDto {
  key: string;
  /** 逗號分隔的能力旗標，例如 "BulkSync, UrlLookup" 或 "Search"。 */
  capabilities: string;
}
```

`SyncJobDto` 補上 `skipped: number`，位置比照後端 `SyncJobDto` 的 `failed` 之後。

### 4.4 ProviderService 的抓取時機

在 `ProviderService` 的建構子裡發出請求，結果寫進 `providers` signal，初值為空陣列。
**不使用 `APP_INITIALIZER`**——那會讓整個應用在這個請求完成前無法渲染，
而它的結果只影響三個按鈕該不該出現。初值為空的後果是「按鈕晚幾百毫秒才出現」，
比「整頁白畫面等一個非關鍵請求」好。

服務是 `providedIn: 'root'`，第一次被注入時建構、之後共用同一個實例，因此請求只會發一次。

## 5. 三條流程

### 5.1 搜尋建檔（新增品項）

```
選品類 → 按鈕啟用 → 開對話框 → 輸入關鍵字 → 網格挑一筆 → 關閉 → 帶入表單 → 使用者確認 → 儲存
```

- 按鈕位置：既有的「從商品網址自動填表」fieldset **下方**，與它同屬 `@if (!itemId())` 區塊。
  兩者並列而非合併，因為它們是兩個獨立的來源，沒有共用狀態
- 品類未選時按鈕**停用**（非隱藏），`title` 寫明「請先選擇品類」。原因是暫時的、使用者可自行解除
- `GET /ingest/search?provider=igdb&q=…&limit=20`
- 結果網格每格：封面（`imageUrl`）、名稱、發售年份（`attributes.releaseDate` 前四碼）、開發商
- 套用（`prefill` 模式）：

```ts
this.name = dto.name;
this.description = dto.description ?? '';
const merged = { ...this.attributes(), ...this.declaredOnly(dto.attributes) };
this.initialAttributes.set(merged);   // 重建表單
this.attributes.set(merged);          // 未經編輯就儲存時的實際送出值
```

- 儲存走既有的 `POST /items`，不新增後端路徑

建出來的品項 `source` 為 `Manual`、`externalRef` 為 `null`，這是正確的。`igdbId` 落在 `attributes`，
後端 `ExternalIdFor` 優先讀 marker，因此它已可定址。它不會出現在批次補完的候選裡
（候選條件是「有 `externalRef` 且缺 marker」），也不需要——它一出生就是完整的。

### 5.2 批次補完（設定頁）

- 只在 `supports('igdb', 'Search')` 為真時渲染整個面板
- **不提供 limit 輸入框**，固定送後端預設值 50，按鈕下方寫「一次處理最多 50 筆尚未補完的品項」。
  補過的品項不再是候選，所以「再按一次」就是下一批。這比一個數字輸入框更容易理解，也少一個要驗證的欄位
- `POST /ingest/enrich/igdb` → 回 `SyncJobDto`
- `finalize` 裡重載紀錄表，成功失敗都要（比照既有 `sync()`，失敗的 job 也會留紀錄）
- 通知文案：`補完完成：更新 12、略過 3、失敗 0`
- 同步紀錄表格插入「略過」欄，位置在「更新」與「失敗」之間；`colspan` 6 → 7

### 5.3 既有品項（品項詳情頁）

```ts
readonly igdbAddressable = computed(() => {
  const item = this.item();
  return item != null
    && (item.attributes['igdbId'] != null || item.externalRef?.provider === 'steam');
});
```

**必須檢查 `provider === 'steam'`，不可只檢查 `externalRef != null`。**
OpenGraph 建的品項也有 `externalRef`，但後端會組出 `opengraph:xxx` 這種 IGDB 反查不了的識別碼，
結果是略過。把它當成可定址，就是把使用者送進一顆按了沒反應的按鈕。

| 狀態 | 按鈕 | 行為 |
|---|---|---|
| 可定址 | 重新從 IGDB 抓取 | `POST /ingest/enrich/igdb` 帶 `{itemIds:[id]}`，成功後重載品項 |
| 不可定址 | 從 IGDB 搜尋並綁定 | 開同一個對話框，`bind` 模式套用 |

`bind` 模式**只套 attributes，不動 `name` 與 `description`**，且**不自動儲存**——留在表單裡讓使用者按儲存。
自動儲存會繞過表單驗證，也剝奪「挑錯了想反悔」的機會。

單筆重抓回來若是 `updated === 0 && skipped > 0`（Steam appid 在 IGDB 沒有對應條目，這會發生），
通知必須說「IGDB 查無對應」，**不可以說「完成」**。

## 6. attributes 過濾（重要）

`attributes` signal 只在 `(valueChange)` 觸發時更新，而 `DynamicFormComponent` 的表單重建**不會**觸發 `valueChanges`
（見 `item-detail.component.ts` 對 `initialAttributes` 的註解）。因此若使用者套用搜尋結果後直接儲存、
中途未編輯任何欄位，送出的就是原封不動的來源內容——包含品類沒宣告的 key，後端回 400。

過濾必須明寫：

```ts
private declaredOnly(source: Record<string, unknown>): Record<string, unknown> {
  const declared = new Set(this.selectedCategory()?.fields.map((f) => f.key) ?? []);
  return Object.fromEntries(Object.entries(source).filter(([key]) => declared.has(key)));
}
```

這是後端 `EnrichCommandHandler.ToEnrichment` 那條政策的第二份實作。避不掉：
`/ingest/search` 不知道目標品類，無法在伺服器端過濾。**兩處要一起改**，spec 在此標明。

`prefill` 與 `bind` 兩種模式都必須經過 `declaredOnly()`，OpenGraph 的 `fetchMetadata()` 收斂後亦同。

## 7. 錯誤處理

沿用既有機制，不新增任何一條。專案已有 `errorInterceptor` 統一顯示 RFC 9457 ProblemDetails，
元件一律用 `IGNORE_HANDLED_BY_INTERCEPTOR` 吞掉錯誤、只在 `finalize` 解鎖按鈕。三個新元件全部照辦，
**不寫 per-call 的錯誤 UI**。

| 情況 | 後端 | 使用者看到 |
|---|---|---|
| IGDB 未設定 | `NotFoundException` → 404 | 看不到，按鈕未渲染 |
| IGDB 故障／逾時／被限流 | `ProviderException` → 502 | 攔截器的 `Provider 'igdb' failed.` |
| 品類沒宣告的 key | `AttributeValidator` → 400 | 應永不發生，`declaredOnly()` 已擋住 |

三個**不是**錯誤、不走攔截器的情況：

1. **搜尋結果為空** — 對話框顯示「查無符合的遊戲」空狀態
2. **單筆重抓 `skipped: 1`** — 通知說「IGDB 查無對應」，見 §5.3
3. **`GET /ingest/providers` 失敗** — `ProviderService` 內部 `catchError(() => of([]))`，退化成「IGDB 不可用」。
   這是啟動時的背景請求，不該在使用者還沒做任何事之前就跳錯誤

## 8. 測試

沿用既有的 TestBed + 假服務模式（`useValue: { jobs: () => of([]) }`），不引入 `HttpTestingController`——
專案目前未使用它，元件測試餵假服務已足夠。

| 檔案 | 涵蓋 |
|---|---|
| `igdb-search-dialog.component.spec.ts` | 送出查詢字串；結果渲染成網格；點選吐出正確的 DTO；空結果顯示空狀態；搜尋中按鈕停用 |
| `provider.service.spec.ts` | `supports()` 正確解析 capabilities 字串；請求失敗時退化成空清單而非拋出 |
| `igdb-enrich.component.spec.ts` | 未設定 IGDB 時整個面板不渲染；補完後重載紀錄表；通知含略過數 |
| `item-detail.component.spec.ts`（擴充） | 品類未選時按鈕停用；`prefill` 覆寫名稱、`bind` 不覆寫；`declaredOnly` 濾掉未宣告的 key；`opengraph` 不算可定址 |
| `settings.component.spec.ts`（擴充） | 紀錄表渲染「略過」欄 |
| `ShowcaseImageDownloaderTests.cs`（新增） | 只有 `coverUrl` 時會被選為來源；`headerUrl` 優先於 `coverUrl` |

最關鍵的三個測試，對應這個設計裡真正會出錯的地方：

- `declaredOnly` 的過濾 —— 漏了就 400
- `bind` 不覆寫名稱 —— 漏了就吃掉使用者的資料
- `opengraph` 不算可定址 —— 漏了就是按了沒反應的按鈕

`ResolveSourceUrl` 目前是 `private static`。改為 `public static` 以便直接測試——它是無狀態的純函式，
所屬類別本來就是 `public`，這比為了一行改動引入 `InternalsVisibleTo` 便宜。

## 9. 明確不做

- **自訂品類的欄位補齊 UI**（後端 Task 14 的兩個端點）。系統的實體／數位遊戲品類已內建 IGDB 欄位，
  走不到那條路；該情境已有優雅降級（`ToEnrichment` 濾掉未宣告的 key）
- **Url 欄位的圖片預覽**。把「Url 欄位」一律當成圖片是過強的假設——使用者自訂的 Url 可能是商品頁、說明書 PDF，
  渲染成破圖比不渲染更糟。要做對得先引入「這個 Url 是圖片」的宣告，那是另一個設計
- **搜尋的即時輸入（debounce 自動搜尋）**。IGDB 限制 4 req/sec 且後端有程序層級節流，
  逐字送出會讓節流器排隊、拖慢每一次擊鍵的回應。明確按下搜尋
- **搜尋結果分頁**。`limit` 固定 20。挑不到就換關鍵字，比翻頁快
- **同步後自動補完**。後端刻意不做（見前置設計 §3.6 的決定），前端不繞過它

## 10. 已知風險

1. **`declaredOnly()` 與後端 `ToEnrichment` 是同一條政策的兩份實作。**
   IGDB 欄位集合變動時兩處都要改。緩解方式是測試明確涵蓋過濾行為，讓漏改立刻失敗
2. **`ItemDetailComponent` 會成長到約 380 行。** 目前判斷不拆，因為新增的內容與既有的 `fetchMetadata`
   本質相同、應該共用一條路徑。若之後再接第三、第四個來源，屆時應把「套用外部中繼資料」整體抽成獨立單元
3. **對話框的封面圖直接載入 `images.igdb.com`。** 這是跨網域的外部資源，未經本地代理。
   IGDB 的 CDN 掛掉時網格會是一片破圖，但名稱與年份仍可讀，功能不中斷
