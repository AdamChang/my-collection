# 庫存頁的返回點

## Problem Statement

使用者的實際流程是「篩出一組品項 → 點進去編輯 → 回列表 → 點下一筆 → 回列表…」，但每次回到 `/catalog` 篩選都歸零，必須重篩。

原因是篩選狀態全部只活在元件裡：`CatalogComponent` 的 `search`、`categoryId`、`attributeFilters`、`missingAttributes`、`selectedTags` 與私有的 `page`（`web/src/app/features/catalog/catalog.component.ts`）都是元件成員，沒有任何一項寫進 URL 或任何儲存。`/catalog` 是 lazy standalone route 且沒有 `RouteReuseStrategy`，離開路由即銷毀元件，狀態隨之消失——**不只導覽列的「庫存」會清空，按瀏覽器上一頁也一樣清空**。

同時 `ItemDetailComponent` 完全沒有返回列表的入口（只有刪除後會 `navigate(['/catalog'])`），使用者只能靠瀏覽器上一頁或導覽列，而導覽列的連結是寫死的 `/catalog`（`web/src/app/app.ts`）。

## Solution

引入**返回點**：使用者上一次離開庫存列表時的位置。

篩選條件改以 URL query param 為唯一真實來源，參數名沿用 API 既有的那一套；另外在 sessionStorage 保存一份返回點記憶 `{查詢字串, 已載入頁數, 錨點品項 id}`，供導覽列「庫存」連結與 item-detail 新增的「← 返回列表」按鈕組出目的地。三條返回路徑（瀏覽器上一頁、導覽列、返回按鈕）都回到同一個列表。

篩選面板另加一顆「清除全部篩選」，只在有條件生效時出現。

## User Stories

1. As a collector, I want the catalog to still be filtered after I edit an item and come back, so that I can work through one filtered batch without re-filtering between every item.
2. As a collector, I want the browser back button, the 庫存 nav link, and a 返回列表 button on the item page to all land me on the same filtered list, so that I don't have to remember which way back is the "safe" one.
3. As a collector who loaded three pages before clicking in, I want to come back to three pages, so that the item I was working near is still on screen.
4. As a collector, I want to come back scrolled to the card I just clicked, so that I can pick up at the next item instead of hunting for my place.
5. As a collector, I want the list re-queried when I come back, so that the edit I just saved is visible instead of a stale copy.
6. As a collector, I want a 清除全部篩選 button, so that I can get back to the whole collection in one click instead of emptying each control by hand.
7. As a collector, I want the filters in the address bar, so that reloading the page keeps them and I can keep a link to a filter I use often.
8. As a collector who opened an item URL directly in a new tab, I want 返回列表 to still work, so that the button is never a dead control.
9. As a collector, I want my filters gone when I close the tab, so that tomorrow's catalog opens showing my whole collection rather than a filter I no longer remember setting.
10. As a developer, I want the browser URL and the API query string to use one vocabulary, so that there is no translation layer to keep in sync.

## Implementation Decisions

- **URL 是篩選條件的唯一真實來源，返回點記憶只回答「回去要去哪」。** 兩者職責不重疊：URL 回答「這個列表現在包含哪些品項」，記憶回答「使用者上次離開時在哪」。純 URL 方案解不了導覽列與返回按鈕（它們不知道該帶哪些參數）；純 sessionStorage 方案會讓網址對列表內容說謊，也與精選頁 `?view=` 的既有慣例分歧。理由詳見 ADR-0010。

- **網址參數沿用 API 的詞彙**：`search`、`categoryId`、`tags`（可重複）、`attr.<key>`、`missingAttrs`（可重複）。與 `MyCollection.Api/Endpoints/ItemEndpoints.cs` 解析的那一套完全同名，瀏覽器網址與 API query string 幾乎同構，不需要翻譯層。網址會比較長（`categoryId` 是 24 字元的 ObjectId），這是可接受的代價；opaque 的 `?f=<base64>` 被否決——它讓「選 URL 當真實來源」的一半理由（可讀、可手改、可保存）當場失效。

- **陣列一律用重複 key，不用分隔符**：`?tags=a&tags=b`。tags 與 attribute 值都是使用者自由輸入的字串，逗號串接會在值本身含逗號時炸開。這也與後端 `query["tags"].ToArray()` 的解析一致。

