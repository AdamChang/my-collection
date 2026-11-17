# 邀請制註冊（前端）設計

- 日期：2026-09-28
- 範圍：前端（`web/`）與相關文件。後端已完成，見 [後端設計](2026-09-28-invitation-only-registration-design.md) 與 `plans/2026-09-28-invitation-only-registration-backend.md`。
- 決策來源：[ADR-0014](../../adr/0014-invitation-only-issued-by-maintenance-command.md)、tech debt I2、後端計畫「後續」一節

## 目標

後端合併後，`POST /auth/register` 缺 `invitationCode` 會回 400，前端目前的註冊模式已經壞掉。這份設計讓前端：

1. 新增 `/register` 頁，讀取維運指令產生的連結 `{origin}/register?code=XXXXX-XXXXX-XXXXX-XXXXX`，把碼帶入表單。
2. 登入頁移除「登入／註冊」切換，只留一個「有邀請碼？前往註冊」的連結。
3. `AuthService.register` 帶上 `invitationCode`，`models.ts` 新增對應的 request 型別。
4. 註冊被拒（403）時，在頁內顯示繁中的通用訊息，不顯示 toast。

**前後端必須一起部署**：兩者目前在同一個分支 `feat/invitation-registration`，合併成一個 PR。

## 後端契約（已實作，前端只能配合）

| 項目 | 內容 |
|---|---|
| 請求 | `POST /auth/register`，body `{ email, password, displayName, invitationCode }`，回 `AuthResponse`（與現在相同） |
| 碼的比對 | 伺服器端正規化：不分大小寫、忽略 `-` 與空白，`I`／`L` 視為 `1`、`O` 視為 `0`。**前端原樣送出，不做任何正規化** |
| 400 | validator 失敗（例如碼為空，或長度超過 64） |
| 403 | 碼不存在、已過期、已使用、已撤銷、email 不符，一律回同一個 detail：`The invitation is invalid or no longer valid.` |
| 409 | email 已被註冊（只有在兩人競態時才會出現，因為已註冊的 email 發不出邀請） |

前端**沒有**、也不應該有「用碼查詢邀請 email」的端點：那會讓持有碼的人能列舉 email，也會讓碼可以被離線驗證。所以 email 必須由使用者自己輸入，頁面要事先說明「email 必須與收到邀請的信箱相同」。

## 設計決策

### 一、`/register` 是獨立元件，不和登入共用一個元件切換模式

新增 `features/auth/register.component.ts`；`LoginComponent` 刪掉 `mode`、`displayName`、`toggle()`。

- 被否決：保留 `LoginComponent` 並由 route data 決定模式。兩種模式的欄位（碼、顯示名稱）、成功後的去向（登入回 `returnUrl`，註冊回 `/`）、錯誤呈現（註冊要處理 403）都不同，塞在同一個元件裡，模板和 `submit()` 會各有一條 `if (mode)` 分支，而且登入頁的測試得同時涵蓋註冊。
- 代價：兩個元件各有一份相似的樣式（約 8 行）。目前只有兩處，抽共用樣式的收益不大；出現第三個類似頁面時再抽。

### 二、`?code=` 只在元件建立時讀一次，之後以表單為準

`RegisterComponent` 從 `ActivatedRoute.snapshot.queryParamMap.get('code')` 帶入欄位初值，欄位保持可編輯，讓只拿到碼、沒拿到連結的人也能手動輸入。

- 被否決：用 `withComponentInputBinding()` 綁成 `input()`。`input()` 是唯讀的，欄位又必須可編輯，還得再包一層 `linkedSignal`；而 `LoginComponent` 讀 `returnUrl` 本來就用 snapshot，沿用同一個寫法比較一致。
- 不套用 ADR-0010「URL 是真實來源」：那條規則針對可分享、可回上一頁的篩選狀態。邀請碼是一次性輸入，不需要雙向同步回 URL。
- 不在讀取後把 `?code=` 從網址列移除。碼會留在瀏覽器歷程紀錄裡，但它綁定 email、只能用一次、預設 7 天過期，註冊成功後就失效；移除它的好處不足以抵銷「重新整理後碼不見」的困擾。**結論翻轉的條件**：邀請的有效期限拉長到數週以上，或 ADR-0014 改成不綁 email。

### 三、403 在頁內顯示繁中訊息，toast 由 `HttpContextToken` 按狀態碼關閉

`errorInterceptor` 目前對所有非 401 錯誤顯示 toast，內容是後端的英文 detail。新增：

