# 精選頁：展示模式頁籤化 + hover 中央預覽 + 尺寸調整（前端設計）

- 日期：2026-08-06
- 分支：`feat/showcase-tabs-and-hover-preview`
- ADR：[ADR-0009](../../adr/0009-showcase-tabs-are-filters-not-layout-pickers.md)
- 範圍：**純前端**。沒有任何 API / Domain / Mongo 變更。

## 目標

`/showcase` 與 `/p/:slug` 目前把 Hero、Stats、Collage 三個展示分區疊在列表網格上方，一頁四種視覺語言，過於混亂。改為使用者自選的四頁籤；同時放大成就看板與拼貼牆的尺寸，並為列表加上滑鼠停留時的中央大圖預覽。

三件事互相獨立，唯一的耦合是 hover 預覽掛在「列表」頁籤上，所以排在頁籤化之後。

## 一、頁籤化

| 頁籤 | 資料來源 | 數字 |
|---|---|---|
| 拼貼牆（預設） | 全部精選品項 | 全部精選數 |
| 焦點展品 | `effectiveDisplayMode === 'Hero'` | 篩選後數量 |
| 遊戲成就 | `effectiveDisplayMode === 'Stats'` | 篩選後數量 |
| 列表 | 全部精選品項 | 全部精選數 |

**語意是篩選器不是版型選擇器**，理由見 ADR-0009。

### URL 同步

`?view=collage|hero|stats|list`，無效值退回 `collage`。

**`app.config.ts` 已啟用 `withComponentInputBinding()`**（規劃階段查證），所以 query param 直接綁到元件的 `view` input，不需注入 `ActivatedRoute` 或 `toSignal`。這同時簡化了測試——TestBed 直接用 `fixture.componentRef.setInput('view', 'hero')` 即可，不必架設路由。

寫回 URL 用 `Router.navigate([], { relativeTo, queryParams: { view }, queryParamsHandling: 'merge', replaceUrl: true })`。`replaceUrl` 是為了讓切頁籤不要在瀏覽記錄裡堆出一長串。

### 載入策略

拿掉「載入更多」按鈕，改為自動續抓至 `total`（安全上限 2000 件），**全部載完才渲染頁籤列**。

理由是頁籤的數字與啟用狀態必須是穩定的事實。分批進來時第一批可能一件 Hero 都沒有，焦點頁籤會先被停用、續抓完又啟用，頁籤列閃動，使用者還可能在那一瞬間點到停用的頁籤。載入期間沿用現有的 `loading()` 狀態。

被否決的替代方案：**每個頁籤各自分頁**（後端加 `displayMode` 篩選參數）。精選品項的量級是幾十件，為一個大概永遠不會觸發的規模去動 API 不划算。

### 頁籤元件

新增 `web/src/app/shared/showcase-tabs/showcase-tabs.component.ts`，`/showcase` 與 `/p/:slug` 共用。

- 樣式沿用站上的切角語言（`clip-path` + 選中時 `--mc-cyan`）。
- 行為做完整 WAI-ARIA tabs pattern：`role="tablist"`／`role="tab"`／`aria-selected`／`aria-controls`、左右方向鍵、Home/End、roving `tabindex`。做一半的 tablist（有 role 沒方向鍵）對螢幕閱讀器使用者比完全沒有 role 更糟——它宣告自己是頁籤卻不照頁籤的方式運作。
- 停用的頁籤（`count === 0`）不可被方向鍵選中，也不可點擊。

切走的頁籤以 `@if` 銷毀，輪播計時器隨之停止。這保留了 ADR-0007 實作時「計時器不得卡住 `whenStable()`」的既有保證——`[hidden]` 會讓看不見的分區繼續跑計時器與 Ken Burns 動畫。

## 二、hover 中央預覽

**只存在於 `/showcase` 的列表頁籤。** 新增 `web/src/app/shared/item-preview-overlay/item-preview-overlay.component.ts`。

