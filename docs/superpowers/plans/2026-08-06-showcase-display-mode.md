# 精選牆展示模式（Hero / Stats / Collage）實作計畫

## Context

`/showcase`（內部精選牆）與公開分享頁目前只有一種呈現：`ItemCardComponent` 組成的網格。使用者想在既有網格之外，針對不同收藏類型加三種更有表現力的呈現——公仔模型/珍藏卡用 Hero 焦點展示、數位遊戲用 Stats 成就看板、所有精選品項共用一個 Collage 拼貼牆。

這是經過完整 grilling 的設計（`docs/adr/0007-showcase-display-mode-and-collage-is-unfiltered.md`、`docs/adr/0008-storage-location-never-public-rating-opt-in.md`，`CONTEXT.md` 已有「展示模式」詞條，都在分支 `docs/showcase-display-mode-grilling` 上）。本計畫把兩份 ADR 落地成程式碼變更。所有引用的檔案路徑與現有邏輯（`$set` 清單、白名單投影、enum→string 慣例）都已在規劃階段實際開檔驗證過，不是猜測。

**已確認的最終決定**（含這輪追加的一個）：
- `DisplayMode` 列舉 `List|Hero|Stats`；`Category.DefaultDisplayMode` 提供預設，`Item.DisplayMode?` 可覆寫；Collage 不受此篩選，只看 `IsShowcased`。
- 新增系統品類「公仔模型」「珍藏卡」，`DefaultDisplayMode = Hero`。
- 新增 `Item.Rating: int?`（1–10）、`Item.StorageLocation: string?`。
- `StorageLocation` 永不進公開分享頁（無開關）；`Rating` 比照 `IncludePrice` 新增 `ShareLink.IncludeRating`。
- **本輪新決定**：公開頁 Hero 卡片的「購買日期」比照 Price，掛在既有 `IncludePrice` 開關下（不新增旗標）。

---

## 1. 後端 Domain

- `src/MyCollection.Domain/Entities/Category.cs`：新增 `public enum DisplayMode { List, Hero, Stats }`；`Category` 新增 `DisplayMode DefaultDisplayMode { get; set; } = DisplayMode.List`。
- `src/MyCollection.Domain/Entities/Item.cs`：新增 `DisplayMode? DisplayMode`、`int? Rating`、`string? StorageLocation`。
- `src/MyCollection.Domain/Entities/ShareLink.cs`：新增 `bool IncludeRating`（預設 false）、`int CollageSlotCount = 4`。

## 2. 後端 Infrastructure（Mongo）

- **`SystemCategoryDefinitions.cs`**：`Category(...)` factory 加 `DisplayMode` 參數；4 個既有品類指定（實體遊戲/音樂專輯/電影光碟→`List`，數位遊戲→`Stats`）。新增固定 Id `...0005`＝公仔模型（icon `toy-brick`）、`...0006`＝珍藏卡（icon `award`），都是 `Physical`／`Hero`：
  - 公仔模型：`比例`(Select: 1/4,1/6,1/7,1/8,未標示比例,其他, searchable+showOnCard)、`製造商`(Text)、`角色作品`(Text, searchable)、`材質`(Text)、`限定版本`(Text)、`保存狀況`(Select，沿用實體遊戲同款選項)
  - 珍藏卡：`簽名者`(Text, searchable+showOnCard)、`鑑定編號`(Text)、`卡片編號`(Text)、`發行系列`(Text)、`保存狀況`(Select，同上)
  - 不加「購買日期」品類欄位——讀 `Item.Acquisition.AcquiredAt`。
