# 精選頁展示模式頁籤化 + hover 中央預覽 + 尺寸調整（實作計畫）

> **For agentic workers**：執行前必須套用 `superpowers:test-driven-development`。每個 Task 嚴格照 Step 1→6 走，**Step 2（確認測試以正確理由失敗）不可省略**——沒看過紅燈就不知道這條測試有沒有效。

**Goal**：把 `/showcase` 與 `/p/:slug` 的三個展示分區從「疊加」改成「四頁籤」，放大成就看板與拼貼牆尺寸，並為內部頁列表加上 hover 中央大圖預覽。

**Architecture**：純前端。Angular 20.3 standalone components + signals，無 NgModule、無 SCSS 檔（一律 inline `styles`）。沒有任何 API / Domain / Mongo 變更。

**Tech Stack**：Angular 20.3.0、TypeScript、Karma + Jasmine、zone.js 0.15。

- 設計文件：`docs/superpowers/specs/2026-08-06-showcase-tabs-and-hover-preview-design.md`
- ADR：`docs/adr/0009-showcase-tabs-are-filters-not-layout-pickers.md`
- 前置計畫：`docs/superpowers/plans/2026-08-06-showcase-display-mode.md`（本計畫建立在它的產物上）

---

## 執行前必讀

### 環境

- 工作目錄：`F:\VibeCode\MyCollection`
- 前端目錄：`web/`
- **分支：`feat/showcase-tabs-and-hover-preview`（不是 master，已建立）**
- 全部測試：`cd web && npm test -- --watch=false --browsers=ChromeHeadless`
- 單檔測試：同上加 `--include=src/app/<path>/<name>.spec.ts`
- Build：`cd web && npm run build`

### 基準線

**前端 179 passed（`TOTAL: 179 SUCCESS`），實測於 2026-08-06，master 乾淨時。**

任何時候數字低於 179 就是弄壞了東西——除了 Task 3 與 Task 4 會**改寫**既有測試，那兩處的預期數字在各自 Task 內註明。後端未受本計畫影響，不需執行 `dotnet test`。

### 絕對不要碰的檔案

開工時工作樹是乾淨的。若執行期間出現無關的未提交變更（例如 Angular CLI 自動寫入的 analytics UUID、`.angular/` 快取），一律不要納入。

**每個 Task 用明確路徑 `git add`，禁止 `git add .`。**

### 慣例

- 註解與 commit message 用**繁體中文**；commit 結尾附 `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`。
- Angular：standalone、`input()` / `output()` / `signal()` / `computed()` / `effect()`、控制流用 `@if` / `@for`，樣式一律寫在 `@Component` 的 inline `styles`。
- 測試選擇器用 `data-*` 屬性（沿用 `data-hero-section`、`data-item-card` 既有慣例），不要靠 CSS class 選。
- 計時器相關測試用 `fakeAsync` + `tick()`；元件內計時器一律 `NgZone.runOutsideAngular()` 建立、`effect` 的 `onCleanup` 清除（沿用 `hero-section.component.ts` 的既有作法）。
- TDD 順序：紅 → 綠 → 重構。一個 Task 一個 commit。

### 規劃階段已查證的事實

| 引用 | 存在？ | 證據 | 影響 |
|---|---|---|---|
| `withComponentInputBinding()` | ✅ 已啟用 | `web/src/app/app.config.ts` 第 13 行 | query param 直接綁 `view` input，不需 `ActivatedRoute`；測試用 `componentRef.setInput()` |
| `@angular/core/rxjs-interop` | ✅ 存在 | `web/node_modules/@angular/core/rxjs-interop/index.d.ts` | 本計畫用不到（因上一列），保留備查 |
| `ItemImageDto.path` / `.cardPath` | ✅ 存在 | `web/src/app/core/models.ts` 第 38-45 行 | full=1600px、card=480px、thumb=160px（`IImageProcessor.cs` 第 11 行） |
| `PublicItemDto` 無 `path` | ✅ 確認 | `models.ts` 第 99 行只有 `cardPath`/`thumbPath` | 公開頁拿不到原圖——但浮層不上公開頁，不受影響 |
| `ShowcaseDisplayItem` | ✅ 存在 | `shared/showcase-sections/showcase-display-item.ts` 第 15-27 行 | 浮層直接複用，不新增介面 |
| `showcase-tabs/` 目錄 | ✅ **尚未存在** | `ls web/src/app/shared/` 只有 6 個既有目錄 | Task 1 建立 |
| `item-preview-overlay/` 目錄 | ✅ **尚未存在** | 同上 | Task 6 建立 |
| `docs/adr/0009-*.md` | ⚠️ 本計畫已建立 | Task 0 一併 commit | — |
| `showcase.component.spec.ts` 3 條測試 | ✅ 確認 | 第 9/30/55 行 | 第 30、55 行兩條會壞，Task 2、3 改寫 |
| `public-share.component.spec.ts` 2 條測試 | ✅ 確認 | `grep -c "it("` = 2 | Task 4 檢查並改寫 |
| `CONTEXT.md` 有「展示模式」詞條 | ✅ 存在 | 第 36-38 行 | Task 0 補上頁籤語意 |

---

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `web/src/app/shared/showcase-tabs/showcase-tabs.component.ts` | 頁籤列。WAI-ARIA tablist、停用 0 件頁籤、切角樣式。內部頁與公開頁共用 |
| `web/src/app/shared/showcase-tabs/showcase-tabs.component.spec.ts` | 上者的測試 |
| `web/src/app/shared/showcase-tabs/showcase-view.ts` | `ShowcaseView` 型別、`SHOWCASE_VIEWS` 常數、`parseShowcaseView()` |
| `web/src/app/shared/item-preview-overlay/item-preview-overlay.component.ts` | hover 中央浮層 |
| `web/src/app/shared/item-preview-overlay/item-preview-overlay.component.spec.ts` | 上者的測試 |
| `docs/adr/0009-showcase-tabs-are-filters-not-layout-pickers.md` | ADR（已寫好） |
| `docs/superpowers/specs/2026-08-06-showcase-tabs-and-hover-preview-design.md` | 設計文件（已寫好） |

