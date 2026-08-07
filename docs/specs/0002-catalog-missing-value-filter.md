# 收藏頁平台篩選的「未設定」選項

## Problem Statement

使用者想找出「平台欄位還沒填」的品項，但收藏頁的平台篩選（見 `docs/specs/0001-catalog-platform-filter.md`）只能選既有的相異值，選不到「沒有值」。空字串在前後端都被當成「不篩選」而直接略過——`CatalogService.search()` 的 `if (value)` 與 `MongoItemRepository.SearchAsync` 的 `IsNullOrWhiteSpace(value)` 兩層——所以既有機制在設計上就無法表達這個意圖。

## Solution

在平台篩選旁加一個「未設定」checkbox，勾選時改送一個獨立的查詢參數 `missingAttrs=platform`，後端把它翻成「該欄位不存在／為 null／為空字串」的條件，並把結果限縮在有宣告 `platform` 欄位的品類。checkbox 與既有的平台 combobox 互斥。

## User Stories

1. As a collector, I want to tick 未設定 next to the 平台 filter, so that I can find the game items whose platform I never filled in.
2. As a collector, I want 未設定 to also catch items whose platform was written as an empty value by some other path, so that a filter that says "not set" really means it.
3. As a collector viewing 全部, I want 未設定平台 to return only game items, not my music albums and movie discs, so that the filter narrows my collection instead of listing nearly all of it.
4. As a collector, I want 未設定 combined with a selected category to stay narrowed to that category, so that the two controls compose the way every other filter pair does.
5. As a collector, I want ticking 未設定 to clear and disable the platform combobox, so that I can't build a filter that is guaranteed to return zero results.
6. As a collector switching to 音樂專輯, I want the 未設定平台 state to be dropped, so that no invisible filter keeps affecting my results.
7. As a developer, I want the backend mechanism to accept any field key, so that opening this up to another attribute later is a front-end change only.
8. As a developer, I want a mistyped or unknown key to return zero items rather than the whole collection, so that the failure direction is safe.
9. As a reviewer, I want the category-restriction semantics covered end to end against the real system categories, so that the rule can't silently regress into the literal reading.

## Implementation Decisions

- **「未設定」的定義涵蓋三態**：key 不存在、值為 `null`、值為空字串。前端寫入路徑（`DynamicFormComponent.attributes()`）會剔除空字串，但同步流程與歷史資料沒有被那條保證守住，所以不假設資料乾淨。實作上 MongoDB 的 `{field: null}` 已同時匹配 null 與 missing，只需 `Or(Eq(BsonNull), Eq(""))` 兩個條件。
- **獨立的傳輸參數，不用哨符值**：新增 `missingAttrs=platform`（可重複，解析比照既有的 `tags`）。刻意不做成 `attr.platform=__none__` 之類的哨符——那會汙染 attribute 值的值域，理論上會跟真實資料碰撞。`attr.` 家族完全不動。
- **`ItemQuerySpec` 新增兩個獨立欄位**：`MissingAttributes`（要求為空的 key 清單）與 `CategoryIds`（schema 推導出的候選品類護欄）。既有的 `CategoryId`（使用者選定的品類）保留不動，兩者在 Repository 用 AND 疊加。刻意不把兩者合併成單一集合：一個是使用者意圖、一個是系統護欄，合併後在 spec 上會長得一樣，之後分不出來。
- **品類限縮算在 `SearchItemsQueryHandler`**：它是唯一同時看得到 categories 與 spec 的地方，且本來就注入了 `ICategoryRepository`（原為 displayMode lookup 用），零新依賴——順手把那次 `ListAsync` 提前到方法開頭共用，不多打一次 DB。
- **多個 key 同時要求未設定時取交集**（宣告了全部 key 的品類），與篩選條件本身的 AND 語意一致。目前 UI 只能產生單一 key，這條路走不到，但後端要有確定答案。
- **未知 key 不驗證、不回 400**：比照 `attr.` 的既有立場（`ItemEndpoints` 已註明 key 未經 schema 驗證，後果僅止於查無資料）。推導出的品類集合為空 → 回零筆。
- **前端狀態獨立成 `missingAttributes` signal**，與 `attributeFilters` 分開——一個選值、一個選「沒有值」。`reload()` 用同一個 `allowed` key 集合同時剪枝兩者，所以切換到沒有平台欄位的品類時，勾選會跟平台值一起被清掉（沿用 0001 的 User Story 5 規則，不重複實作清除邏輯）。
- **互斥由前端強制**：勾選時把該欄位的值設為空字串（`reload()` 會連帶把 `platformDraft` 收斂成空），並 `disabled` combobox；取消勾選不還原舊值。這跟既有的 `commitPlatformFilter()`「不讓使用者送出保證零結果的篩選」同一立場。
- **DOM 結構**：`<label>` 不可巢狀，所以平台那一格從 `<label>` 改成 `<div class="catalog__filter">`，內含「選值」與「選沒有值」兩個並列的 label。其他欄位的結構不變。
- **後端通用、前端窄**：後端任意 key 都吃；前端只對 `platform` 渲染 checkbox。未來擴充只需改前端條件。這與 ADR-0006 的跨品類白名單是正交維度，理由見該 ADR 的補充段落。

## Testing Decisions

- **`MongoItemRepositoryTests`（真 Mongo）**：驗三態（key 不存在／null／空字串都要中，有值的不中）與 `CategoryIds` 的兩個行為（限縮生效、空清單回零筆）。三態必須在這一層驗，因為寫入路徑會把 `null` 與 `""` 剔掉，端到端造不出這種資料。
- **`CatalogEndpointsTests`（真 HTTP + 真 Mongo + 真系統品類）**：驗「未設定平台」只涵蓋宣告了 platform 的品類（音樂專輯不入列）、與 `categoryId` 的組合、以及未知 key 回零筆。這是唯一能驗到品類限縮語意的層級。
- **`catalog.component.spec.ts`**：驗勾選後送出的參數帶 `missingAttributes: ['platform']` 且平台值已清空（互斥），以及切到沒有平台欄位的品類後該狀態消失。
- 刻意不寫「斷言傳進 `SearchAsync` 的 spec 長怎樣」的 Moq 單元測試——那是驗實作細節，與 0001 立下的測試原則相衝。

## Out of Scope

- 把「未設定」checkbox 開放給 `platform` 以外的欄位——後端已支援，前端刻意先不開。
- 反向的「已設定」篩選。
- 歷史資料中不一致的空值（`null` vs `""`）的回溯清洗——這次只讓查詢涵蓋它們，不改寫資料。
- `missingAttrs` 的 schema 驗證與錯誤回報。

## Further Notes

- 延續 `docs/specs/0001-catalog-platform-filter.md`；0001 本身不改寫，保持它作為那一次決策的紀錄。
- 架構決策的理由見 `docs/adr/0006-platform-filter-in-all-view-is-a-hardcoded-whitelist.md` 的補充段落。
- CONTEXT.md 沒有異動——這次沒有新的 ubiquitous language 詞彙。
- 這份規格同樣是先用 `/mattpocock-skills:grill-me` 做 Socratic 訪談對齊設計，再實作。