- **`SystemCategorySeeder.cs`**：`UpdateOneModel` 的 `$set` 加 `.Set(x => x.DefaultDisplayMode, category.DefaultDisplayMode)`，否則新品類每次重啟都被沖回 `List`。
- **`MongoCategoryRepository.cs`** `UpdateAsync`（第 46-51 行 `$set` 清單）：加 `.Set(x => x.DefaultDisplayMode, category.DefaultDisplayMode)`。**已驗證現況確實缺這行**，不加的話使用者在 UI 改品類預設模式會靜默無效。
- **`MongoItemRepository.cs`** `UpdateAsync`（第 118-128 行 `$set` 清單）：加 `.Set(x => x.DisplayMode, item.DisplayMode)`、`.Set(x => x.Rating, item.Rating)`、`.Set(x => x.StorageLocation, item.StorageLocation)`。**同樣已驗證現況缺這三行。**
- **`IPublicCatalogReader.cs`**（已重新開檔核對，內容與計畫假設一致）：
  - `PublicItemProjection` 加 `DisplayMode DisplayMode`（永遠投影，不受任何旗標控制——用來算 `EffectiveDisplayMode`）、`int? Rating`（`includeRating` 才有值）、`DateTime? AcquiredAt`（`includePrice` 才有值，比照本輪決定）。
  - `ListItemsAsync` 簽章加 `bool includeRating` 參數（緊接在 `includePrice` 後面）。
  - `ListCategoryNamesAsync` 更名為 `ListCategoriesAsync`，回傳型別改為 `IReadOnlyDictionary<ObjectId, PublicCategoryInfo>`；新增 `public sealed record PublicCategoryInfo(string Name, DisplayMode DefaultDisplayMode, IReadOnlyList<CategoryFieldDto> CardFields);`（`CardFields` 只放 `ShowOnCard = true` 的欄位，讓公開頁 Hero 卡片能跟內部頁一樣顯示品類專屬屬性——呼應先前「內部/公開共用呈現邏輯」的決定，Attributes 本身早就全量透傳，只是缺這份「該顯示哪幾個」的清單）。
- **`MongoPublicCatalogReader.cs`**（已重新開檔核對）：
  - `BaseProjection`（第 17-23 行）加 `.Include(x => x.DisplayMode)`（無條件）。
  - `ListItemsAsync`：`includePrice` 時額外 `.Include("acquisition.price")` **和** `.Include("acquisition.acquiredAt")`；`includeRating` 時額外 `.Include(x => x.Rating)`。**`StorageLocation` 永遠不出現在這個檔案裡，不寫任何相關程式碼**——這就是 ADR-0008 的具體落實。
  - `ToProjection(BsonDocument)`（第 66-81 行）：仿現有 `GetValue(key, BsonNull.Value) is { IsBsonNull: false }` pattern 解析 `displayMode`（`Enum.Parse<DisplayMode>(..., ignoreCase: true)`，Mongo 因 `EnumRepresentationConvention(BsonType.String)` 存字串）、`rating`、`acquisition.acquiredAt`。
  - `ListCategoryNamesAsync` → `ListCategoriesAsync`：查詢加 `.Include(x => x.DefaultDisplayMode)`、`.Include(x => x.Fields)`，組出 `PublicCategoryInfo`（`Fields` 篩 `ShowOnCard` 後轉成 `CategoryFieldDto`，可直接呼叫既有 `CategoryMapper.ToDto(CategoryField)`）。

## 3. 後端 Application

- **`CategoryDtos.cs`**：`CategoryDto` 加 `string DefaultDisplayMode`；`ToDto` 用 `.ToString()`。新增 `CategoryMapper.ToDisplayModeLookup(IEnumerable<Category>)` 共用 helper。
- **`CategoryCommands.cs`**：`Create/UpdateCategoryCommand` 加 `string DefaultDisplayMode`；驗證仿現有 `Kind` 規則（`Enum.TryParse<DisplayMode>`）；handler 用 `Enum.Parse`。
- **`ItemDtos.cs`**：`ItemDto` 加 `string? DisplayMode, int? Rating, string? StorageLocation, string EffectiveDisplayMode`；`ItemMapper.ToDto` 簽章改為 `ToDto(Item item, DisplayMode categoryDefaultDisplayMode)`，`effective = item.DisplayMode ?? categoryDefaultDisplayMode`。
- **`ItemCommands.cs`**：`Create/UpdateItemCommand` 加 `string? DisplayMode, int? Rating, string? StorageLocation`；驗證 `Rating` 1–10、`DisplayMode` 合法列舉值（都用 `.When(...HasValue/NotBlank)`）；兩個 handler 已有 `category` 變數在手（`ItemWriteHelper.ResolveAsync` 產出），呼叫 `ItemMapper.ToDto(item, category.DefaultDisplayMode)` 取代原本的單參數呼叫。
- **`ItemQueries.cs`**：**這是唯一會擴大範圍的地方，已驗證屬實**——`ItemDto.EffectiveDisplayMode` 非 nullable，導致 `SearchItemsQueryHandler`（68 行）、`GetItemQueryHandler`（88 行）也要能算出它，否則編不過。兩者建構子加 `ICategoryRepository categories`；`SearchItemsQueryHandler` 一次 `ListAsync` 建 lookup dict 傳給每筆 `ItemMapper.ToDto`；`GetItemQueryHandler` 用 `categories.GetAsync(item.CategoryId, ct)` 單筆查（查不到就退回 `DisplayMode.List`，防呆用，正常不會發生）。
- **`GetShowcaseQuery.cs`**：`GetShowcaseQueryHandler` 同樣加 `ICategoryRepository categories`，載入一次、建 lookup。
- **`ShareDtos.cs`**：`ShareLinkDto` 加 `bool IncludeRating, int CollageSlotCount`；`PublicItemDto` 加 `string EffectiveDisplayMode, int? Rating, DateTime? AcquiredAt`（**不加 `StorageLocation`**）；`PublicShareDto` 加 `int CollageSlotCount`，`PublicItemDto`（或新增一個小型 `PublicCardFieldDto`）視需要帶上該品項所屬品類的 `CardFields`，供公開 Hero 卡片渲染屬性。
- **`ShareCommands.cs`**：`CreateShareLinkCommand` 加 `bool IncludeRating, int CollageSlotCount = 4`（trailing optional，降低既有呼叫點衝擊）；驗證 `CollageSlotCount` 1–10；`ShareMapper`/handler 直接透傳，比照 `IncludePrice`。
- **`GetPublicShareQuery.cs`**：改呼叫 `ListCategoriesAsync`；把 `link.IncludeRating` 傳進 `ListItemsAsync`；組 `PublicItemDto` 時用 category lookup 算 `EffectiveDisplayMode`／`CategoryName`／`CardFields`；`CollageSlotCount = link.CollageSlotCount`。

