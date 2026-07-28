# MyCollection 系統品類與 Neon Grid 全站改造設計

日期：2026-07-28
狀態：已通過對話設計審核，待書面規格審閱

## 1. 目標

本次改造解決兩個問題：

1. 全新或既有資料庫啟動後，所有使用者都能立即使用「實體遊戲、數位遊戲、音樂專輯、電影光碟」四個內建品類。
2. 將目前接近瀏覽器預設樣式的 Angular 前端，改造成一致、可讀、響應式的 Neon Grid Cyberpunk 介面。

既有路由、API contract、動態 schema 機制與核心操作流程維持不變。本次不導入 UI 元件庫，也不新增與收藏管理無關的功能。

## 2. 已確認的產品決策

- 四個預設品類是全域系統品類，`ownerId = null`。
- 系統品類對所有使用者可見，但不可編輯或刪除。
- 既有與新註冊使用者都使用同一份系統品類，不為每位使用者複製資料。
- 系統品類的動態欄位全部選填。
- 常用欄位設為可搜尋；每個品類只挑 2–3 個欄位顯示在卡片上。
- UI 使用已選定的 A 方向「Neon Grid／霓虹網格」。
- Cyberpunk 改造涵蓋全站，包括匿名公開分享頁。
- 使用原生 Angular 與共用 CSS design tokens，不導入第三方 UI framework。

## 3. 系統品類架構

### 3.1 啟動流程

新增獨立的 `SystemCategorySeeder`。API 啟動時依序執行：

1. `MongoIndexInitializer.EnsureIndexesAsync`
2. `SystemCategorySeeder.SeedAsync`
3. `app.Run`

Seeder 直接使用 `MongoContext.Categories`，不經過帶有 `IUserContext` 的使用者 Repository。這可明確表達「系統資料初始化」與「使用者資料寫入」是兩種不同責任。

MongoDB 無法連線、建立索引失敗或 seed 失敗時，API 維持現有 fail-fast 行為，不接受請求。

### 3.2 冪等性與多 instance 安全

四個系統品類使用固定 `ObjectId`：

| 品類 | 固定 ID |
|---|---|
| 實體遊戲 | `000000000000000000000001` |
| 數位遊戲 | `000000000000000000000002` |
| 音樂專輯 | `000000000000000000000003` |
| 電影光碟 | `000000000000000000000004` |

Seeder 以 `_id` 為 filter 執行 upsert：

- `$set`：`ownerId`、`name`、`icon`、`kind`、`fields`、`updatedAt`
- `$setOnInsert`：`createdAt`

固定 ID 讓多個 API instance 同時啟動時仍只會產生一份資料。每次啟動都會把系統 schema 更新到程式定義的版本，但不會修改任何使用者自訂品類或收藏品項。

### 3.3 欄位設計原則

欄位參考 Colnect 收藏目錄常見的分類維度，例如遊戲主機、音樂載體與唱片公司、電影光碟格式與區域資訊；實際 schema 收斂為個人收藏最常用的 metadata，避免複製外部網站的完整分類複雜度。

所有欄位：

- `required = false`
- key 使用既有 camelCase 規則
- 不使用難以長期維護的大型固定選項清單
- 適合自由擴充的值使用 `Text`
- 只有穩定且短小的封閉集合使用 `Select`

參考來源：