### 修改

| 檔案 | 改動 |
|---|---|
| `web/src/app/features/showcase/showcase.component.ts` | 加 `view` input、自動續抓、模板改頁籤、接上浮層 |
| `web/src/app/features/showcase/showcase.component.spec.ts` | 改寫 2 條、新增數條 |
| `web/src/app/features/public/public-share.component.ts` | 加 `view` input、模板改頁籤 |
| `web/src/app/features/public/public-share.component.spec.ts` | 依頁籤化調整 |
| `web/src/app/shared/showcase-sections/stats-section.component.ts` | `min-height` 改 clamp |
| `web/src/app/shared/showcase-sections/collage-section.component.ts` | 卡片 11rem→18rem、`justify-content: center` |
| `CONTEXT.md` | 「展示模式」詞條補頁籤語意 |

### Task 相依順序

```
Task 0 (docs)
   │
   ├──────────────┬─────────────────┬──────────────┐
   ▼              ▼                 ▼              ▼
Task 1         Task 2            Task 5        Task 6
(tabs 元件)   (自動續抓)         (尺寸)        (浮層元件)
   │              │                 獨立           │
   └──────┬───────┘              可任何時候        │
          ▼                        插入            │
       Task 3 ──────────────────────────────────────┤
     (showcase 頁籤化)                              ▼
          │                                     Task 7
          ▼                                (列表接上浮層)
       Task 4
    (public 頁籤化)
```

- **Task 1、2、5、6 彼此獨立**，可並行。
- Task 3 需要 Task 1 與 Task 2 都完成。
- Task 4 需要 Task 1（實務上排在 Task 3 之後，兩者模板結構相同，可直接沿用）。
- Task 7 需要 Task 3 與 Task 6。

**Usage-safe checkpoint**：Task 4 結束（頁籤化全部完成、測試全綠）是最自然的斷點；Task 5 結束是第二個。

### Commit 顆數說明

grilling 定案時說「四個 commit」，指的是四個**邏輯交付**。本計畫依 skill 慣例拆成一個 Task 一個 commit，共 **8 個 commit**，對應關係：

| 邏輯交付 | Task | Commit 前綴 |
|---|---|---|
| ① 文件 | Task 0 | `docs:` |
| ② 頁籤化 | Task 1–4 | `feat(web):` ×4 |
| ③ 尺寸 | Task 5 | `style(web):` |
| ④ hover 浮層 | Task 6–7 | `feat(web):` ×2 |

若偏好維持 4 顆，執行完各組後 squash 即可——**但這是使用者的決定，執行時先照 8 顆做，不要自作主張 squash**。

---

## Task 0：文件與詞彙表

**Files:** Create: `docs/adr/0009-showcase-tabs-are-filters-not-layout-pickers.md`（已寫）、`docs/superpowers/specs/2026-08-06-showcase-tabs-and-hover-preview-design.md`（已寫）、本計畫檔 / Modify: `docs/adr/0007-showcase-display-mode-and-collage-is-unfiltered.md`、`CONTEXT.md`

先落地文件，讓後續 Task 有可引用的權威來源。ADR-0007 要加一行指向 0009，否則往後讀 0007 的人不會知道呈現方式已經變了。

- [ ] Step 1: 在 ADR-0007 末尾加「後續」一節，指向 ADR-0009，說明 per-item 語意未變、只有呈現方式改為頁籤
- [ ] Step 2: `CONTEXT.md`「展示模式」詞條補一句：精選頁以頁籤呈現，頁籤是**篩選器**（只顯示該模式的品項），不是版型選擇器
- [ ] Step 3: Commit
```
git add docs/adr/0007-showcase-display-mode-and-collage-is-unfiltered.md docs/adr/0009-showcase-tabs-are-filters-not-layout-pickers.md docs/superpowers/specs/2026-08-06-showcase-tabs-and-hover-preview-design.md docs/superpowers/plans/2026-08-06-showcase-tabs-and-hover-preview.md CONTEXT.md
```
commit message：
```
docs: record the showcase tabs ADR, spec, and implementation plan

- ADR-0009 記錄「頁籤是篩選器不是版型選擇器」與「公開頁與內部頁
  行為一致」兩個決定，以及各自被否決的替代方案。
- ADR-0007 加上指向 0009 的後續連結；per-item displayMode 語意未變。
- CONTEXT.md 的「展示模式」詞條補上頁籤語意。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

---

## Task 1：ShowcaseTabsComponent

**Files:** Create: `web/src/app/shared/showcase-tabs/showcase-view.ts`、`web/src/app/shared/showcase-tabs/showcase-tabs.component.ts`、`web/src/app/shared/showcase-tabs/showcase-tabs.component.spec.ts`

這個元件是整個改動的地基，兩個頁面共用。最容易錯的地方是**停用頁籤與鍵盤導覽的互動**：方向鍵必須跳過停用的頁籤，否則使用者會被卡在一個按了沒反應的頁籤上；roving tabindex 也必須只有作用中的那顆是 `0`，否則 Tab 鍵會逐一走過四顆按鈕，違反 tablist 的預期行為。

- [ ] Step 1: 寫失敗測試 `showcase-tabs.component.spec.ts`

```ts
import { TestBed } from '@angular/core/testing';
import { ShowcaseTabsComponent, ShowcaseTab } from './showcase-tabs.component';

const tabs: ShowcaseTab[] = [
  { id: 'collage', label: '拼貼牆', count: 5 },
  { id: 'hero', label: '焦點展品', count: 0 },
  { id: 'stats', label: '遊戲成就', count: 2 },
  { id: 'list', label: '列表', count: 5 },
];

async function createFixture(active: ShowcaseTab['id'] = 'collage') {
  await TestBed.configureTestingModule({ imports: [ShowcaseTabsComponent] }).compileComponents();

  const fixture = TestBed.createComponent(ShowcaseTabsComponent);
  fixture.componentRef.setInput('tabs', tabs);
  fixture.componentRef.setInput('active', active);
  fixture.detectChanges();

  return fixture;
}