- **query param 綁進元件後必須正規化形狀**：`app.config.ts` 已啟用 `withComponentInputBinding()`，而它對 query param 的形狀是**一個值給 `string`、多個值給 `string[]`**。所以要有一支解析函式把輸入收斂成確定形狀，比照 `shared/showcase-tabs/showcase-view.ts` 的 `parseShowcaseView`：壞值或缺值一律退回預設，不讓畫面壞掉。

- **`attr.` 前綴只切前 5 個字元，不可 `split('.')`**：attribute 的 key 由品類宣告、是使用者自訂的，可能含 `.` 或非 ASCII。後端用的是 `kv.Key[5..]`，前端解析必須用同樣的規則，否則 key 含點的欄位會在兩端解出不同結果。

- **「未設定」勾選也走網址**（`missingAttrs=platform`）。它是篩選條件的一部分，跟其他條件一起進 URL，不另外處理。

- **已載入頁數與錨點品項只進記憶、不進網址。** 它們回答的是「使用者剛才看到哪」——瀏覽進度，不是列表的身分。而且 `?page=3` 在這裡的語意是「載入第 1..3 頁」，與一般分頁的「第 3 頁」不同，放進可貼可改的網址等於埋一個誤解。

- **返回點的內容是 `{查詢字串, 已載入頁數, 錨點品項 id}`，還原時先比對查詢字串。** 與當前網址的查詢字串不一致就只還原第一頁、不捲動。否則使用者手改網址換了篩選，卻套用了上一組篩選的頁數與錨點。

- **記憶在每次篩選變更時覆寫**（`reload()` 與 `loadMore()` 是所有狀態變更的匯流點），不是在離開路由時才寫。這讓「清除全部篩選」**不需要任何額外的清記憶邏輯**——清除本身就走 `reload()`，記憶當場被覆寫成無篩選。若改成離開時才寫，使用者按了清除卻從某條沒觸發寫入的路徑離開，下次回來又被還原成舊篩選，那是最惡劣的一種 bug：他明明按了清除。

- **記憶存在 sessionStorage，生命週期即分頁的生命週期。** 關掉分頁就忘。刻意不用 localStorage：隔天打開看到一個空蕩蕩的列表卻不記得自己設過篩選，是很難自我診斷的困惑，而它省下的只是一次點選。解析失敗或形狀不符的舊記憶一律丟棄，比照 `parseShowcaseView` 對壞值的立場。

- **還原上限 8 頁（192 筆）。** `pageSize` 在 `ItemQueries.cs` 的 FluentValidation 與 `MongoItemRepository` 的 `Math.Clamp` 兩處都限制在 `1..200`，所以單一請求最多還原 8 頁。超出的部分讓使用者自己再按「載入更多」。刻意不放寬後端 clamp（為假想情境鬆動一道防禦性上限），也不拆成多個依序請求（多請求的順序與部分失敗換來的是一個幾乎踩不到的邊界）——真的捲過 192 筆還找不到東西時，正解是收窄篩選。

- **回列表一律重打 API，不重用離開前的結果。** 這個流程的核心動作就是編輯，快取會讓剛存的修改在列表上看起來沒生效，那比重篩嚴重得多。

- **錨點品項已不在結果中時，靜靜捲到頂端，不提示。** 要判斷「它是被使用者剛才的編輯改掉才消失的」得比對前後兩份結果集，成本不成比例；而那個消失多半正是使用者自己剛做的動作造成的。也不捲到「它原本的位置」——網格是 `repeat(auto-fill, …)`，重排後那個位置就是錯的。

- **錨點在點擊卡片時記下。** `ItemCardComponent` 目前整張卡片是 `[routerLink]`，沒有 click handler，需要由 catalog 這一側在卡片被啟動時記錄品項 id。捲動還原以 `scrollIntoView` 對準該卡片元素，不還原像素位移——網格寬度一變，像素位置就沒有意義。

- **「← 返回列表」按鈕永遠顯示**，沒有返回點時導向乾淨的 `/catalog`。涵蓋兩個情境：從「新增品項」進 `/items/new`（不是從卡片點進去的），以及直接把 `/items/:id` 貼進新分頁。一顆時有時無的按鈕比一顆偶爾回到未篩選列表的按鈕更難用；而且新增品項存檔後會 `navigate(['/items', saved.id])`，那時想回列表看看新東西是很自然的需求。

- **導覽列的「庫存」連結改為動態**，目的地取自返回點。它與返回按鈕必須行為一致——如果一個記得、一個忘記，會比全部忘記更糟，因為使用者無法預期。連結帶上 query param 後要確認 `routerLinkActive` 的比對行為仍然正確。

