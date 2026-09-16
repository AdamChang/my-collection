# 品類欄位改名：前端實作計畫

> For agentic workers: REQUIRED SUB-SKILL — 逐 Task 走 TDD。一個 Task 一個 commit，`git add` 只用明確路徑。karma 的失敗模式是「掛住」不是紅燈（見 `20-TechStack/Testing/Karma-Hangs-Instead-Of-Failing`）：先設 `CHROME_BIN`、輸出導向 log 檔、看最後一行 `TOTAL: N SUCCESS`。

**Goal**：品類編輯 dialog 內，既有欄位的 key 唯讀、提供「重新命名」inline 操作，呼叫後端 `POST /categories/{id}/fields/{key}/rename` 並回報搬移數。刪除品類的 409 與改名的 400/409 交給既有 interceptor。

**Architecture**：Angular 20 standalone + signals + template-driven form（沿用 `CategoriesComponent` 現況）。改名是獨立 HTTP 呼叫，不排隊到「儲存」。

**Tech Stack**：Angular 20.3、Karma + Jasmine、`HttpTestingController`。

- 設計文件：`docs/superpowers/specs/2026-09-15-category-field-rename-design.md` §4
- 後端計畫：`docs/superpowers/plans/2026-09-15-category-field-rename-backend.md`——本計畫依賴其 Task 5 的 `RenameFieldResultDto { category, movedItemCount }` 與 Task 7 的路由；DTO 形狀已定，可在後端 Task 5 之後並行。

## 執行前必讀

### 環境

- 路徑：`F:\VibeCode\MyCollection\web`
- 分支：`feat/category-field-rename`（與後端同一條；若後端尚未合併，從該分支續接）
- 全部測試（Git Bash）：
  ```bash
  export CHROME_BIN="C:\Program Files\Google\Chrome\Application\chrome.exe"
  cd web && npm test -- --watch=false --browsers=ChromeHeadless > ../.tmp/karma.log 2>&1
  ```
  完成判定：log 最後一行 `TOTAL: N SUCCESS`；process 可能不退出，看到即可收掉。
- 單檔：`npm test -- --watch=false --browsers=ChromeHeadless --include='**/categories.component.spec.ts'`
- Build：`npm run build`（0 warnings）

### 基準線

- 前端：**`TOTAL: 268 SUCCESS`**（2026-09-16 於 `997106c` 實測）；Task 1 後 269、Task 2 後 271（`e849f70`，實測）
- build：0 warnings

任何時候數字低於基準線就是弄壞了東西。

### 絕對不要碰的檔案

- `src/`、`tests/`（後端）——屬於後端計畫。
- `web/package.json` / `package-lock.json`——不裝新套件。
- `web/.angular/`、`.tmp/`——不進 git。
- 禁止 `git add .`；Angular CLI 可能寫入 analytics 設定，只 add 本 Task 列出的路徑。

### 慣例

- 註解與 commit message 繁體中文；識別字英文。commit 格式 `feat(web): …` / `test(web): …`，結尾 `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`。
- 元件測試用 `TestBed` + `useValue` 假的 `CategoryService` / `NotificationService`（沿用 `categories.component.spec.ts` 既有寫法）；service 測試用 `provideHttpClientTesting` + `HttpTestingController`（沿用 `catalog.service.spec.ts`）。
- 不用 `window.prompt` / `confirm`——瀏覽器 modal 會擋自動化，也無法客製文案。
- 錯誤一律 `error: IGNORE_HANDLED_BY_INTERCEPTOR`，元件不解析 ProblemDetails。

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `web/src/app/core/api/category.service.spec.ts` | `renameField` 的 HTTP 形狀（目前 `core/api/` 下無此 spec，已確認） |

### 修改

| 檔案 | 改動 |
|---|---|
| `web/src/app/core/models.ts` | `RenameFieldResultDto` |
| `web/src/app/core/api/category.service.ts` | `renameField(id, key, newKey)` |
| `web/src/app/features/categories/categories.component.ts` | `originalKeys`、`renaming`、`renamingInFlight`；key 唯讀；inline 改名列；`busy` 納入 |
| `web/src/app/features/categories/categories.component.spec.ts` | +4 測試 |

### Task 相依順序