function buttons(fixture: Awaited<ReturnType<typeof createFixture>>): HTMLButtonElement[] {
  return Array.from(fixture.nativeElement.querySelectorAll('[role="tab"]'));
}

describe('ShowcaseTabsComponent', () => {
  it('renders a tablist with one tab per entry and marks the active one', async () => {
    const fixture = await createFixture('stats');
    const all = buttons(fixture);

    expect(fixture.nativeElement.querySelector('[role="tablist"]')).toBeTruthy();
    expect(all.length).toBe(4);
    expect(all.map((b) => b.getAttribute('aria-selected')))
      .toEqual(['false', 'false', 'true', 'false']);
  });

  it('shows each tab count and disables the ones with no items', async () => {
    const fixture = await createFixture();
    const all = buttons(fixture);

    expect(all[0].textContent).toContain('5');
    expect(all[1].textContent).toContain('0');
    expect(all[1].disabled).toBeTrue();
    expect(all[0].disabled).toBeFalse();
  });

  it('keeps a roving tabindex so Tab enters the tablist only once', async () => {
    const fixture = await createFixture('stats');

    expect(buttons(fixture).map((b) => b.getAttribute('tabindex')))
      .toEqual(['-1', '-1', '0', '-1']);
  });

  it('emits the next enabled tab on ArrowRight, skipping disabled ones', async () => {
    const fixture = await createFixture('collage');
    const emitted: string[] = [];
    fixture.componentInstance.activeChange.subscribe((v) => emitted.push(v));

    // collage → (hero 停用，跳過) → stats
    buttons(fixture)[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    fixture.detectChanges();

    expect(emitted).toEqual(['stats']);
  });

  it('wraps around on ArrowLeft from the first tab', async () => {
    const fixture = await createFixture('collage');
    const emitted: string[] = [];
    fixture.componentInstance.activeChange.subscribe((v) => emitted.push(v));

    buttons(fixture)[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
    fixture.detectChanges();

    expect(emitted).toEqual(['list']);
  });

  it('jumps to the first and last enabled tab with Home and End', async () => {
    const fixture = await createFixture('stats');
    const emitted: string[] = [];
    fixture.componentInstance.activeChange.subscribe((v) => emitted.push(v));

    buttons(fixture)[2].dispatchEvent(new KeyboardEvent('keydown', { key: 'Home' }));
    buttons(fixture)[2].dispatchEvent(new KeyboardEvent('keydown', { key: 'End' }));
    fixture.detectChanges();

    expect(emitted).toEqual(['collage', 'list']);
  });

  it('emits on click but never for a disabled tab', async () => {
    const fixture = await createFixture();
    const emitted: string[] = [];
    fixture.componentInstance.activeChange.subscribe((v) => emitted.push(v));

    buttons(fixture)[3].click();
    buttons(fixture)[1].click(); // 停用，不該發出
    fixture.detectChanges();

    expect(emitted).toEqual(['list']);
  });
});
```

- [ ] Step 2: 跑測試確認失敗
  Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/shared/showcase-tabs/showcase-tabs.component.spec.ts`
  **確認失敗原因是「找不到模組 `./showcase-tabs.component`」**，不是語法錯字。
- [ ] Step 3: 最小實作
  - `showcase-view.ts`：`export type ShowcaseView = 'collage' | 'hero' | 'stats' | 'list';`、`export const SHOWCASE_VIEWS: readonly ShowcaseView[]`、`export function parseShowcaseView(value: string | null | undefined): ShowcaseView`（無效值回 `'collage'`）
  - `showcase-tabs.component.ts`：`ShowcaseTab { id: ShowcaseView; label: string; count: number }`；`tabs = input<ShowcaseTab[]>([])`、`active = input.required<ShowcaseView>()`、`activeChange = output<ShowcaseView>()`；`onKeydown` 處理 ArrowLeft/ArrowRight/Home/End，只在 `count > 0` 的頁籤間移動並環繞；切角樣式 `clip-path`，選中態用 `--mc-cyan`
- [ ] Step 4: 跑單檔測試　Expected: `TOTAL: 7 SUCCESS`
- [ ] Step 5: 跑全部測試　Expected: `TOTAL: 186 SUCCESS`
- [ ] Step 6: Commit
```
git add web/src/app/shared/showcase-tabs/
```
```
feat(web): add a shared WAI-ARIA tablist for showcase display modes

方向鍵與 Home/End 一律跳過 count 為 0 的停用頁籤，roving tabindex
讓 Tab 鍵只進入 tablist 一次。做一半的 tablist（有 role 沒方向鍵）
對螢幕閱讀器比完全沒有 role 更糟，所以一次做完整。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

---

## Task 2：精選牆一次載滿

**Files:** Modify: `web/src/app/features/showcase/showcase.component.ts`、`web/src/app/features/showcase/showcase.component.spec.ts`

頁籤的數字與啟用狀態必須是穩定的事實（ADR-0009），所以要在渲染頁籤列之前把資料抓完。這個 Task 只改載入邏輯，**模板暫時不動**——先讓載入策略獨立通過測試，Task 3 再換模板，兩者混在一起會很難判斷是哪邊壞的。

最容易錯的地方是續抓的終止條件：若後端回傳的 `total` 大於實際可取得的資料量，`items().length < total()` 會永遠成立而無限發請求。必須同時以「這一頁沒拿到任何東西」與「達到 2000 件上限」兩個條件保底。

- [ ] Step 1: 寫失敗測試——改寫既有的 `loads the first page with a size large enough...`（第 30 行），換成兩條新測試

```ts
  it('keeps fetching until every showcased item is loaded', async () => {
    const calls: unknown[][] = [];
    const page = (page: number, count: number, total: number) => ({
      items: Array.from({ length: count }, (_, i) => item({ id: `p${page}-${i}` })),
      total,
      page,
      pageSize: 200,
    });

    await TestBed.configureTestingModule({
      imports: [ShowcaseComponent],
      providers: [
        provideRouter([]),
        {
          provide: CatalogService,
          useValue: {
            showcase: (...args: unknown[]) => {
              calls.push(args);
              return of(calls.length === 1 ? page(1, 200, 250) : page(2, 50, 250));
            },
          },
        },
        { provide: CategoryService, useValue: { list: () => of([]) } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ShowcaseComponent);
    fixture.detectChanges();

    expect(calls).toEqual([[1, 200], [2, 200]]);
    expect(fixture.componentInstance.items().length).toBe(250);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('stops fetching when a page comes back empty even if total disagrees', async () => {
    const calls: unknown[][] = [];

    await TestBed.configureTestingModule({
      imports: [ShowcaseComponent],
      providers: [
        provideRouter([]),
        {
          provide: CatalogService,
          useValue: {
            showcase: (...args: unknown[]) => {
              calls.push(args);
              // total 謊報 9999，但第二頁回空——不能無限抓下去
              return of({ items: calls.length === 1 ? [item({ id: 'a' })] : [], total: 9999, page: calls.length, pageSize: 200 });
            },
          },
        },
        { provide: CategoryService, useValue: { list: () => of([]) } },
      ],
    }).compileComponents();

    TestBed.createComponent(ShowcaseComponent).detectChanges();

    expect(calls.length).toBe(2);
  });
```

（`item()` helper 從既有第 3 條測試提取到 describe 頂層共用。）

- [ ] Step 2: 跑測試確認失敗
  Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/features/showcase/showcase.component.spec.ts`
  **確認第一條失敗在 `expect(calls).toEqual([[1, 200], [2, 200]])` 只拿到 `[[1, 200]]`**，也就是「沒有續抓」，而不是別的錯。
- [ ] Step 3: 最小實作——`load()` 改成遞迴續抓：拿到結果後若 `items().length < total` 且該頁非空且 `items().length < 2000`，`page += 1` 再抓一次；全部抓完才 `loading.set(false)`。移除 `loadMore()` 與模板上的「載入更多」按鈕。
- [ ] Step 4: 跑單檔測試　Expected: `TOTAL: 4 SUCCESS`
- [ ] Step 5: 跑全部測試　Expected: `TOTAL: 187 SUCCESS`
- [ ] Step 6: Commit
```
git add web/src/app/features/showcase/showcase.component.ts web/src/app/features/showcase/showcase.component.spec.ts
```
```
feat(web): load every showcased item before rendering

頁籤的數字與啟用狀態必須是穩定的事實（ADR-0009），分批載入會讓
焦點頁籤先顯示 0 被停用、續抓完又啟用。改為自動續抓至 total，
並以「空頁」與 2000 件上限雙重保底，避免 total 謊報時無限請求。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

---

## Task 3：`/showcase` 頁籤化

**Files:** Modify: `web/src/app/features/showcase/showcase.component.ts`、`web/src/app/features/showcase/showcase.component.spec.ts`

既有第 3 條測試（`shows the hero and stats sections only for items in the matching display mode`，第 55 行）**必定會壞**——頁籤化後預設落在拼貼牆，Hero 分區不再與列表同時存在。它要被拆成「頁籤篩選正確」與「切換頁籤渲染對應分區」兩條。

容易錯的地方：`view` input 來自 query param 綁定（`withComponentInputBinding()` 已啟用），型別是 `string | undefined`，**必須經過 `parseShowcaseView()` 正規化**，否則 `?view=xxx` 會讓四個分區全部消失。

- [ ] Step 1: 寫失敗測試（改寫第 3 條，新增以下）

```ts
  it('defaults to the collage tab and renders only that section', async () => {
    const fixture = await createShowcase([
      item({ id: 'h', effectiveDisplayMode: 'Hero' }),
      item({ id: 'l', effectiveDisplayMode: 'List' }),
    ]);

    expect(fixture.nativeElement.querySelector('[data-collage-section]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-hero-section]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-stats-section]')).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('[data-item-card]').length).toBe(0);
  });

  it('renders the hero section when the view input selects it', async () => {
    const fixture = await createShowcase([
      item({ id: 'h', effectiveDisplayMode: 'Hero' }),
      item({ id: 'l', effectiveDisplayMode: 'List' }),
    ]);
    fixture.componentRef.setInput('view', 'hero');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-hero-section]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-collage-section]')).toBeNull();
  });

  it('falls back to the collage tab for an unknown view value', async () => {
    const fixture = await createShowcase([item({ id: 'a', effectiveDisplayMode: 'List' })]);
    fixture.componentRef.setInput('view', 'not-a-view');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-collage-section]')).toBeTruthy();
  });

  it('counts hero and stats tabs by display mode and the others by total', async () => {
    const fixture = await createShowcase([
      item({ id: 'h1', effectiveDisplayMode: 'Hero' }),
      item({ id: 's1', effectiveDisplayMode: 'Stats' }),
      item({ id: 's2', effectiveDisplayMode: 'Stats' }),
      item({ id: 'l1', effectiveDisplayMode: 'List' }),
    ]);

    expect(fixture.componentInstance.tabs().map((t) => [t.id, t.count]))
      .toEqual([['collage', 4], ['hero', 1], ['stats', 2], ['list', 4]]);
  });

  it('renders every showcased item in the list tab', async () => {
    const fixture = await createShowcase([
      item({ id: 'h', effectiveDisplayMode: 'Hero' }),
      item({ id: 'l', effectiveDisplayMode: 'List' }),
    ]);
    fixture.componentRef.setInput('view', 'list');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('[data-item-card]').length).toBe(2);
  });

  it('hides the tablist until every item has loaded', async () => {
    // showcase() 回傳一個尚未 emit 的 Subject，模擬載入中
    // 期望：[role="tablist"] 為 null、「載入中」文字存在
  });
```

（`createShowcase(items)` helper 收斂重複的 TestBed 設定。最後一條的 Subject 版本在實作時補完，重點是**驗證載入中不渲染頁籤列**。）

- [ ] Step 2: 跑測試確認失敗　**確認失敗是「預設仍渲染 hero/stats 分區」與「`tabs()` 不存在」**，不是 TestBed 設定寫錯
- [ ] Step 3: 最小實作
  - `readonly view = input<string>()`；`readonly activeView = computed(() => parseShowcaseView(this.view()))`
  - `readonly tabs = computed<ShowcaseTab[]>(...)`，依上表計數
  - 模板：`@if (!loading())` 才渲染 `<app-showcase-tabs [tabs]="tabs()" [active]="activeView()" (activeChange)="selectView($event)" />`，底下用 `@switch (activeView())` 分別渲染四個分區（`@if` 語意，切走即銷毀）
  - `selectView(view)` 用 `Router.navigate([], { relativeTo, queryParams: { view }, queryParamsHandling: 'merge', replaceUrl: true })`
  - 空狀態（`items().length === 0`）維持現狀，不渲染頁籤列
- [ ] Step 4: 跑單檔測試　Expected: `TOTAL: 9 SUCCESS`
- [ ] Step 5: 跑全部測試　Expected: `TOTAL: 192 SUCCESS`
- [ ] Step 6: Commit
```
git add web/src/app/features/showcase/showcase.component.ts web/src/app/features/showcase/showcase.component.spec.ts
```
```
feat(web): split the showcase page into display-mode tabs

四個頁籤（拼貼牆/焦點展品/遊戲成就/列表）取代原本疊在一起的三個
分區。頁籤是篩選器不是版型選擇器——per-item displayMode 語意不變
（ADR-0009）。狀態走 ?view= query param，靠既有的
withComponentInputBinding() 直接綁到 input，無效值退回拼貼牆。
切走的頁籤以 @if 銷毀，輪播計時器隨之停止。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

---

## Task 4：`/p/:slug` 頁籤化

**Files:** Modify: `web/src/app/features/public/public-share.component.ts`、`web/src/app/features/public/public-share.component.spec.ts`

結構與 Task 3 相同，差別是資料來自 `share()`、`slotCount` 用分享者設定的 `data.collageSlotCount`、列表頁籤是既有的 `.public__wall`（沒有 hover 浮層）。

**ADR-0008 的防線要小心處理**：既有測試 `never renders a storage location...`（第 38-84 行）第 81 行斷言 `[data-hero-section]` 存在，頁籤化後預設落在拼貼牆，**這行會直接紅燈**。

危險的修法是為了消紅燈直接刪掉第 81 行——那樣第 82 行的 `[data-hero-storage-location]` 斷言就變成假通過（DOM 裡根本沒有 Hero 分區，當然找不到存放位置），這條安全性回歸測試就白測了。**正確修法是 `setInput('view', 'hero')` 後再斷言**，讓第 81 行繼續有效地證明「Hero 分區確實被渲染出來了，而它裡面沒有存放位置」。

- [ ] Step 1: 寫失敗測試——比照 Task 3 的四條（預設拼貼牆、`view` 切換、無效值退回、列表渲染），**並修正 storageLocation 回歸測試**：`setInput('view', 'hero')` 後再斷言 `[data-hero-storage-location]` 為 null
- [ ] Step 2: 跑測試確認失敗
  Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/features/public/public-share.component.spec.ts`
  **特別確認 storageLocation 那條在改寫後仍是紅的**（因為 hero 分區還沒被頁籤條件包起來，或斷言路徑改了），確定它真的在測東西
- [ ] Step 3: 最小實作——比照 Task 3
- [ ] Step 4: 跑單檔測試　Expected: `TOTAL: 6 SUCCESS`
- [ ] Step 5: 跑全部測試　Expected: `TOTAL: 196 SUCCESS`
- [ ] Step 6: Commit
```
git add web/src/app/features/public/public-share.component.ts web/src/app/features/public/public-share.component.spec.ts
```
```
feat(web): use the same display-mode tabs on the public share page

公開頁與內部頁行為一致（ADR-0009）：共用同一個頁籤元件與同一組
?view= query param，預設同樣是拼貼牆。分享者指定預設頁籤的能力
不做——ShareLink 加 DefaultView 是為假想需求先付 Domain 層代價。

storageLocation 的 DOM 回歸測試改為先切到焦點頁籤再斷言，否則
頁籤化後 Hero 分區預設不存在，那條測試會變成假通過。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

---

## Task 4b：空頁籤自動退回拼貼牆（執行中追加）

**Files:** Modify: `web/src/app/features/showcase/showcase.component.ts`、`web/src/app/features/public/public-share.component.ts` 及兩者的 spec

規劃時沒想到的邊界：`?view=` 指到一個**合法但沒有品項**的頁籤（書籤存了 `?view=hero`，之後所有焦點品項都被取消），會停在一個停用又空白的頁籤上。`parseShowcaseView()` 只擋得掉語法上無效的值，擋不掉這個。

兩頁的 `activeView` computed 一併檢查該頁籤的 `count`，為 0 就退回 `DEFAULT_SHOWCASE_VIEW`。

- [x] Step 1–6 完成，commit `2306562`

---

**← Usage-safe checkpoint：頁籤化完整交付，可收工。**

## 執行紀錄（Task 0–4b，2026-08-06）

**committed code 才是權威，以下數字取代計畫前段的預測值。**

| Task | Commit | 全套測試 |
|---|---|---|
| 0 文件 | `3ad2cb7` | — |
| 1 ShowcaseTabsComponent | `20c2376` | 186 |
| 2 一次載滿 | `c04e76e` | 187 |
| 3 `/showcase` 頁籤化 | `3c50dd9` | 192 |
| 4 `/p/:slug` 頁籤化 | `fab7afc` | 195 |
| 4b 空頁籤退回 | `2306562` | **197** |

`npm run build` 乾淨（2.231s，0 errors／0 warnings）。

### 偏離計畫之處與原因

1. **公開頁 spec 是 5 條不是預測的 6 條**，所以 Task 4 的全套是 195 而非 196。加上 Task 4b 的 2 條後為 197。**下游 Task 5／6／7 的預測數字要各自加 2**（Task 5 → 198、Task 6 → 203、Task 7 → 206）。
2. **公開頁測試必須加 `provideRouter([])`**。計畫沒寫到這點：元件現在注入 `Router` 來寫回 `?view=`，而既有測試只提供 `ActivatedRoute` mock，會 `NullInjectorError`。順序是 `provideRouter([])` 在前、`ActivatedRoute` mock 在後（後者蓋掉前者的 `ActivatedRoute`）。
3. **`@switch` 取代計畫寫的 `@if`**。四個互斥的分支用 `@switch` 比四個獨立 `@if` 清楚，銷毀語意相同（切走的分支整個拆掉，計時器隨之停止）。
4. **公開頁的頁籤列包在 `@if (data.items.length)` 裡**。0 件時四個頁籤全部停用很難看，直接不渲染頁籤列。內部頁靠既有的空狀態分支達到同樣效果。
5. **Task 4b 是規劃時沒有的**（見上）。

## 執行紀錄（Task 5–7，2026-08-06）

| Task | Commit | 全套測試 |
|---|---|---|
| 5 尺寸 | `612c046` | 198 |
| 6 浮層元件 | `9f69eea` | 203 |
| 7 列表接上浮層 | `8a6f2e9` | **206** |

`npm run build` 乾淨（0 warnings / 0 errors）。

### 偏離計畫之處與原因

6. **Task 5 的測試碼在計畫裡就是錯的**。原本寫「給 10 件、驗 8 格」，但 `CollageSectionComponent` 只有在 `pool.length > slotCount` 時才啟動輪播——10 > 8 會起一個**真實的 `setInterval(4000)`**，而那條測試不是 `fakeAsync`。結果整個 karma 永遠跑不完（症狀是同一條指令從 60 秒變成無限等待）。改成**剛好 8 件**：`slotCount` 為 4 時只渲染 4 格，一樣驗得出 slotCount，但不跨過輪播門檻。
   **教訓**：skill §4 要求「檢查測試碼的競態」，這條漏掉了。往後凡是餵資料給有計時器的元件，都要先確認有沒有跨過啟動門檻。
7. **浮層的 `fullImageUrl` 由呼叫端提供**，不是元件自己算。元件只有 `ShowcaseDisplayItem`（其 `imageUrl` 已是 card 圖），拿不到 `ItemDto.images[].path`。由 `ShowcaseComponent` 預載完再傳進來，元件保持純呈現。
8. **預載完成要比對 `pendingId`**。圖片載入是非同步的，回來時游標可能早就移到別張卡片，不比對就會把前一張的原圖套到現在這張上。計畫沒提到這點。
9. **`DatePipe` 不能放在 `imports`**（NG8113 warning）。浮層在類別裡用 `new DatePipe('en-US')` 格式化日期，模板沒有用到 pipe 語法，列在 `imports` 會讓 build 出現「All imports are unused」警告。

### 環境陷阱（會再遇到）

被 `TaskStop` 中止的 karma 會留下 node 行程佔住 port，**下一次 `npm test` 會無限掛住而不是報錯**。症狀是同一條指令從 60 秒變成跑不完。處理：

```powershell
Get-Process node | Select-Object Id,StartTime   # 找出開始時間對得上的那幾個
Stop-Process -Id <ids> -Force
```

**這個陷阱在 Task 5–7 又出現了三次**，而且不只 `TaskStop` 會造成——**單檔測試（`--include=`）正常結束後也可能留下 node 行程**。穩妥的作法是每條 `npm test` 後面接一次清理：

```bash
npm test -- --watch=false --browsers=ChromeHeadless 2>&1 | tail -3
powershell -NoProfile -Command "Get-Process node -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue"
```

（本機沒有其他長駐 node 服務時才能無差別清掉；有的話要挑 PID。）

另外 Bash tool 的 cwd 會跨呼叫保留——commit 時 `cd` 回 repo 根目錄之後，下一個 `npm test` 會在根目錄執行而 ENOENT。**每個 npm 指令都自己 `cd /f/VibeCode/MyCollection/web`。**

---

## Task 5：成就看板與拼貼牆尺寸

**Files:** Modify: `web/src/app/shared/showcase-sections/stats-section.component.ts`、`web/src/app/shared/showcase-sections/collage-section.component.ts`、`web/src/app/features/showcase/showcase.component.ts`

純 CSS 加一個綁定值，與其他 Task 完全獨立。內部頁 `slotCount` 由寫死的 `4` 改為 `8`；公開頁繼續用 `data.collageSlotCount`（分享者設定），**不要一起改掉**。

CSS 本身沒有值得寫單元測試的行為，但 `slotCount` 是有的——它決定拼貼牆渲染幾張卡。

- [ ] Step 1: 寫失敗測試（加在 `showcase.component.spec.ts`）

```ts
  it('feeds the collage eight slots on the internal showcase page', async () => {
    const fixture = await createShowcase(
      Array.from({ length: 10 }, (_, i) => item({ id: `i${i}`, effectiveDisplayMode: 'List' })),
    );

    expect(fixture.nativeElement.querySelectorAll('[data-collage-card]').length).toBe(8);
  });
```

- [ ] Step 2: 跑測試確認失敗　**確認拿到 4 而不是 8**（證明它讀的真的是 `slotCount`）
- [ ] Step 3: 最小實作
  - `stats-section`：`min-height: 16rem` → `min-height: clamp(20rem, 46vw, 40rem)`
  - `collage-section`：`.collage__card` `width: 11rem` → `18rem`；`.collage__wall` 加 `justify-content: center`
  - `showcase.component.ts`：`[slotCount]="8"`
  - `hero-section` **不動**
- [ ] Step 4: 跑單檔測試　Expected: `TOTAL: 10 SUCCESS`
- [ ] Step 5: 跑全部測試　Expected: `TOTAL: 197 SUCCESS`
- [ ] Step 6: Commit
```
git add web/src/app/shared/showcase-sections/stats-section.component.ts web/src/app/shared/showcase-sections/collage-section.component.ts web/src/app/features/showcase/showcase.component.ts web/src/app/features/showcase/showcase.component.spec.ts
```
```
style(web): enlarge the stats board and the collage wall

成就看板 min-height 16rem → clamp(20rem, 46vw, 40rem)：頁籤化後它
獨佔一整頁，原尺寸會在下方留一大片空白。焦點展品維持 16/10 不動
——它是左圖右欄的並排版型，硬拉高只會讓右欄的欄位面板留白。

拼貼牆改 8 格 × 18rem 置中（1920px 下排成 4+4）：它現在是預設
第一眼，需要足夠的視覺重量。公開頁仍用分享者設定的 slotCount。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

---

## Task 6：ItemPreviewOverlayComponent

**Files:** Create: `web/src/app/shared/item-preview-overlay/item-preview-overlay.component.ts`、`web/src/app/shared/item-preview-overlay/item-preview-overlay.component.spec.ts`

純呈現元件：接一個 `ShowcaseDisplayItem | null`，是 `null` 就什麼都不渲染。**200ms 進場延遲不由這個元件負責**——延遲屬於「何時要顯示」，是呼叫端（Task 7）的職責；元件只負責「拿到東西就畫出來」。這個切分讓延遲邏輯與渲染邏輯可以各自測試。

容易錯的地方是漸進式換圖：先掛 `cardPath`、背景載 `path`、載完才換。實作用一個 `Image` 物件在 `effect` 裡預載，`onload` 時更新 signal。測試不能真的等網路，所以斷言的是「初始 `src` 是 card 圖」而不是「最終會變成 full 圖」——後者留給手動驗證。

- [ ] Step 1: 寫失敗測試

```ts
import { TestBed } from '@angular/core/testing';
import { ItemPreviewOverlayComponent } from './item-preview-overlay.component';
import { ShowcaseDisplayItem } from '../showcase-sections/showcase-display-item';

const preview: ShowcaseDisplayItem = {
  id: 'x', name: '初音未來 1/7 比例模型', description: '這段描述不該出現在浮層裡',
  imageUrl: 'http://localhost/media/x-card.webp', effectiveDisplayMode: 'Hero',
  acquiredAt: '2026-01-15T00:00:00Z', price: { amount: 12800, currency: 'TWD' },
  rating: 9, storageLocation: '書房 A 櫃第二層',
  attributes: {}, cardAttributes: [{ key: 'scale', label: '比例', value: '1/7' }],
};

async function createOverlay(item: ShowcaseDisplayItem | null) {
  await TestBed.configureTestingModule({ imports: [ItemPreviewOverlayComponent] }).compileComponents();

  const fixture = TestBed.createComponent(ItemPreviewOverlayComponent);
  fixture.componentRef.setInput('item', item);
  fixture.detectChanges();

  return fixture;
}

describe('ItemPreviewOverlayComponent', () => {
  it('renders nothing without an item', async () => {
    const fixture = await createOverlay(null);
    expect(fixture.nativeElement.querySelector('[data-preview-overlay]')).toBeNull();
  });

  it('shows the name, card attributes, and acquisition fields', async () => {
    const fixture = await createOverlay(preview);
    const text = fixture.nativeElement.textContent;

    expect(fixture.nativeElement.querySelector('[data-preview-overlay]')).toBeTruthy();
    expect(text).toContain('初音未來 1/7 比例模型');
    expect(text).toContain('比例');
    expect(text).toContain('1/7');
    expect(text).toContain('書房 A 櫃第二層');
    expect(text).toContain('9');
  });

  it('never shows the description', async () => {
    const fixture = await createOverlay(preview);
    expect(fixture.nativeElement.textContent).not.toContain('這段描述不該出現在浮層裡');
  });

  it('starts from the already-cached card image', async () => {
    const fixture = await createOverlay(preview);
    expect(fixture.nativeElement.querySelector('[data-preview-image]').getAttribute('src'))
      .toBe('http://localhost/media/x-card.webp');
  });

  it('falls back to an initial when the item has no image', async () => {
    const fixture = await createOverlay({ ...preview, imageUrl: null });

    expect(fixture.nativeElement.querySelector('[data-preview-image]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-preview-placeholder]').textContent.trim())
      .toBe('初');
  });
});
```

- [ ] Step 2: 跑測試確認失敗
  Run: `cd web && npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/shared/item-preview-overlay/item-preview-overlay.component.spec.ts`
  **確認是「找不到模組」而非測試碼本身寫錯**
- [ ] Step 3: 最小實作
  - `item = input<ShowcaseDisplayItem | null>(null)`、`fullImageUrl = input<string | null>(null)`（呼叫端算好的原圖路徑）
  - 固定 16/10 框、`object-fit: contain`、模糊背景層填黑邊
  - 欄位以 `repeat(auto-fit, minmax(11rem, 1fr))` 網格壓在圖片下緣、漸層 scrim
  - scrim `rgb(5 7 13 / 72%)`、`pointer-events: none`、150ms 淡入、整體包在 `@media (hover: hover)`
  - **不渲染 `description`**
- [ ] Step 4: 跑單檔測試　Expected: `TOTAL: 5 SUCCESS`
- [ ] Step 5: 跑全部測試　Expected: `TOTAL: 202 SUCCESS`
- [ ] Step 6: Commit
```
git add web/src/app/shared/item-preview-overlay/
```
```
feat(web): add the centred item preview overlay

固定 16/10 框 + object-fit: contain，黑邊用同一張圖模糊放大填滿：
收藏裡直式公仔卡牌與寬扁的 Steam header 比例差極遠，讓浮層隨圖片
比例伸縮會在滑鼠移動時劇烈變形。

欄位沿用 Hero 那組但拿掉描述，pointer-events: none 讓滑鼠永遠不會
進入浮層，因此不會卡住預覽或擋住底下卡片的點擊。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

---

## Task 7：列表頁籤接上浮層

**Files:** Modify: `web/src/app/features/showcase/showcase.component.ts`、`web/src/app/features/showcase/showcase.component.spec.ts`

把延遲與 hover 狀態接起來。**這是唯一需要 `fakeAsync` 的 Task**——200ms 延遲必須用 `tick()` 驗證，而不是真的等。

容易錯的地方有兩個：一是滑鼠快速滑過多張卡片時，前一張的延遲計時器必須被取消，否則會閃出錯誤的品項；二是元件銷毀時計時器要清掉，否則測試會卡在 `whenStable()`——這正是既有 Hero/Stats 元件註解裡記載過的坑。

- [ ] Step 1: 寫失敗測試

```ts
  it('opens the preview only after the hover delay elapses', fakeAsync(async () => {
    const fixture = await createShowcase([item({ id: 'a', effectiveDisplayMode: 'List' })]);
    fixture.componentRef.setInput('view', 'list');
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('[data-item-card]');
    card.dispatchEvent(new MouseEvent('mouseenter'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-preview-overlay]')).toBeNull();

    tick(200);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-preview-overlay]')).toBeTruthy();

    card.dispatchEvent(new MouseEvent('mouseleave'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-preview-overlay]')).toBeNull();
  }));

  it('cancels a pending preview when the pointer moves on before the delay', fakeAsync(async () => {
    const fixture = await createShowcase([
      item({ id: 'a', effectiveDisplayMode: 'List' }),
      item({ id: 'b', effectiveDisplayMode: 'List' }),
    ]);
    fixture.componentRef.setInput('view', 'list');
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('[data-item-card]');
    cards[0].dispatchEvent(new MouseEvent('mouseenter'));
    tick(120);
    cards[0].dispatchEvent(new MouseEvent('mouseleave'));
    cards[1].dispatchEvent(new MouseEvent('mouseenter'));
    tick(200);
    fixture.detectChanges();

    // 只有第二張的預覽，不是第一張
    expect(fixture.nativeElement.querySelector('[data-preview-overlay]').textContent).toContain('b');
  }));

  it('does not show the preview outside the list tab', fakeAsync(async () => {
    const fixture = await createShowcase([item({ id: 'a', effectiveDisplayMode: 'Hero' })]);
    fixture.componentRef.setInput('view', 'hero');
    fixture.detectChanges();

    tick(200);
    expect(fixture.nativeElement.querySelector('[data-preview-overlay]')).toBeNull();
  }));
```

- [ ] Step 2: 跑測試確認失敗　**確認失敗是「延遲後仍找不到浮層」**（浮層還沒接上），而不是 `data-item-card` 選不到
- [ ] Step 3: 最小實作
  - `hovered = signal<ShowcaseDisplayItem | null>(null)`，`mouseenter` 起 200ms 計時器、`mouseleave` 清除並清空
  - 計時器用 `NgZone.runOutsideAngular()` 建立，`DestroyRef.onDestroy` 清除
  - 延遲期間就 `new Image().src = fullUrl` 預載原圖
  - 列表頁籤的 `<app-item-card>` 外層包一個帶 `(mouseenter)` / `(mouseleave)` 的容器，浮層放在列表頁籤區塊內
- [ ] Step 4: 跑單檔測試　Expected: `TOTAL: 13 SUCCESS`
- [ ] Step 5: 跑全部測試　Expected: `TOTAL: 205 SUCCESS`
- [ ] Step 6: Commit
```
git add web/src/app/features/showcase/showcase.component.ts web/src/app/features/showcase/showcase.component.spec.ts
```
```
feat(web): show a centred preview when hovering a showcase list card

200ms 進場延遲讓滑鼠滑過整排卡片時不會瘋狂閃爍；游標在延遲結束前
移開就取消，不會閃出錯誤的品項。延遲期間即開始預載 full 圖，載完
替換掉列表已快取的 card 圖。

計時器比照 Hero/Stats 以 runOutsideAngular 建立、DestroyRef 清除，
避免卡住 ApplicationRef.whenStable()。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

---

## 完成後的驗證

- [ ] `cd web && npm test -- --watch=false --browsers=ChromeHeadless` → `TOTAL: 205 SUCCESS`（基準線 179 + 26）
- [ ] `cd web && npm run build` → 0 errors、0 warnings
- [ ] `git status` → 工作樹乾淨，無未追蹤檔案
- [ ] `git log --oneline master..HEAD` → 8 顆 commit（`docs:` ×1、`feat(web):` ×6、`style(web):` ×1）
- [ ] `git diff master --stat` → 只動到 `web/src/app/{shared,features}/`、`docs/`、`CONTEXT.md`
- [ ] `grep -rn "storageLocation" web/src/app/shared/showcase-sections/showcase-display-item.ts` → 公開頁那條映射仍寫死 `null`

**測試數字是預測不是事實。** 實際數字可能因為實作時拆分測試而不同——重點是不得低於 179，且新增的每條測試都看過紅燈。

## 手動驗證

自動測試涵蓋不到的，需要真實瀏覽器：

1. `npm start`，登入後開 `/showcase`。確認預設落在拼貼牆，8 張拍立得排成置中兩排。
2. 點「遊戲成就」，確認看板高度明顯放大（桌機約佔 40rem），背景圖沉浸感足夠。
3. 用鍵盤 Tab 進入頁籤列（應只進入一次），按左右方向鍵切換，確認**跳過數字為 0 的停用頁籤**，Home/End 跳到頭尾。
4. 確認網址列隨切換更新為 `?view=stats` 等；**重新整理後停在同一個頁籤**；按上一頁不會在頁籤之間堆積歷史。
5. 手動輸入 `?view=nonsense`，確認退回拼貼牆而不是空白頁。
6. 切到「列表」，滑鼠停在一張卡片上約 0.2 秒 → 浮層淡入。確認：直式圖片完整不裁切、黑邊是模糊的同一張圖、**原圖載完後畫質提升**（自動測試測不到這條）、欄位沒有描述、滑鼠移開即消失、快速滑過整排不會閃爍。
7. 滑鼠移到浮層本身覆蓋的區域，確認**底下的卡片仍可點擊**進入詳細頁。
8. 開瀏覽器 DevTools 切成觸控模擬，確認 hover 浮層完全不觸發。
9. 建一個分享連結開 `/p/:slug`，確認四個頁籤與內部頁一致、**列表頁籤沒有 hover 浮層**、切到焦點頁籤時**看不到存放位置**（ADR-0008）。
10. 縮到手機寬度，確認頁籤列不溢出、成就看板不會變成一整屏黑底圖。

## 後續（不在本計畫內）

- 分享者指定公開頁預設頁籤（`ShareLink.DefaultView`）。
- `/catalog` 庫存頁的 hover 預覽。
- 觸控裝置的長按預覽。
- 浮層內的多圖瀏覽。
- 拼貼牆格數依視窗寬度自適應。