## 4. 後端 API 層

`src/MyCollection.Api/Endpoints/{Item,Category,Showcase,Share}Endpoints.cs`：不需要改路由。新欄位都是既有 request/response record 的追加屬性，走 minimal-API record binding 自動生效（跟當初加 `IncludePrice`/`LocationId`時一樣）。實作時逐一确认沒有任何 endpoint 用位置參數解構命令記錄。

## 5. 精選牆抓資料策略（已定案，不再是選項）

**單次抓取、前端切分，不加新的後端篩選參數。**

- `GetShowcaseQuery`/`GetPublicShareQuery` 現有回傳就是「未依展示模式篩選」的所有精選品——這正好同時滿足 List 網格（本來就要全部）跟 Collage（依 ADR-0007 也要全部）。Hero／Stats 只是對同一份陣列做 `effectiveDisplayMode` 篩選，前端 `computed()` 就能做。
- `/showcase`：`ShowcaseComponent` 第一次呼叫 `catalog.showcase(1, 200)`（既有驗證器上限本來就是 200）取代目前的 24；Hero/Stats/Collage 用這批資料算，List 網格「載入更多」邏輯不變（後續分頁只餵 List）。
- `/p/:slug`：`IPublicCatalogReader.ListItemsAsync` 本來就一次回全部，不用改。
- 不採「加 `mode` 查詢參數」：Collage 本身沒有對應的篩選條件（就是「全部」），加參數只會多出三次來回還是得在後端 join 品類預設值，複雜度不划算。

## 6. 前端 models / services

- `web/src/app/core/models.ts`：`export type DisplayMode = 'List' | 'Hero' | 'Stats';`；`CategoryDto` 加 `defaultDisplayMode`；`ItemDto` 加 `displayMode/rating/storageLocation/effectiveDisplayMode`；`ShareLinkDto` 加 `includeRating/collageSlotCount`；`PublicItemDto` 加 `effectiveDisplayMode/rating/acquiredAt/cardFields`（**不加 storageLocation**）；`PublicShareDto` 加 `collageSlotCount`。
- `catalog.service.ts`：`ItemWritePayload` 加三個新欄位；`showcase()` 簽章不變（呼叫端自己決定傳 200）。
- `category.service.ts`：`CategoryWritePayload` 加 `defaultDisplayMode`。
- `share.service.ts`：`ShareWritePayload` 加 `includeRating/collageSlotCount`。

## 7. 前端新元件（Hero / Stats / Collage）

新資料夾 `web/src/app/shared/showcase-sections/`：

