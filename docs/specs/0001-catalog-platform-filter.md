# 收藏頁「全部」視圖的平台篩選

## Problem Statement

使用者在收藏頁想依平台篩選品項，但選「全部」（不選特定品類）時完全看不到任何屬性篩選，包括平台。原因是「平台」是實體遊戲／數位遊戲兩個品類各自宣告的專屬屬性，音樂專輯、電影光碟沒有這個欄位；既有的篩選邏輯刻意「依目前選中的單一品類 schema 動態渲染」，選「全部」時找不到單一品類，直接回傳空清單，不混用不同品類的 schema。

## Solution

在「全部」視圖下，針對「平台」這個屬性開一個明確的白名單例外：只要有品類宣告了 `platform` 欄位，就在「全部」視圖也顯示這個篩選，固定顯示為「平台」（不因品類不同而顯示不一致的標籤）。同時把平台篩選的輸入方式從純文字輸入框升級為限制只能選既有相異值的 combobox（打字即時本地過濾建議清單，不接受清單外的自由文字），解決使用者記不住自己曾經打過的確切平台名稱（大小寫、全形半形等）而篩不到東西的問題。

## User Stories

1. As a collector viewing 全部 in the catalog, I want a 平台 filter to appear, so that I can narrow items by platform without first picking a specific game category.
2. As a collector, I want the 平台 filter label in 全部 view to stay consistent regardless of which game category an item belongs to, so that I'm not confused by two different per-category labels ("平台" vs "平台／商店").
3. As a collector viewing 音樂專輯 or 電影光碟, I want the 平台 filter to NOT appear, so that I'm not shown a filter that can never match anything in that category.
4. As a collector, I want to see only platform values that were actually assigned to items I own, so that I don't pick a value guaranteed to return zero results.
5. As a collector switching from 全部 to 音樂專輯, I want any active 平台 filter to be cleared, so that stale filter state doesn't silently keep affecting results with no visible control for it.
6. As a collector switching back to a game category or 全部, I want to type into the 平台 filter and see autocomplete suggestions drawn from my own existing items, so that I don't have to remember exact spelling/casing.
7. As a collector, I want the 平台 filter to reject values that don't match any of my existing platform values, so that I can't accidentally submit a filter guaranteed to return zero results due to a typo.
8. As a collector selecting a single game category (實體遊戲 or 數位遊戲), I want the platform suggestion list scoped to just that category's items, so that suggestions stay relevant to what I'm currently browsing.
9. As a collector selecting 全部, I want the platform suggestion list to be the union of values across all game categories, so that I can filter across my whole collection at once.
10. As the account owner, I want the platform suggestion list (and the underlying filter) to only ever reflect items I own, so that other users' data is never exposed through this feature.
11. As a developer maintaining this codebase, I want the eligibility rule for "which categories can show the cross-category filter" to be based on declared Category Fields (the Attribute schema), not hardcoded category identity, so that a future category that declares a `platform` field participates automatically without code changes.
12. As a developer, I want the "全部" cross-category filter capability to stay a narrow, explicit whitelist (currently just `platform`) rather than a generic "any field shared by ≥2 categories auto-appears" mechanism, so that two categories that happen to reuse the same field key aren't silently treated as filterable together.
13. As a developer, I want the existing `reload()` pruning logic (which drops attribute-filter keys not in the newly-selected category's searchable fields) to keep working unmodified for the platform field, so that category-switch cleanup logic isn't duplicated.
14. As a developer, I want `searchableFields` to correctly react to category-selection changes, so that a pre-existing reactivity bug doesn't silently undermine either the old per-category filters or this new whitelist behavior.
15. As a reviewer, I want the new backend behavior (owner scoping, category scoping, cross-category union) covered by integration tests at the repository seam, consistent with how `tags` are already tested, so regressions are caught without needing a browser.

## Implementation Decisions

- Domain/Application 層的 Category、Attribute 模型不變。`SearchItemsQuery` / `ItemQuerySpec` / `MongoItemRepository.SearchAsync` 也不用改——它們本來就支援可選的 `CategoryId` 搭配 `Attributes` 字典做精準比對，這次直接沿用。
- 新增 `IItemRepository.ListPlatformsAsync(categoryId?, ct)`，回傳相異的 `attributes.platform` 值，一律先套既有的 owner 範圍限制。`categoryId` 有值時用 `CategoryId` 精準比對；為 null（對應「全部」）時改用「該 attribute key 是否存在」限定範圍，不硬編「哪些品類算遊戲品類」的清單——這仰賴既有的 Attribute 驗證不變式：只有宣告了 `platform` 欄位的品類，品項才可能被寫入這個 key。
- 這個方法是專屬、非通用寫法（比照既有的 `ListTagsAsync`），刻意不做成「任意 attribute key 的相異值」通用方法——理由見 `docs/adr/0006`。
- Application 層新增對應的 Query／Handler，帶一個可選的 CategoryId 字串參數，格式驗證比照既有的 CategoryId 驗證規則。
- API 層新增一個 `GET /items/platforms` 端點，掛在既有的 `/items` 路由群組下（沿用群組的驗證要求），必須註冊在 `/{id}` 之前，理由跟既有的 `/tags` 端點一樣。
- 前端「哪些品類該顯示平台篩選」的判斷邏輯：選「全部」時不是「找單一品類的 schema」，而是「檢查是否有任何一個品類宣告了 key 為 `platform` 的欄位」；有的話合成一個單一的白名單欄位描述（key 固定 `platform`、label 固定「平台」）。選單一品類時行為不變，仍照該品類自己宣告的 Label（例如數位遊戲的「平台／商店」）。
- 這個白名單目前只包含 `platform` 一個 key，是刻意的、寫死的例外，不是通用機制——同樣記在 ADR-0006。
- 平台篩選的輸入元件從純文字輸入框改成瀏覽器原生 combobox（文字輸入框 + 建議清單），清單內容來自新的 `/items/platforms` 端點，在「平台篩選變得可見」或「選定品類改變」時重新抓取。使用者輸入的值只在失焦／確認時（不是每個按鍵）才會提交：若值不在目前抓到的清單裡，直接拒絕並還原成上一個已提交的值，確保永遠不會送出一個保證查無結果的篩選值。
- 過程中一併修掉一個既有 bug：`CatalogComponent` 的 `categoryId` 原本是一般欄位（非 signal），卻在 `searchableFields` 這個 `computed()` 內被讀取。Angular 的 `computed()` 只追蹤 signal 讀取，導致這個 computed 在首次求值後，不會再因為單純切換品類而重新計算——也就是說，切換品類原本並不會正確顯示/隱藏依品類而定的屬性篩選（含這次新增的平台篩選）。修法是把 `categoryId` 改成真正的 `signal<string>('')`，並更新所有讀寫點。
- 曾嘗試但捨棄的做法：用 Angular `effect()` 依據 `searchableFields()`/`categoryId()` 反應式地抓取平台選項。這會在「清空選項」的分支觸發 `NG0600`（effect 內同步寫入其他 signal，Angular 預設禁止）。改為一個明確的同步方法，從既有的「所有會改變篩選狀態的動作最終都會呼叫的地方」（建構子的品類載入回呼、`reload()`）呼叫，跟這個元件既有的「單一入口收斂副作用」寫法一致。

## Testing Decisions

- 好的測試只驗證外部可觀察的行為（查詢回什麼、API 回什麼），不驗證實作細節（例如 combobox 的 DOM 結構、`computed()` 的內部快取機制）。
- 後端測試 seam：延續既有的 `MongoItemRepository` 整合測試（`MongoItemRepositoryTests`，跑在真實 MongoDB 上），跟既有的 `ListTagsAsync`、`SearchAsync` 屬性篩選、owner 隔離測試用同一個 seam。新增三個測試：品類範圍內的相異值、`categoryId` 為 null 時跨品類聯集、絕不回傳其他使用者的值。
- 前端沒有新增自動化測試：這個 repo 目前整個 `web/src` 底下沒有任何 `*.spec.ts`，維持既有慣例不额外引入測試框架。正確性改用嚴格 TypeScript typecheck（`tsc --noEmit`）、production build（`ng build`），以及在本機起 API + MongoDB 後的完整瀏覽器手動走查（跨三個品類建立測試品項、驗證篩選顯示規則、送出／拒絕行為、切換品類時的清除行為）。

## Out of Scope

- 「任何被多個品類共用的欄位都自動出現在全部視圖」的通用機制——刻意不做，理由見 ADR-0006。
- 平台 combobox 的 server-side、逐字 debounce 查詢——目前做法是「品類範圍改變時抓一次完整相異值清單、前端本地過濾」，因為平台值的基數預期很低。
- 既有品項裡不一致的歷史平台值（大小寫、全形半形不同的舊資料）——不做回溯清洗，這次的限制只防止「往後」新增不一致的值。
- 同步／補完流程如何寫入 `platform`（我這一份持有的平台），以及跟它語意不同的「發行平台」——完全沒動。
- 把這個白名單機制套用到 `platform` 以外的其他欄位。

## Further Notes

- 這份規格文件是回溯性質：透過 `/mattpocock-skills:grill-with-docs`（Socratic 訪談 + domain-modeling）先對齊設計，再直接實作完成，而不是先寫規格交給 agent 去做。
- 架構決策的完整理由見 `docs/adr/0006-platform-filter-in-all-view-is-a-hardcoded-whitelist.md`。
- 後續延伸：`docs/specs/0002-catalog-missing-value-filter.md` 在這個平台篩選旁加了「未設定」選項。這份文件維持原狀，不併入那次的決策。
- `CONTEXT.md` 沒有異動——既有的「平台」／「發行平台」定義已經精確涵蓋這次的詞彙，不需要新的 ubiquitous language 詞條。