```
Task 1  models + service.renameField（獨立）
Task 2  元件：既有 key 唯讀 + 重新命名入口 ← 1
Task 3  元件：confirm 呼叫 API、更新 draft、busy 鎖 ← 2
```

三個 Task 線性。Usage-safe checkpoint：每個 Task 後都可收工（Task 2 結束時 UI 有按鈕但按了只開 inline 列，不呼叫 API——這是可接受的中間態）。

---

## Task 1：`RenameFieldResultDto` 與 `CategoryService.renameField` ✅ `95bc690`

**Files:** Modify: `web/src/app/core/models.ts`、`web/src/app/core/api/category.service.ts`；Create: `web/src/app/core/api/category.service.spec.ts`

容易錯：路徑裡的 `key` 沒 encode。key 受後端 regex 限制為 `[a-z][a-zA-Z0-9]*`，理論上安全，但仍用 `encodeURIComponent` 防禦——成本是零。

- [ ] Step 1：寫失敗測試

```ts
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { CategoryService } from './category.service';

describe('CategoryService', () => {
  let service: CategoryService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CategoryService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('posts a field rename and returns the moved count', async () => {
    const pending = firstValueFrom(service.renameField('c1', 'price', 'purchasePrice'));

    const request = http.expectOne('/api/categories/c1/fields/price/rename');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ newKey: 'purchasePrice' });

    request.flush({
      category: { id: 'c1', name: '公仔', icon: 'box', kind: 'Physical', isSystem: false, defaultDisplayMode: 'List', fields: [] },
      movedItemCount: 12,
    });

    const result = await pending;
    expect(result.movedItemCount).toBe(12);
    expect(result.category.id).toBe('c1');
  });
});
```

- [ ] Step 2：單檔跑 → TypeScript 編譯錯（`renameField` 不存在）。正確的紅。
- [ ] Step 3：最小實作

`models.ts`（放在 `CategoryDto` 之後）：
```ts
/** 改名回應。movedItemCount 是使用者唯一能確認「真的動到資料」的證據（ADR-0012）。 */
export interface RenameFieldResultDto {
  category: CategoryDto;
  movedItemCount: number;
}
```

`category.service.ts`：
```ts
renameField(id: string, key: string, newKey: string): Observable<RenameFieldResultDto> {
  return this.http.post<RenameFieldResultDto>(
    `${API_BASE}/categories/${id}/fields/${encodeURIComponent(key)}/rename`,
    { newKey },
  );
}
```

- [ ] Step 4：單檔 → `TOTAL: 1 SUCCESS`
- [ ] Step 5：全部 → 基準線 + 1
- [ ] Step 6：Commit `feat(web): CategoryService.renameField`；`git add web/src/app/core/models.ts web/src/app/core/api/category.service.ts web/src/app/core/api/category.service.spec.ts`

---

## Task 2：既有欄位 key 唯讀 + 「重新命名」入口 ✅ `e849f70`

**Files:** Modify: `web/src/app/features/categories/categories.component.ts`、`categories.component.spec.ts`

「既有」的定義是**開啟 dialog 時品類已宣告的鍵**，不是「draft 裡有 key 的欄位」——使用者在 dialog 裡新加、打了 key 的欄位仍要可改。所以要在 `edit()` 時快照一份 `originalKeys`，`startNew()` 時清空。用 `readonly` 不用 `disabled`：disabled 的 ngModel 不會進 payload，PUT 會把該欄位當成撤回。

- [ ] Step 1：寫失敗測試（加進 `categories.component.spec.ts`；抽一個 helper 減少重複）