- `showcase-display-item.ts`：共用介面 `ShowcaseDisplayItem { id, name, description, imageUrl, effectiveDisplayMode, acquiredAt, price, rating, storageLocation, attributes, cardAttributes }`；`toShowcaseDisplayItem(item, categories)`（內部頁，`cardAttributes` 篩 `Category.Fields` 的 `showOnCard`，邏輯抽取自 `ItemCardComponent.cardAttributes`／`imageUrl`，避免重複實作 header→cover→icon 的圖片挑選）；`toPublicShowcaseDisplayItem(item)`（公開頁，`storageLocation` 恆為 `null`，`cardAttributes` 讀 `item.cardFields`）。
- `hero-section.component.ts`：單品項輪播，Ken Burns（CSS `@keyframes` + `setInterval`/signal 驅動索引），側欄顯示 name/cardAttributes/acquiredAt/price/storageLocation（若非 null）/description/rating；`items` 為空時整個 `@if` 掉，無空狀態文字。
- `stats-section.component.ts`：單品項輪播，背景圖 `headerUrl→coverUrl→iconUrl`，`playtimeForever`／`psnProgress` 缺值時不渲染該列。
- `collage-section.component.ts`：固定槽位（`slotCount: input<number>(4)`），拍立得傾斜＋定時替換，資料來源是整批精選品（不篩選）。
- 三個元件都用 inline `styles`（沿用 `showcase.component.ts` 現行風格，不開 SCSS 檔）。

`showcase.component.ts`：inject `CategoryService`；第一次 `load()` 用 `pageSize=200`；新增 `heroItems`/`statsItems` computed；版面依序插入 `<app-hero-section>` → `<app-stats-section>` → `<app-collage-section [slotCount]="4">` → 現有 `.showcase__wall` 網格。

`public-share.component.ts`：同順序，`toPublicShowcaseDisplayItem` 轉換，`<app-collage-section [slotCount]="data.collageSlotCount">`；不需要動分頁邏輯。

## 8. 前端 item-detail／categories／settings

- **`item-detail.component.ts`**：仿現有 `[(ngModel)]` 純欄位風格，加 `rating/storageLocation/displayModeOverride`；`<section data-item-showcase>`（`isShowcased` 附近）加 rating number input、storage location text input、display-mode override select（含「沿用品類預設」空選項）；`hydrate()`/`toPayload()` 對應補上。
- **`categories.component.ts`**：`kind` select（第 54-60 行）後面加 `defaultDisplayMode` select；`startNew()`/`edit()` 補預設值/回填。
- **`settings.component.ts`**：仿照第 92-116 行 `includePrice` checkbox 的位置與 class 樣式，加 `includeRating` checkbox 與 `collageSlotCount` number input（1–10）；`createShare()` 補欄位；分享清單渲染（~109 行）比照 `@if (share.includePrice)` 加 `@if (share.includeRating)` 徽章與 `collageSlotCount` 顯示。

## 9. 測試

**後端（延伸既有 flat 檔案，不新建）**：
- `Unit/SystemCategoryDefinitionsTests.cs`：兩個新品類的欄位存在性、6 個品類的 `DefaultDisplayMode` 斷言。
- `Integration/SystemCategorySeederTests.cs`：`HaveCount(4)`→`6`，`AssertCategory` helper 加 `DefaultDisplayMode` 參數。
- `Unit/CategoryCommandTests.cs`：`ValidCommand(...)` helper 建構子改動會牽動既有測試；加 `DefaultDisplayMode` 驗證/持久化案例。
- `Unit/ItemCommandTests.cs`：`Command(...)` helper 同上；加 Rating 範圍驗證、DisplayMode 覆寫往返、StorageLocation 持久化、確認這三個欄位屬於「可變」（跟 Source/ExternalRef/Images 不同）。
- `Unit/ShowcaseQueryTests.cs`：`GetShowcaseQueryHandler` 建構子多一個 `ICategoryRepository`，既有兩個測試都要補 mock；加「品類預設 vs 品項覆寫」案例。
- `Unit/ShareCommandTests.cs`：`CreateShareLinkCommand(...)` 呼叫點補新欄位；`ListCategoryNamesAsync` mock 改成 `ListCategoriesAsync`／`PublicCategoryInfo`；補 `IncludeRating`/`AcquiredAt` gating 案例。
- `Integration/MongoPublicCatalogReaderTests.cs`：`ListItemsAsync` 呼叫補 `includeRating` 參數；新增「`StorageLocation` 在任何旗標組合下都不出現」的回歸測試（這是 ADR-0008 唯一的防線，值得單獨測）；`EffectiveDisplayMode`/`AcquiredAt` gating 案例。
- `Integration/MongoItemRepositoryTests.cs` / `MongoCategoryRepositoryTests.cs`：新欄位透過 `UpdateAsync` 的往返測試（直接驗證 §2 提到的 `$set` 清單有沒有漏）。
- `Integration/CatalogEndpointsTests.cs`：建立品項帶 rating/storageLocation/displayMode，經 `/items` 與 `/items/{id}` 都能讀回、`effectiveDisplayMode` 正確。
- `Integration/ShareEndpointsTests.cs`：`/public/{slug}` 原始 JSON 字串搜尋確認沒有 `storageLocation` 這個 key（不只是反序列化檢查，防止未來有人在白名單外加欄位卻沒發現）。

