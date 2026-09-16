# 品類欄位改名：設計文件

- 日期：2026-09-15
- 決策紀錄：[ADR-0012](../../adr/0012-field-key-is-identity-rename-is-explicit.md)（「為什麼這樣設計」全部在那裡，本文件不重複）
- 語彙：`CONTEXT.md` 的 **欄位鍵 / 改名 / 未宣告屬性 / 來源欄位**
- 計畫：[後端](../plans/2026-09-15-category-field-rename-backend.md)、[前端](../plans/2026-09-15-category-field-rename-frontend.md)

## 1. 目標

1. 使用者能把自訂品類的某個欄位鍵改成另一個，該品類下所有品項的對應屬性一併搬移，且全做或全不做。
2. 來源欄位（Steam / IGDB / PSN 的定義 + `platform`）不能被改名、也不能從宣告中撤回。
3. 仍有品項的品類不能被刪除。
4. 撤回宣告不刪品項上的值（現況即是如此，本次只是把它變成明文規則，不加清理）。

## 2. 前提查證（2026-09-15，對照 `5b7926d`）

| 前提 | 結果 |
|---|---|
| `CategoryField` 只有 `Key`，無識別碼 | ✅ `Category.cs:37` |
| `UpdateCategoryCommandHandler` 整包置換 `Fields`，不讀舊值比對 | ✅ `CategoryCommands.cs:139` |
| `DeleteCategoryCommandHandler` 不碰 items | ✅ |
| 系統品類不可改：`MongoCategoryRepository.UpdateAsync` 擲 `ForbiddenException`；`DeleteAsync` 以 `OwnerId = user` 過濾 → 系統品類回 404 | ✅ |
| 前端既有欄位的 key 為可編輯 `ngModel` | ✅ `categories.component.ts:90` |
| dev / prod 都連 Atlas（replica set），本機 compose 無自建 Mongo | ✅ `docker-compose.yml` 只有 `Mongo__ConnectionString` |
| Testcontainers.MongoDb 4.15.0 有 `WithReplicaSet(string)` | ✅ nuget xml |
| MongoDB.Driver 3.11.1，`IClientSessionHandle.WithTransactionAsync` 可用 | ✅ |
| `GlobalExceptionHandler`：FluentValidation `ValidationException` → 400 + `errors`；`ConflictException` → 409 + `detail` | ✅ `GlobalExceptionHandler.cs:60-76` |
| 前端 `error.interceptor.ts` 會把 `errors` 攤平、否則顯示 `detail` | ✅ 第 39-46 行；前端不需為 400/409 另寫處理 |
| BSON 命名為 camelCase（`fields.$[f].key`） | ✅ `MongoConventions.cs:32` |
| Docker 於本 session 未啟動 | ⚠️ 後端基準線由執行者第一步補填 |

## 3. 後端

### 3.1 新增

| 元件 | 層 | 責任 |
|---|---|---|
| `IProtectedFieldKeys` | Application/Categories | `bool IsProtected(string key)`。鎖定集合的查詢介面 |
| `ProviderFieldKeyCatalog` | Infrastructure/Providers | 靜態聯集：`SteamFields.All` ∪ `IgdbFields.All` ∪ `PsnFields.All` 的 key ∪ `"platform"`。singleton，不看 `ProviderRegistry` |
| `RenameCategoryFieldCommand(CategoryId, Key, NewKey)` → `RenameFieldResultDto(Category, MovedItemCount)` | Application/Categories | 驗證 + 前置檢查，交給 renamer |
| `ICategoryFieldRenamer.RenameAsync(categoryId, oldKey, newKey, updatedAt, ct) : Task<long>` | Application/Categories | 「全做或全不做」的承諾；不暴露 session |
| `MongoCategoryFieldRenamer` | Infrastructure/Mongo | 一個 transaction：衝突計數 → items `$rename` → category `fields.$[f].key` |
| `IItemRepository.CountByCategoryAsync(categoryId, ct)` | Application/Items | owner-scoped 計數 |
| `POST /categories/{id}/fields/{key}/rename` body `{ newKey }` | Api | 200 `RenameFieldResultDto` |

### 3.2 修改

| 元件 | 改動 |
|---|---|
| `UpdateCategoryCommandHandler` | 注入 `IProtectedFieldKeys`；`withdrawn = existing.Fields.Keys − request.Keys`，任一受保護 → `ValidationException`（400，`Fields`） |
| `DeleteCategoryCommandHandler` | 注入 `ICategoryRepository.GetAsync` 走存在/擁有權檢查、`IItemRepository.CountByCategoryAsync`；count > 0 → `ConflictException`（409，附數量） |
| `MongoFixture` | `.WithReplicaSet("rs0")` |
| `DependencyInjection.cs` | 註冊 catalog（singleton）與 renamer（scoped） |

### 3.3 錯誤語意

| 情境 | 例外 | HTTP |
|---|---|---|
| 品類不存在／不可見 | `NotFoundException("Category")` | 404 |
| 系統品類 | `ForbiddenException` | 403 |
| 舊鍵未宣告 | `NotFoundException("CategoryField")` | 404 |
| 舊鍵受保護（改名）／撤回受保護鍵（PUT） | `ValidationException` | 400 |
| 新鍵已宣告／新鍵格式錯／新鍵 == 舊鍵 | `ValidationException` | 400 |
| 品項同時帶舊鍵與新鍵 | `ConflictException`（附品項數） | 409 |
| 刪除仍有品項的品類 | `ConflictException`（附品項數） | 409 |

### 3.4 明確排除

- 改名同時改型別。
- 改名的新鍵若命中受保護集合（例如把 `foo` 改成 `steamAppId`）——目前 PUT 也能以任意型別宣告 `steamAppId`，這是既有行為，本次不新增規則。
- 未宣告屬性的清理或診斷工具。
- 通用 `IUnitOfWork`。
- `CategoryDto.itemCount`。
- 08-07 之前產生的孤兒值救援。

## 4. 前端

### 4.1 行為

- 編輯既有品類時，**原本就存在的欄位**其 key 輸入框唯讀（`readonly`，不是 `disabled`——值仍要送出）；本次 dialog 內新加的欄位 key 可編輯。
- 每個既有欄位旁有「重新命名」按鈕。按下後同一列展開一個 inline 輸入（不用 `window.prompt`，瀏覽器 modal 會卡住自動化且無法客製訊息），輸入新鍵 → 確認即呼叫 `POST …/rename`，**不等表單儲存**。
- 成功：通知「已將 `old` 改名為 `new`，搬移 N 筆品項的屬性」，更新 draft 內該欄位的 key 與「原始鍵集合」，重新載入品類列表。
- 失敗：交給 interceptor（400 的 `errors`、409 的 `detail`），元件不另寫分支。
- 改名進行中納入 `busy`，與儲存／刪除互斥。
- 刪除品類收到 409：interceptor 顯示 `detail`，元件不預判。

### 4.2 新增／修改

| 檔案 | 改動 |
|---|---|
| `core/models.ts` | `RenameFieldResultDto { category: CategoryDto; movedItemCount: number }` |
| `core/api/category.service.ts` | `renameField(id, key, newKey): Observable<RenameFieldResultDto>` |
| `features/categories/categories.component.ts` | `originalKeys` signal、`renaming` 狀態、唯讀 key、inline 改名列、`busy` 納入 renaming |
| 對應 `.spec.ts` | 見前端計畫 |

### 4.3 明確排除

- 前端預判哪些鍵受保護（沒有端點提供集合；靠 400）。
- 改名後修正網址／返回點裡的舊鍵（ADR-0012 後果段）。
- 批次改名、undo。