```ts
const custom = {
  id: 'c1', name: '公仔', icon: 'box', kind: 'Physical' as const, isSystem: false,
  defaultDisplayMode: 'List' as const,
  fields: [{ key: 'price', label: '價格', type: 'Text' as const, options: null, required: false, searchable: false, showOnCard: false }],
};

async function setup(api: Partial<CategoryService>, notifications: Partial<NotificationService> = {}) {
  await TestBed.configureTestingModule({
    imports: [CategoriesComponent],
    providers: [
      { provide: CategoryService, useValue: { list: () => of([custom]), ...api } },
      { provide: NotificationService, useValue: { success: () => undefined, error: () => undefined, ...notifications } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(CategoriesComponent);
  fixture.detectChanges();
  fixture.componentInstance.edit(custom);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

it('locks the key of fields the category already declares but not of fields added in this session', async () => {
  const fixture = await setup({});

  // 模板的 [name]="'key' + $index" 是 property binding，被 NgModel 接走、不會落到 DOM attribute，
  // 所以用模板既有的 aria-label 定位（實測偏離：計畫原寫 input[name="key0"] 會查到 null）
  const existingKey: HTMLInputElement = fixture.nativeElement.querySelector('input[aria-label="欄位 1 key"]');
  expect(existingKey.readOnly).toBe(true);
  expect(fixture.nativeElement.querySelector('button[data-rename="0"]')).toBeTruthy();

  fixture.componentInstance.addField();
  fixture.detectChanges();

  const newKey: HTMLInputElement = fixture.nativeElement.querySelector('input[aria-label="欄位 2 key"]');
  expect(newKey.readOnly).toBe(false);
  expect(fixture.nativeElement.querySelector('button[data-rename="1"]')).toBeNull();
});

it('opens an inline rename row for the chosen field', async () => {
  const fixture = await setup({});

  fixture.nativeElement.querySelector('button[data-rename="0"]').click();
  fixture.detectChanges();

  const input: HTMLInputElement = fixture.nativeElement.querySelector('input[name="renameKey"]');
  expect(input).toBeTruthy();
  expect(fixture.nativeElement.querySelector('button[data-rename-confirm]')).toBeTruthy();
  expect(fixture.nativeElement.querySelector('button[data-rename-cancel]')).toBeTruthy();
});
```

- [ ] Step 2：單檔跑 → 第一個測試在 `readOnly` 斷言失敗（現況是 false）。正確的紅。
- [ ] Step 3：最小實作

元件 class 新增：
```ts
/** 開啟 dialog 時品類已宣告的鍵。key 是身分（ADR-0012），既有欄位的 key 只能走改名，不能直接編輯。 */
readonly originalKeys = signal<ReadonlySet<string>>(new Set());
/** 正在改名的欄位索引與輸入中的新鍵；null = 沒有 inline 列展開。 */
readonly renaming = signal<{ index: number; newKey: string } | null>(null);

isExistingField(field: CategoryFieldDto): boolean {
  return this.originalKeys().has(field.key);
}

startRename(index: number): void {
  if (this.busy()) {
    return;
  }
  this.renaming.set({ index, newKey: '' });
}

cancelRename(): void {
  this.renaming.set(null);
}

setRenameKey(newKey: string): void {
  this.renaming.update((r) => (r ? { ...r, newKey } : r));
}
```

`startNew()` 加 `this.originalKeys.set(new Set());`、`this.renaming.set(null);`
`edit()` 加 `this.originalKeys.set(new Set(category.fields.map((f) => f.key)));`、`this.renaming.set(null);`
`dismiss()` 加 `this.renaming.set(null);`

模板：key 輸入框加 `[readonly]="isExistingField(field)"`；在 `<button type="button" (click)="removeField($index)">移除</button>` 前加：

```html
@if (editingId() && isExistingField(field)) {
  <button type="button" [attr.data-rename]="$index" [disabled]="busy()" (click)="startRename($index)">重新命名</button>
}
@if (renaming(); as r) {
  @if (r.index === $index) {
    <div class="editor__rename" role="group" aria-label="重新命名欄位">
      <input
        [ngModel]="r.newKey"
        (ngModelChange)="setRenameKey($event)"
        name="renameKey"
        aria-label="新的 key"
        placeholder="新的 key（camelCase）"
      />
      <button type="button" data-rename-confirm [disabled]="busy()" (click)="confirmRename()">確認</button>
      <button type="button" data-rename-cancel (click)="cancelRename()">取消</button>
    </div>
  }
}
```

`confirmRename()` 在此 Task 先放空方法（Task 3 填）。

注意：「重新命名」按鈕與 inline 列的 `isExistingField(field)` 判定用的是 `field.key`——改名成功後 Task 3 會同步更新 `originalKeys`，否則新鍵會被視為「本次新增」而解鎖。

- [ ] Step 4：單檔 → 既有 4 + 2 = `TOTAL: 6 SUCCESS`
- [ ] Step 5：全部 → 基準線 + 3
- [ ] Step 6：Commit `feat(web): 既有欄位 key 唯讀，提供重新命名入口`；`git add` 兩個路徑。