**前端（Angular spec）**：
- `showcase.component.spec.ts`：加 `CategoryService` stub、混合 `effectiveDisplayMode` 的 fixture、斷言第一次呼叫 `pageSize=200`、Hero/Stats 有無資料時的顯示/隱藏。
- `public-share.component.spec.ts`：fixture 補新欄位，斷言 DOM 中永遠沒有 storageLocation 相關內容（即使測試 fixture 故意塞了也不該被讀取/渲染）。
- `item-detail.component.spec.ts`：fixture 補三個新欄位，hydrate/toPayload 往返案例。
- `categories.component.spec.ts`：fixture 補 `defaultDisplayMode`，斷言新 select 存在且雙向綁定。
- 三個新元件各自新增 `*.spec.ts`（全新元件，這裡不適用「延伸既有檔案」的慣例）。

**手動瀏覽器驗證**：Hero/Stats 輪播與動畫、Collage 傾斜替換、空分區完全隱藏、List 網格與「載入更多」不受影響、公開頁 `includePrice=false/true` 時購買日期跟著 Price 一起出現/消失、`includeRating` 獨立控制評分顯示、**storageLocation 在任何旗標組合下都不出現在網路回應或 DOM**、`collageSlotCount` 1 與 10 的邊界、品類/品項的展示模式覆寫在 `/showcase` 正確生效。

## 執行前必查清單（依 CLAUDE.md 的「實作計畫驗證」規則）

以下已在規劃階段實際開檔確認，執行時無需重查：`IPublicCatalogReader.cs`、`MongoPublicCatalogReader.cs`、`MongoItemRepository.cs`、`MongoCategoryRepository.cs`、`ItemDtos.cs`、`ItemQueries.cs`、`ItemCommands.cs`（`ItemWriteHelper`/兩個 handler 的 `category` 變數作用域）、`CategoryDtos.cs`、`MongoConventions.cs`（enum 存字串）。

以下**尚未開檔**，執行到對應步驟前務必先讀一次再動手：`ShareCommands.cs`、`ShareDtos.cs`、`ShareCommandTests.cs`、`MongoPublicCatalogReaderTests.cs`、`ItemEndpoints.cs`/`CategoryEndpoints.cs`/`ShowcaseEndpoints.cs`/`ShareEndpoints.cs`、`item-detail.component.ts`／`categories.component.ts`／`settings.component.ts` 全文、`ItemCardComponent`（供抽取共用圖片挑選邏輯）、`SystemCategorySeederTests.cs`、`CatalogEndpointsTests.cs`、`ShareEndpointsTests.cs`。

### Critical Files
- `src/MyCollection.Domain/Entities/{Item,Category,ShareLink}.cs`
- `src/MyCollection.Infrastructure/Mongo/{SystemCategoryDefinitions,SystemCategorySeeder,MongoItemRepository,MongoCategoryRepository,MongoPublicCatalogReader}.cs`
- `src/MyCollection.Application/Items/{ItemDtos,ItemQueries,ItemCommands}.cs`
- `src/MyCollection.Application/Categories/{CategoryDtos,CategoryCommands}.cs`
- `src/MyCollection.Application/Sharing/{IPublicCatalogReader,ShareDtos,ShareCommands,GetPublicShareQuery}.cs`
- `src/MyCollection.Application/Showcase/GetShowcaseQuery.cs`
- `web/src/app/core/models.ts`
- `web/src/app/features/showcase/showcase.component.ts`
- `web/src/app/features/public/public-share.component.ts`
- `web/src/app/shared/showcase-sections/*`（新增）