- [Colnect Video Games Catalog](https://colnect.com/en/video_games)
- [Colnect Music Records Catalog](https://colnect.com/en/music_records)
- [Colnect Movies Catalog](https://colnect.com/en/movies)

## 4. 四個預設 schema

### 4.1 實體遊戲

- `icon = "gamepad-2"`
- `kind = Physical`

| Key | 標籤 | 型別 | 可搜尋 | 卡片顯示 |
|---|---|---|---:|---:|
| `platform` | 平台 | Text | 是 | 是 |
| `edition` | 版本 | Text | 是 | 是 |
| `region` | 區域 | Text | 是 | 否 |
| `mediaFormat` | 媒體格式 | Select：光碟、卡匣、記憶卡、其他 | 是 | 是 |
| `developer` | 開發商 | Text | 是 | 否 |
| `publisher` | 發行商 | Text | 是 | 否 |
| `releaseDate` | 發售日期 | Date | 否 | 否 |
| `productCode` | 產品編號 | Text | 是 | 否 |
| `barcode` | 條碼 | Text | 是 | 否 |
| `condition` | 保存狀況 | Select：全新、近全新、良好、普通、需修復 | 是 | 否 |

### 4.2 數位遊戲

- `icon = "gamepad-2"`
- `kind = Digital`

| Key | 標籤 | 型別 | 可搜尋 | 卡片顯示 |
|---|---|---|---:|---:|
| `platform` | 平台／商店 | Text | 是 | 是 |
| `developer` | 開發商 | Text | 是 | 否 |
| `publisher` | 發行商 | Text | 是 | 是 |
| `releaseDate` | 發售日期 | Date | 否 | 否 |
| `productCode` | 產品編號 | Text | 是 | 否 |
| `playtimeForever` | 遊玩時數（分鐘） | Number | 否 | 是 |
| `headerUrl` | 封面圖網址 | Url | 否 | 否 |
| `iconUrl` | 圖示網址 | Url | 否 | 否 |

`playtimeForever`、`headerUrl`、`iconUrl` 必須保留，因為它們是現有 Steam provider 寫入的 attributes。

### 4.3 音樂專輯

- `icon = "disc-3"`
- `kind = Physical`

| Key | 標籤 | 型別 | 可搜尋 | 卡片顯示 |
|---|---|---|---:|---:|
| `artist` | 演出者 | Text | 是 | 是 |
| `mediaFormat` | 媒體格式 | Select：CD、黑膠唱片、卡帶、SACD、其他 | 是 | 是 |
| `albumType` | 專輯類型 | Select：專輯、單曲、EP、精選輯、原聲帶、其他 | 是 | 否 |
| `label` | 唱片公司 | Text | 是 | 是 |
| `catalogNumber` | 目錄編號 | Text | 是 | 否 |
| `country` | 國家／地區 | Text | 是 | 否 |
| `releaseDate` | 發行日期 | Date | 否 | 否 |
| `genre` | 曲風 | Text | 是 | 否 |
| `style` | 風格 | Text | 是 | 否 |
| `barcode` | 條碼 | Text | 是 | 否 |

### 4.4 電影光碟

- `icon = "film"`
- `kind = Physical`

| Key | 標籤 | 型別 | 可搜尋 | 卡片顯示 |
|---|---|---|---:|---:|
| `discFormat` | 光碟格式 | Select：Blu-ray、4K UHD、DVD、VCD、其他 | 是 | 是 |
| `edition` | 版本 | Text | 是 | 是 |
| `director` | 導演 | Text | 是 | 是 |
| `studio` | 片商 | Text | 是 | 否 |
| `regionCode` | 區碼 | Text | 是 | 否 |
| `country` | 國家／地區 | Text | 是 | 否 |
| `releaseDate` | 發行日期 | Date | 否 | 否 |
| `genre` | 類型 | Text | 是 | 否 |
| `barcode` | 條碼 | Text | 是 | 否 |

## 5. Steam 同步相容性

現有 Steam 同步在找不到「數位遊戲」時會建立使用者品類。系統品類加入後，選擇順序調整為：

1. 同名的使用者自訂品類
2. 系統「數位遊戲」
3. 僅作防禦性 fallback：建立使用者品類

這個順序確保：

- 已經有 Steam 資料的使用者繼續寫入原本的 category ID，不會把同一遊戲庫拆成兩個品類。
- 新使用者直接使用系統「數位遊戲」，不產生重複品類。
- 異常缺少 seed 資料時，同步仍可運作。

## 6. Neon Grid 視覺系統

### 6.1 視覺語言

整體採深藍黑介面，收藏圖片仍是畫面主角。Cyberpunk 感由以下元素組成：

- 青藍作主要互動色與 focus 色。
- 洋紅僅用於錯誤、警示與少量裝飾。
- 細網格、掃描線與局部光暈只存在於大面積背景。
- 面板使用細邊框、半透明深色表面與小幅切角。
- 等寬字體只用於編號、狀態與 eyebrow label；中文正文維持系統無襯線字體。
- hover 可有短暫位移或光暈，不使用循環閃爍。

建議 token：

```css
--mc-bg: #05070d;
--mc-surface: #09111a;
--mc-surface-raised: #0d1824;
--mc-border: #17384a;
--mc-text: #e9f7ff;
--mc-text-muted: #7f9aae;
--mc-cyan: #20e7ff;
--mc-cyan-soft: rgb(32 231 255 / 14%);
--mc-magenta: #ff2f8b;
--mc-warning: #f4d35e;
--mc-danger: #ff4d6d;
--mc-success: #46f2a5;
--mc-cut: 10px;
```

### 6.2 共用基礎

`web/src/styles.css` 負責：

- reset、頁面背景與字體
- design tokens
- 標題與連結
- button、input、select、textarea、fieldset
- focus-visible、disabled、error、success
- 通用 panel、badge、empty state、loading state
- reduced-motion 與小螢幕基礎規則

各 Angular 元件的 `styles` 只負責該元件自己的 layout 與獨特視覺，不重複定義按鈕、表單或色票。

### 6.3 應用殼層

登入後的應用殼層：

- 桌面版使用固定頂部控制列。
- 顯示 `MY//COLLECTION` 品牌、主要路由與登出操作。
- 目前頁面使用青藍底線、切角底色或左側狀態線表示。
- `main.shell` 保留合理最大寬度，但允許庫存頁使用較寬空間。
- toast 改為 Neon Grid 狀態面板，保留目前通知服務行為。

手機版：

- 導覽允許換行或改為水平可見的緊湊列。
- 操作目標至少 44px。
- 不允許頁面產生水平溢位。

### 6.4 全站頁面

#### 登入／註冊

- 全螢幕網格背景與置中終端面板。
- 清楚區分品牌、說明、欄位和主要提交動作。
- 登入／註冊切換仍使用同一畫面與既有邏輯。

#### 精選

- Header 改為「私人收藏終端」視覺。
- 顯示目前總件數等可由現有回應可靠取得的資訊；不虛構無 API 支援的統計。
- 空狀態提供清楚的下一步連結。
- 收藏牆使用較大的圖片與穩定網格。

#### 庫存

- 篩選側欄改為控制面板。
- 搜尋、品類、動態屬性與標籤保持既有行為。
- 結果區 header 清楚顯示件數與「新增品項」主要動作。
- 手機版側欄回到結果上方，欄位完整可操作。

#### 品項卡片

- 圖片使用固定比例，hover 時只做短暫、低幅度變化。
- 無圖片 placeholder 使用品項首字與網格紋理。
- 標題、精選 badge、schema 欄位、tags 建立清楚層級。
- 卡片仍整體可點擊，保持現有 router link。

#### 品項檢視／編輯

- 圖片區、核心欄位、動態欄位、購入資訊與操作區以面板分組。
- 儲存是唯一高強度主要動作。
- 刪除使用 danger 樣式並與一般動作分離。

#### 品類管理

- 系統品類與自訂品類視覺上清楚區分。
- 系統品類顯示唯讀狀態；不提供可造成誤解的編輯／刪除操作。
- schema 欄位編輯器維持現有功能，提升密度與對齊。

#### 設定

- Steam 綁定、同步操作、同步紀錄與分享設定使用一致面板。
- 成功、進行中、失敗狀態不只靠顏色辨識，必須有文字。

#### 公開分享

- 使用同一組色彩與卡片語言，但不出現登入後導覽。
- 頁首顯示分享名稱與收藏數量，內容優先於系統裝飾。
- 無效或過期分享保持清楚的錯誤狀態。

## 7. 動態與無障礙

- 所有互動元件必須有明顯 `:focus-visible`。
- 文字和背景需維持 WCAG AA 等級的實用對比。
- 使用 `@media (prefers-reduced-motion: reduce)` 停用非必要 transition 與背景效果。
- 不使用快速閃爍、持續掃描動畫或會干擾閱讀的 glitch 動畫。
- 顏色不是狀態的唯一傳達方式；狀態需搭配文字或圖形。
- 裝飾性網格與掃描線不得攔截滑鼠事件。

## 8. 錯誤處理

### 後端

- Seeder 失敗直接中止啟動並保留完整 server log。
- 不在 Seeder 內吞掉 MongoDB exception。
- 使用既有全域 ProblemDetails 處理執行期間 API 錯誤。
- 系統品類更新仍由 Repository 的 `ForbiddenException` 保護。

### 前端

- 沿用現有 error interceptor 與 notification service。
- 視覺改造不得加入各頁面自己的重複 try/catch 或 toast 邏輯。
- 載入、空資料與失敗狀態在版面上有清楚位置，不因深色主題而隱藏。

## 9. 測試策略

實作遵循 Red-Green-Refactor。

### 後端

1. Seeder 首次執行後存在四個系統品類。
2. Seeder 重跑不增加文件數量。
3. 每個固定 ID、名稱、kind、icon 與 fields 符合本規格。
4. 所有系統欄位都是選填。
5. 系統品類仍不可由一般使用者更新或刪除。
6. Steam 同步優先沿用既有使用者「數位遊戲」品類。
7. 沒有使用者同名品類時，Steam 同步使用系統品類。

### 前端

1. 應用殼層保留所有導覽路由與登出操作。
2. 登入／註冊切換、送出與 busy 狀態維持原行為。
3. 庫存搜尋、品類篩選、動態屬性篩選與標籤篩選維持原行為。
4. 系統品類不顯示可編輯或刪除操作。
5. Item card 仍顯示圖片 fallback、精選狀態、卡片欄位與 tags。
6. 主要空狀態仍包含正確的下一步操作。

### 完整驗證

```powershell
dotnet test
cd web
npm test -- --watch=false --browsers=ChromeHeadless
npm run build
```

另以桌面與手機 viewport 實際檢查登入、精選、庫存、品項編輯、品類、設定和公開分享頁，確認：

- 沒有水平溢位
- focus 清楚可見
- reduced-motion 生效
- 深色介面下文字、表單與錯誤訊息可讀

## 10. 不在本次範圍

- 新的 UI framework 或 icon package
- 主題切換器
- 自訂使用者色票
- 新增統計 API
- 位置階層 UI
- Discogs、IGDB 或電影資料 provider
- 修改既有 API 路由或 DTO
- 重構與本次視覺或系統品類無關的業務邏輯

## 11. 驗收條件

1. API 在空資料庫與既有資料庫啟動後，都能看見恰好四個指定系統品類。
2. 重啟 API 不會增加系統品類數量。
3. 系統品類不可編輯或刪除，自訂品類 CRUD 不受影響。
4. 新使用者執行 Steam 同步時使用系統「數位遊戲」；既有同名自訂品類仍被沿用。
5. 全站畫面使用一致的 Neon Grid 視覺語言。
6. 桌面與手機均可完成既有主要流程。
7. 所有既有與新增測試通過，Angular production build 成功。