---

## Task 3：確認改名 → 呼叫 API → 更新 draft → busy 鎖 ✅ `cdd58cb`

**Files:** Modify: `categories.component.ts`、`categories.component.spec.ts`

容易錯的三處：(1) 成功後只改 draft 不改 `originalKeys`，新鍵會被當成本次新增而解鎖 key 輸入框；(2) `busy` 沒納入 renaming，使用者可以在改名進行中按儲存，PUT 帶著舊鍵送出，後端把它當撤回；(3) 失敗時關掉 inline 列——使用者打錯 camelCase 得重按一次，讓它留著。

- [ ] Step 1：寫失敗測試

```ts
import { Subject, throwError } from 'rxjs';

it('confirms a rename through the API, updates the draft key and reports the moved count', async () => {
  const renameField = jasmine.createSpy('renameField').and.returnValue(
    of({ category: { ...custom, fields: [{ ...custom.fields[0], key: 'purchasePrice' }] }, movedItemCount: 12 }),
  );
  const success = jasmine.createSpy('success');
  const fixture = await setup({ renameField }, { success });

  fixture.nativeElement.querySelector('button[data-rename="0"]').click();
  fixture.detectChanges();

  const input: HTMLInputElement = fixture.nativeElement.querySelector('input[name="renameKey"]');
  input.value = 'purchasePrice';
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();

  fixture.nativeElement.querySelector('button[data-rename-confirm]').click();
  fixture.detectChanges();

  expect(renameField).toHaveBeenCalledWith('c1', 'price', 'purchasePrice');
  expect(fixture.componentInstance.draft()!.fields[0].key).toBe('purchasePrice');
  expect(fixture.componentInstance.isExistingField(fixture.componentInstance.draft()!.fields[0])).toBe(true);
  expect(success).toHaveBeenCalledWith(jasmine.stringContaining('12'));
  expect(fixture.nativeElement.querySelector('input[name="renameKey"]')).toBeNull();
});

it('keeps the draft and the inline row when the rename fails', async () => {
  const renameField = jasmine.createSpy('renameField').and.returnValue(throwError(() => new Error('409')));
  const fixture = await setup({ renameField });

  fixture.nativeElement.querySelector('button[data-rename="0"]').click();
  fixture.detectChanges();
  const input: HTMLInputElement = fixture.nativeElement.querySelector('input[name="renameKey"]');
  input.value = 'purchasePrice';
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
  fixture.nativeElement.querySelector('button[data-rename-confirm]').click();
  fixture.detectChanges();

  expect(fixture.componentInstance.draft()!.fields[0].key).toBe('price');
  expect(fixture.nativeElement.querySelector('input[name="renameKey"]')).toBeTruthy();
});

it('locks save and delete while a rename is in flight', async () => {
  const pending = new Subject<never>();
  const renameField = jasmine.createSpy('renameField').and.returnValue(pending.asObservable());
  const fixture = await setup({ renameField });

  fixture.nativeElement.querySelector('button[data-rename="0"]').click();
  fixture.detectChanges();
  const input: HTMLInputElement = fixture.nativeElement.querySelector('input[name="renameKey"]');
  input.value = 'purchasePrice';
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
  fixture.nativeElement.querySelector('button[data-rename-confirm]').click();
  fixture.detectChanges();

  expect(fixture.componentInstance.busy()).toBe(true);

  pending.complete();
  fixture.detectChanges();
  expect(fixture.componentInstance.busy()).toBe(false);
});
```

（`busy` 目前是 `readonly busy = computed(...)`，測試直接讀 signal；若既有測試有「儲存鈕 disabled」的 DOM 斷言方式，改成同樣的 DOM 斷言更一致——以既有檔案為準。）

- [ ] Step 2：單檔跑 → 第一個測試 `renameField` 未被呼叫。正確的紅。
- [ ] Step 3：最小實作