- 置中浮層 `position: fixed` + scrim `rgb(5 7 13 / 72%)`；浮層本身 `pointer-events: none`，所以滑鼠永遠不會「進入」浮層，不會出現預覽卡住或擋住底下卡片點擊的問題。
- 進場延遲 200ms（滑過整排卡片時不觸發），淡入 150ms。
- **固定 16/10 框 + `object-fit: contain`**，黑邊用同一張圖模糊放大填滿。框不隨圖片比例跳動，直式的公仔／卡牌也完整可見。被否決的替代方案是讓浮層隨圖片比例伸縮——收藏裡直式的公仔卡牌與寬扁的 Steam header（460×215）比例差極遠，浮層會隨滑鼠移動劇烈變形。
- 圖片漸進式：先用列表已快取的 `cardPath`（480px），背景載 `path`（full 1600px）載完替換；延遲期間就開始預載。只顯示主圖，多圖瀏覽留給品項詳細頁。沒有 `images` 的同步品項直接用 `attributes` 的 CDN 網址（`headerUrl` → `coverUrl` → `iconUrl`）。
- 欄位＝Hero 那組**減掉描述**：品類 `showOnCard` 欄位 + 入手日期 + 入手價格 + 存放位置 + 評分。以 `grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr))` 自動換欄壓在圖片下緣，配漸層 scrim。不設欄位數上限——欄位是使用者自己在品類設定裡勾 `showOnCard` 的，程式再砍一次等於讓那個勾選失效。
- 沒有圖片的品項照常出現浮層，圖片區顯示首字母方塊。「有些卡片 hover 有反應、有些沒有」會讓人以為是壞掉了。
- 整套關在 `@media (hover: hover)`，觸控與鍵盤不觸發。浮層 `pointer-events: none` 意味著它本來就不能互動，是純視覺增強；觸控裝置點進詳細頁本來就能看到更完整的內容，長按手勢還會跟系統選單打架。

**ADR-0008 注意**：浮層顯示 `storageLocation`。它只在內部頁使用，但 `toPublicShowcaseDisplayItem` 把 `storageLocation` 寫死 `null` 的防線必須保留，作為公開頁的第二道保險。

## 三、尺寸

| 元件 | 現況 | 改為 |
|---|---|---|
| `stats-section` | `min-height: 16rem` | `min-height: clamp(20rem, 46vw, 40rem)` |
| `hero-section` | `aspect-ratio: 16/10` | **不動** |
| `collage-section` 內部頁 | `slotCount` 4、卡片 11rem | `slotCount` 8、卡片 18rem、`justify-content: center` |
| `collage-section` 公開頁 | `data.collageSlotCount` | **不動**（分享者設定） |

焦點區不放大：它是左圖右欄的並排版型，硬拉高只會讓右邊的欄位面板頂在上面留一大片空；成就區是滿版背景圖，拉高才有沉浸感。兩者不需要等高——頁籤化之後它們永遠不會同時出現在畫面上。

拼貼牆 8 格 × 18rem 在 1920px 容器會排成 4+4 兩排置中。它是預設第一眼，要有足夠的視覺重量撐起門面。

## 影響面

| 檔案 | 影響 |
|---|---|
| `features/showcase/showcase.component.ts` | 模板重寫、載入策略改、加 `view` input |
| `features/showcase/showcase.component.spec.ts` | 3 條測試中 2 條會壞（驗證三分區同時存在、驗證 `[1, 200]` 單次呼叫），必須改寫 |
| `features/public/public-share.component.ts` | 模板重寫、加 `view` input |
| `features/public/public-share.component.spec.ts` | 2 條測試需檢查是否受頁籤化影響 |
| `shared/showcase-sections/{hero,stats,collage}-section.component.ts` | 只改 CSS，邏輯與既有 fakeAsync 計時器測試不動 |
| `shared/showcase-sections/showcase-display-item.ts` | 不動（浮層直接複用 `ShowcaseDisplayItem`） |
| `CONTEXT.md` | 「展示模式」詞條補上頁籤語意 |

## 明確排除

- 任何後端變更（API、Domain、Mongo）。
- 分享者指定公開頁預設頁籤（`ShareLink.DefaultView`）。
- `/catalog` 庫存頁的 hover 預覽——那頁是查找導向的，hover 跳大圖會干擾掃視。
- 觸控裝置的長按預覽。
- 浮層內的多圖瀏覽。