- **「清除全部篩選」只在有任何條件生效時出現。** 它必須同時清掉 `attributeFilters` 與 `missingAttributes`——後者是獨立的 signal（見 0002），一格一格手動清的話「未設定」勾選還得額外再點一次才解除。不做逐條 chip 的移除：chips 與左側面板顯示同一份資訊兩次，還要處理「未設定」這種非值型條件的呈現，成本不成比例。桌機上左側面板是 `position: sticky` 看得到值，但 `@media (max-width: 760px)` 下面板變成 `static` 且排在結果上方，捲下去就完全看不到自己設了什麼——這是這顆按鈕真正的理由。

- **後端零異動。** `search` / `categoryId` / `tags` / `attr.<key>` / `missingAttrs` / `page` / `pageSize` 全部已支援，`pageSize` 上限 200 也已覆蓋 8 頁的還原需求。

## Testing Decisions

- **`catalog.component.spec.ts`**：從網址 query param 初始化篩選（含重複 key 的 tags、`attr.<key>`、`missingAttrs`，以及單值/多值兩種形狀）、篩選變更會反映到網址、「清除全部篩選」的出現條件與清除後送出的查詢。這一層是主要戰場。
- **返回點記憶的單元測試**：重點在「查詢字串與記憶不一致時**不**套用頁數與錨點」。這是唯一會默默給出錯誤結果的分支——套用了錯的頁數不會拋錯、不會空白，只會顯示一份看起來很合理但不對的清單。同時驗還原上限 8 頁與壞掉的記憶被丟棄。
- **`item-detail.component.spec.ts`**：返回按鈕在「有返回點」與「無返回點」兩種情況下的目的地。薄薄一層即可。
- **捲動還原只斷言「對正確的卡片元素呼叫了 `scrollIntoView`」**，不驗真實捲動——TestBed／jsdom 沒有版面，驗不到。真實捲動行為需人工確認，本規格不宣稱有測試涵蓋。
- 不寫「斷言傳進 `CatalogService.search()` 的參數物件長怎樣」的 mock 單元測試，沿用 0001／0002 的立場。

## Out of Scope

- **具名檢視（Saved Views）**：把一組篩選存成叫得出名字的東西、可以有多組。使用者的痛是「回來又要重篩」，不是「我有五組固定篩選要輪流用」。它需要後端 schema、命名 UI 與管理頁，等這次的隱式記憶被證明不夠再說。
- **精選頁的返回點**：`ShowcaseComponent` 用同一個 `app-item-card`，從那裡點進品項再回來一樣會掉回預設的拼貼牆。但那裡只有四個頁籤、切回去成本是一次點選；而且記憶機制若一開始就要泛化成多路由，會逼著提早設計 key 的命名與清除策略。
- **跨裝置或跨 session 的持久化**（localStorage / 後端）。
- **可分享給他人的篩選連結**：網址技術上已可貼給任何人，但收件者需要自己的登入，這不是一個被設計過的分享功能。
- **標記剛編輯過的品項**（例如高亮）：那解的是「我做到哪了」，是另一個問題，答案更可能是排序或一個「最近編輯」篩選。
- 排序控制項——目前列表沒有排序 UI，這次不引入。

## Further Notes

- 篩選條件本身的既有行為見 `docs/specs/0001-catalog-platform-filter.md` 與 `docs/specs/0002-catalog-missing-value-filter.md`；本規格不改動任何一條篩選語意，只改變它們存在哪裡、活多久。
- URL 與記憶的分工理由見 `docs/adr/0010-catalog-url-is-truth-return-point-is-memory.md`。
- `CONTEXT.md` 新增一個詞：**返回點（Return Point）**。刻意不為「一組同時生效的篩選條件」另立正式名稱——那是既有的日常用語，加詞只增加語彙表體積、不增加精確度。另外兩個看似順手的名字都不能用：**檢視／View** 已被 `ShowcaseView` 佔走，**查詢／Query** 已被後端 MediatR 的 `SearchItemsQuery` 家族佔走。
- 檔名編號沿用 `0003`，不修正 `docs/specs/` 既有的兩個 `0002`——改檔名會動到跨檔引用與 commit 的可追溯性，而編號在這個 repo 顯然只是排序前綴、不是識別碼。
- 這份規格同樣是先用 `/mattpocock-skills:grill-with-docs` 做 Socratic 訪談對齊設計，再實作。