```ts
readonly renamingInFlight = signal(false);

/** 儲存、刪除、改名三者不該並行，任一進行中就鎖住全部。 */
readonly busy = computed(() => this.saving() || this.removing() || this.renamingInFlight());

confirmRename(): void {
  const id = this.editingId();
  const pending = this.renaming();
  const draft = this.draft();
  if (!id || !pending || !draft || this.busy()) {
    return;
  }

  const oldKey = draft.fields[pending.index].key;
  const newKey = pending.newKey.trim();
  if (!newKey) {
    return;
  }

  this.renamingInFlight.set(true);
  this.api
    .renameField(id, oldKey, newKey)
    .pipe(finalize(() => this.renamingInFlight.set(false)))
    .subscribe({
      next: (result) => {
        this.notifications.success(`已將「${oldKey}」改名為「${newKey}」，搬移 ${result.movedItemCount} 筆品項的屬性。`);
        // 改名已在後端生效，不等表單儲存；draft 與 originalKeys 一起更新，否則新鍵會被當成本次新增而解鎖
        this.draft.update((current) =>
          current
            ? { ...current, fields: current.fields.map((f, i) => (i === pending.index ? { ...f, key: newKey } : f)) }
            : current,
        );
        this.originalKeys.update((keys) => {
          const next = new Set(keys);
          next.delete(oldKey);
          next.add(newKey);
          return next;
        });
        this.renaming.set(null);
        this.reload();
      },
      // 失敗留著 inline 列，讓使用者修正後重送；訊息由 interceptor 顯示
      error: IGNORE_HANDLED_BY_INTERCEPTOR,
    });
}
```

- [x] Step 4：單檔 → `TOTAL: 9 SUCCESS`（實測 10，見下）
- [x] Step 5：全部 → 271 + 3 = 274；`npm run build` 0 warnings（實測 **275**，見下）

> 實測偏差：
> 1. 三個新測試在點 `data-rename` 之後都要 `await fixture.whenStable()`。inline 列的 `NgModel` 在 `<form>` 內，`NgForm.addControl` 延後一個 microtask 才 `setUpControl`，否則 `dispatchEvent('input')` 當下 control 尚未接線、`newKey` 為空而提前 return。
> 2. `busy` 直接讀 signal 斷言——既有測試沒有「儲存鈕 disabled」的 DOM 寫法。
> 3. 順手修了 Task 2 記下的 `isExistingField` 邊角：`removeField(index)` 同步把該 key 從 `originalKeys` 剔除，讓 `originalKeys` 的語意收斂成「draft 仍宣告的既有鍵」，同名重新新增的欄位自然判成本次新增。不影響 PUT 內容（撤回受保護欄位仍由後端以 payload 判定）。+1 測試 `treats a field re-added with a removed key as new in this session`，所以是 275 不是 274。
> 4. 留待後續：inline 改名列展開中若移除更前面的欄位，`renaming().index` 不會位移（既有行為）。
- [ ] Step 6：Commit `feat(web): 欄位改名呼叫 API 並回報搬移數`；`git add` 兩個路徑。

---

## 完成後的驗證

- [x] karma `TOTAL: 274 SUCCESS`（log 最後一行）——實測 275
- [ ] `npm run build` 0 warnings
- [ ] `git status` 乾淨；`git diff master..HEAD --stat -- web/` 只有本計畫列的 5 個檔案
- [x] 3 顆 commit（前端）：`95bc690`、`e849f70`、`cdd58cb`
- [ ] 殘留的 node / chrome 行程已收掉

## 手動驗證

需真實瀏覽器與後端（本機 `docker compose up` 或部署後）：

1. 編輯自訂品類 → 既有欄位 key 呈唯讀（點擊無法輸入），新增欄位 key 可輸入。
2. 按「重新命名」→ 輸入 `PurchasePrice`（大寫開頭）→ 確認 → interceptor 顯示 400 訊息，inline 列仍在，改成 `purchasePrice` → 成功通知含搬移數 → 該列 key 顯示新鍵且仍唯讀。
3. 改名進行中（可用 devtools 節流）→ 「儲存」「刪除」「重新命名」皆 disabled；Esc 無法關閉 dialog。
4. 刪除仍有品項的品類 → 顯示「仍有 N 筆品項」的 409 訊息，dialog 仍開著。
5. 改名後回庫存頁，若網址帶 `attr.price=…` 舊鍵 → 列表為空或不篩，重選篩選即恢復（ADR-0012 後果段，預期行為）。

## 後續（不在本計畫內）

- 前端預判受保護鍵（需後端提供集合端點）。
- 改名後修正網址／返回點的舊鍵。
- 批次改名、undo。
- `CategoryDto.itemCount` 與刪除鈕預先 disabled。
