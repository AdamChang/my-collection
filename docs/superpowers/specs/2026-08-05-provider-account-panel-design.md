# 帳號綁定面板抽出與 PSN 綁定入口

## 問題

PSN 同步的後端已完整可用，但**從瀏覽器碰不到**：設定頁的帳號綁定寫死 Steam
（`link('steam', …)`、`sync('steam')`、表單欄位為 SteamID64 與 Web API Key），
沒有任何地方能設定 NPSSO。缺口源自已核可的 PSN 計畫——六個 Task 中
Task 5 只做卡片顯示，從未有一項是帳號綁定 UI。

前端 API 層無需改動：`IngestionService` 的 `accounts` / `link` / `unlink` / `sync`
本來就都吃 provider key。後端亦無需改動。這是純前端工作。

## 決定

1. **同頁、各自獨立面板。** 設定頁維持單一頁面的垂直堆疊，PSN 多出一塊自己的面板。
   不做分頁籤，不做獨立路由。共用的「同步紀錄」表格**保留**，仍列出全部來源。
2. **抽出可重用的 `ProviderAccountComponent`**，Steam 與 PSN 各一個實例，
   而非為 PSN 另寫專用元件。沿用 `ProviderEnrichComponent` 已建立的
   「吃 provider key、可重複實例化」模式——同一頁上兩種面板的用法應該長得一樣。
3. **每個面板只鎖自己。** 移除頁面級的 `busy` 鎖。

## 範圍

**動**：

- `web/src/app/features/settings/settings.component.ts`
- 新增 `web/src/app/features/settings/provider-account.component.ts`
- 新增 `web/src/app/features/settings/provider-account.component.spec.ts`
- `web/src/app/features/settings/settings.component.spec.ts`（案例遷移）

**不動**：後端全部、`core/api/ingestion.service.ts`、`core/models.ts`、
`provider-enrich.component.ts`、同步紀錄表格、分享連結、圖片轉移。

## `ProviderAccountComponent`

介面刻意與 `ProviderEnrichComponent` 對稱。

```ts
provider       = input.required<string>();
heading        = input.required<string>();
secretLabel    = input.required<string>();   // 'Web API Key' / 'NPSSO'
hint           = input<string>('');
requiresUserId = input(true);                // PSN 為 false
userIdLabel    = input('');                  // 見下方註記
fixedUserId    = input('me');                // requiresUserId 為 false 時送出的值
changed        = output<void>();             // 綁定／解綁／同步後通知父層重載紀錄
```

`userIdLabel` **不可宣告為 `input.required`**——PSN 實例不會傳它。
「requiresUserId 為 true 時要給」是呼叫端的義務，不是型別層的約束。

元件自行持有 `account / linking / unlinking / syncing`，以及**只涵蓋自身動作**的
`busy`。呼叫 `accounts()` 後以 `provider` 篩出自己那筆。

送出綁定時，`externalUserId` 取 `requiresUserId() ? userId : fixedUserId()`。
成功後清空祕密欄位（沿用既有作法，避免 NPSSO 留在 DOM 中）。

### 使用方式

```html
<app-provider-account provider="steam" heading="Steam 帳號"
  userIdLabel="SteamID64" secretLabel="Web API Key"
  hint="個人資料需設為公開，否則 Steam 回傳空清單。"
  (changed)="reloadJobs()" />

<app-provider-account provider="psn" heading="PSN 帳號"
  [requiresUserId]="false" secretLabel="NPSSO"
  hint="登入 playstation.com 後，於同一瀏覽器開啟 ca.account.sony.com/api/v1/ssocookie，
        取回應中的 64 字元字串。約兩個月過期，需重新取得。"
  (changed)="reloadJobs()" />
```

## `SettingsComponent` 的變化

移除 `steamAccount`、`link()`、`unlink()`、`sync()`、`steamId`、`apiKey`
與 `syncing / linking / unlinking` 三個 signal。`busy` 縮減為只涵蓋分享連結的
`creatingShare` 與 `removingShareId`。`reloadAccounts()` 一併移除——
帳號查詢改由各面板自理。

預期行數：`SettingsComponent` 由 285 行降至約 200 行，新元件約 100 行。

## PSN 特有的細節

**已綁定時不顯示使用者 ID。** Steam 顯示「已綁定 SteamID64：7656…」有意義；
PSN 的 `externalUserId` 是字面值 `me`，顯示「已綁定：me」只會讓人困惑。
`requiresUserId` 為 false 時改顯示「已綁定（更新於 {{updatedAt}}）」。

**NPSSO 過期不加任何機制。** ADR-0004 已決定只以訊息表達，不新增狀態分類。
落點已查證：`SyncCommand` 失敗時先寫入 `job.Error` 再重擲，因此
「NPSSO 已過期，請重新取得」會同時出現在通知列與同步紀錄表的狀態欄。
**不要**為此新增過期旗標、重新綁定引導或任何持久化的過期狀態——那會與 ADR-0004 相牴觸。

**PSN 同步可能持續數分鐘。** 數百款遊戲分頁抓取，按鈕會長時間停在「同步中…」。
這是 `SyncCommand` 同步執行的既有行為（PSN 是 `IBulkSyncProvider`，
不像 Steam 繁中補完走背景佇列）。本次**不改**，僅記錄此已知體驗問題。

## 錯誤處理

沿用既有慣例：所有呼叫的 error 分支交給 `IGNORE_HANDLED_BY_INTERCEPTOR`，
由 `error.interceptor` 統一顯示通知。元件不自行組錯誤訊息。
`finalize` 中一律重置忙碌 signal，並在同步路徑額外發出 `changed`——
失敗的同步也會留下一筆紀錄，兩條路徑都要讓父層重載。

## 測試

新增 `provider-account.component.spec.ts`，比照 `provider-enrich.component.spec.ts`
的結構（TestBed + 假的 `IngestionService`）：

1. 綁定送出的 provider 與 `externalUserId` 正確——PSN 案例須斷言送出的是 `me`。
2. 未綁定時不渲染同步按鈕；已綁定時渲染。
3. `requiresUserId` 為 false 時不渲染使用者 ID 欄位，且不顯示「已綁定：me」。
4. 動作進行中只停用自身按鈕（以兩個實例並存驗證互不影響）。
5. 綁定成功後祕密欄位被清空。

`settings.component.spec.ts` 中涵蓋 Steam 綁定／解綁／同步的既有案例
**必須遷移至新 spec，不得刪除**——否則 Steam 綁定會在這次重構中失去覆蓋。

## 驗收

- `npm test -- --watch=false --browsers=ChromeHeadless` 全綠。
- `npm run build` EXIT=0。
- 實機：能以 NPSSO 綁定 PSN、觸發同步、於同步紀錄看到 `psn` 那筆作業。

## 不做

- 分頁籤或獨立路由。
- 拆分同步紀錄表格。
- 依 `/ingest/providers` 的能力旗標動態產生面板——目前只有兩個來源，
  硬列兩個實例比動態產生好讀。第三個來源出現時再談。
- 任何 NPSSO 過期的偵測或引導機制（見上，與 ADR-0004 相牴觸）。
- 把 PSN 同步改為背景執行。