```ts
export const SUPPRESS_ERROR_TOAST = new HttpContextToken<(error: HttpErrorResponse) => boolean>(() => false);
```

`errorInterceptor` 在顯示 toast 前先問這個 predicate，回 `true` 就不顯示，但錯誤照樣往外拋。`AuthService.register` 在請求上設定 `error => error.status === 403`；`RegisterComponent` 捕捉 403，顯示頁內訊息（`role="alert"`）。400、409、連線失敗仍然走 toast。

- 為什麼是 predicate 而不是 boolean：註冊只想自己處理 403。如果用 boolean 整個關掉，400（validator）和 409 就沒有任何提示，元件得把 `errorInterceptor.describe` 的邏輯複製一份。
- 為什麼設在 `AuthService` 而不是元件：`HttpContext` 是請求的屬性，只有發請求的一方能設定；元件拿到的是 `Promise`，碰不到請求。這樣做等於 `register()` 的契約多了一條「403 由呼叫端顯示」，會寫在 JSDoc 裡。
- 被否決：在 `errorInterceptor` 裡用 URL 判斷（像現在的 `isRefresh`）。那會讓 interceptor 知道註冊頁的顯示細節；`isRefresh` 是 interceptor 自己的換發流程，性質不同。
- 被否決：toast 和頁內訊息並存。同一個錯誤出現兩次，而且一則英文、一則中文。
- 影響：AGENTS.md「錯誤由 `errorInterceptor` 顯示 toast」這條慣例會多一個明確的例外機制，要一起更新，避免之後有人在元件裡自己處理錯誤卻沒有關掉 toast。

頁內訊息的內容（通用，不透露真實原因，與後端規則 6 一致）：

> 邀請碼無效或已失效。請確認 email 與收到邀請的信箱相同；邀請只能使用一次，且有使用期限。仍無法註冊時，請向邀請你的人索取新的邀請。

### 四、登入頁保留「有邀請碼？前往註冊」連結

維運指令同時輸出碼和連結，被邀請者可能只拿到碼。註冊頁即使被陌生人看到，也需要 100 bit 的碼才能註冊，猜測的風險不會因為多一個連結而提高。連結沿用現有切換按鈕的觸控尺寸（至少 44px）。

### 五、註冊成功後導向 `/`，不讀 `returnUrl`

新帳號不會有「登入前想去的頁面」。註冊頁也不理會 `returnUrl`，避免有人把註冊連結做成開放轉址。

## 對既有程式的影響

| 位置 | 影響 |
|---|---|
| `web/src/app/core/error.interceptor.ts` | 新增 `SUPPRESS_ERROR_TOAST`；目前沒有測試，新增 `error.interceptor.spec.ts` |
| `web/src/app/core/models.ts` | 新增 `RegisterRequest` |
| `web/src/app/core/auth.service.ts` | `register(request: RegisterRequest)`，改用物件參數（四個字串參數容易傳錯順序），並設定 `SUPPRESS_ERROR_TOAST` |
| `web/src/app/features/auth/login.component.ts`／`.spec.ts` | 移除註冊模式；`.login__toggle` 改為指向 `/register` 的 `.login__link` |
| `web/src/app/features/auth/register.component.ts`／`.spec.ts` | 新增 |
| `web/src/app/app.routes.ts` | 新增 `register`（必須放在 `**` 之前） |
| `auth.interceptor.ts` | 不用改：`/auth/register` 已在 `AUTH_ENDPOINTS` 內，不附 token、401 不換發 |
| `app.ts` | 不用改：未登入時本來就隱藏導覽列、套用 `shell--public` |
| 文件 | `AGENTS.md` 前端慣例、`docs/architecture/16-module-web.md` 路由表、`90-tech-debt.md` I2 標為完全解決、`docs/deployment/production-operations.md` 補「發邀請」維運步驟 |

## 明確排除

- 已登入的使用者開啟 `/register`：不另加 guard。註冊成功會覆蓋目前的 session，這和在同一個瀏覽器登入另一個帳號的結果相同；真正的問題是 W1（跨分頁 session 同步），不在這裡處理。
- 其他頁面的錯誤改成頁內顯示：`SUPPRESS_ERROR_TOAST` 只套用在註冊上。
- I1 速率限制、寄送邀請信、email 所有權驗證。
- 正式環境的第一份邀請：部署後由維運者照 `production-operations.md` 執行。
