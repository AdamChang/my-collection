# 邀請制註冊（後端）設計

- 日期：2026-09-28
- 範圍：後端（`src/`、`tests/`）。前端另寫一份規格與計畫，見文末。
- 決策來源：[ADR-0013](../../adr/0013-invitation-only-issued-by-maintenance-command.md)、`CONTEXT.md`「帳號」一節、tech debt I2、Q12

## 目標

`/auth/register` 只接受持有有效**邀請**的人註冊。**邀請**只能透過維運指令發出。系統不會新增管理者角色，任何環境都沒有開放註冊的開關。

## 已定案的規則（grilling 結論）

| # | 規則 |
|---|---|
| 1 | 只有維運者能發邀請，用 `maintenance invite create\|revoke\|list` |
| 2 | 邀請綁定 email，註冊時的 email 必須相符（比對前 Trim 並轉小寫） |
| 3 | 單次使用；`--days` 預設 7 天；未使用前可撤銷；已使用的保留並記下 `usedByUserId` |
| 4 | 同一個 email 同時只能有一份有效邀請：發新的會自動撤銷舊的未使用邀請。已註冊的 email 拒絕發出 |
| 5 | 邀請碼**存明文**，格式為 Crockford base32、20 字元（100 bit），顯示成 `XXXXX-XXXXX-XXXXX-XXXXX`。比對時不分大小寫、忽略連字號與空白 |
| 6 | 註冊失敗時，碼不存在、已過期、已使用、已撤銷、email 不符都回同一個 403 訊息；真實原因只記在 log |
| 7 | 兌換邀請和建立 User 在同一個 transaction 裡完成（Q12 改選 (c)） |
| 8 | 被邀請者與其他使用者權限對等，不引入角色 |
| 9 | 既有帳號不回補邀請紀錄 |

## 架構

依照「Endpoint → MediatR command/query + validator → Application 介面 → Infrastructure 實作」的慣例。

```
maintenance invite create ─┐
maintenance invite revoke ─┼─► ISender ─► Create/Revoke/ListInvitation handlers ─► IInvitationRepository ─► MongoInvitationRepository
maintenance invite list ───┘                                                     └► IUserRepository（拒發已註冊 email）

POST /auth/register ─► RegisterCommandHandler ─► IInvitationRedeemer ─► MongoInvitationRedeemer
                                                                          └ transaction：消耗邀請 + 插入 User
```

### Domain

`Domain/Entities/Invitation.cs`：

| 欄位 | 說明 |
|---|---|
| `Id` | ObjectId |
| `Email` | 已正規化（Trim、小寫） |
| `Code` | 正規化後的 20 字元（不含連字號），明文 |
| `Status` | `InvitationStatus { Pending, Used, Revoked }`，存成字串 |
| `CreatedAt`／`ExpiresAt` | UTC |
| `UsedAt`／`UsedByUserId`／`RevokedAt` | 可為 null |

「已過期」**不**儲存，一律由 `Status == Pending ∧ ExpiresAt <= now` 推導。如果把過期存成狀態，就需要一個排程去改它，而系統沒有排程機制；推導的結果不會和現實不一致。

### Application（`Application/Auth/`）

- `InvitationCode`（static）：`Generate()`、`Normalise(string)`、`Format(string)`。產生碼用 `RandomNumberGenerator`。`Normalise` 依 Crockford 的解碼規則：轉大寫、去掉 `-` 和空白，並把 `I`、`L` 視為 `1`、`O` 視為 `0`。這樣被邀請者手動輸入時打錯易混淆的字元也能通過，而碼本身從不產生這些字元。
- `IInvitationRepository`：`InsertAsync`、`RevokePendingAsync(email, now)`（回傳撤銷筆數）、`ListAsync`。
- `IInvitationRedeemer.RedeemAsync(string normalisedCode, User newUser, DateTime now, ct)`：成功時完成兩筆寫入；失敗時拋 `ForbiddenException`（通用訊息）或 `ConflictException`（email 已被註冊，只會在競態下發生）。
- `InvitationCommands.cs`：`CreateInvitationCommand(Email, Days)`、`RevokeInvitationCommand(Email)`、`ListInvitationsQuery`，以及各自的 validator 和 `InvitationDto` record。`InvitationDto` 帶有推導後的 `Status` 字串（`Pending`、`Expired`、`Used`、`Revoked`）。
- `RegisterCommand` 新增 `InvitationCode` 欄位，validator 要求非空。handler 改為先產生 `User`（Id 在用戶端產生），再呼叫 `IInvitationRedeemer`。

用 MediatR 串起維運指令，是為了重用 `ValidationBehavior` 和 handler 的單元測試結構，維運程式碼不必自己驗證輸入。

### Infrastructure（`Infrastructure/Mongo/`）

- `MongoContext.Invitations` → collection `invitations`。
- `MongoIndexInitializer` 新增兩個索引：
  - `ux_invitations_code`：`code` 唯一。
  - `ux_invitations_email_pending`：`email` 唯一，partial 條件是 `status: "Pending"`。這是規則 4 在資料庫端的保證：`RevokePendingAsync` 加上 `InsertAsync` 兩個動作不是原子操作，兩個維運指令同時執行時，由這個索引把第二個轉成 `ConflictException`。
- `MongoInvitationRepository`。
- `MongoInvitationRedeemer`：沿用 `MongoCategoryFieldRenamer` 的模式，在一個 `WithTransactionAsync` 裡完成以下步驟：
  1. `UpdateOne`，filter 為 `code ∧ email ∧ status=Pending ∧ expiresAt > now`，`$set` 的內容是 `status=Used, usedAt, usedByUserId=newUser.Id`。User 的 Id 已事先產生，所以「回寫 `usedByUserId`」可以併進同一個更新。
  2. `MatchedCount == 0` 時，用 code 查一次，把真實原因記成 `LogWarning`（unknown、expired、used、revoked、email mismatch），然後拋出通用的 `ForbiddenException`。
  3. `Users.InsertOne(session, newUser)`。遇到 DuplicateKey 時拋 `ConflictException`，transaction 回滾，邀請保持 Pending。

### Api

- `AuthEndpoints` 不用改：`RegisterCommand` 多一個欄位，minimal API 的 binding 會自動帶入。
- `Program.cs` 恢復 M2 的 `maintenance` 分支（`6afd6de` 的寫法）：`CreateBuilder(isMaintenance ? [] : args)`。`Build()` 之後先執行 `EnsureIndexesAsync`，因為全新的資料庫需要 partial unique 索引，再交給 `Maintenance/InviteMaintenance.RunAsync(args, services)` 處理，最後回傳 exit code。
- `InviteMaintenance`：負責解析參數（`create --email <e> [--days <n>]`、`revoke --email <e>`、`list`）並格式化輸出。`create` 輸出格式化的碼和註冊連結 `{origin}/register?code={formatted}`，其中 `origin` 取自 `Cors:AllowedOrigins:0`，也就是 SPA 的來源；沒有設定時只輸出相對路徑。參數錯誤時回 exit code 2 並印出用法。

`origin` 沒有另外新增設定鍵，理由是維運指令在本機用 user secrets 連正式環境執行，這個鍵本來就已經設好了。如果之後 CORS 的來源和對外網址分開，再拆出獨立的設定鍵。

## 對既有程式的影響

| 位置 | 影響 |
|---|---|
| `tests/.../Fixtures/AuthenticatedClient.cs` | 目前直接呼叫 `/auth/register`，所有需要登入的整合測試都經過這裡。改成先透過 `factory.Services` 送出 `CreateInvitationCommand` 取得碼，再帶碼註冊 |
| `tests/.../Fixtures/MongoFixture.ResetAsync` | 加入清空 `Invitations` |
| `AuthEndpointsTests` | 原本 7 處直接註冊，全部改成帶邀請碼 |
| `RegisterCommandTests` | 從 mock `IUserRepository.InsertAsync` 改為 mock `IInvitationRedeemer` |
| 前端 `auth.service.ts` | 本計畫合併後，前端的註冊會因缺少 `invitationCode` 而收到 400。**後端和前端必須一起部署** |

## 被否決的替代方案

- **白名單 email**（I2 的另一個建議方向）：要事先登記 email，其實就是一份不需要碼、不會過期的邀請。沒有碼的話，只要知道 email 就能註冊，而系統不驗證 email 的所有權。
- **跨請求的兩步寫入，不用 transaction**：見 ADR-0013 §二。
- **存「已過期」狀態**：見上文 Domain 段落。
- **新增 `InvitationException` 型別**：`ForbiddenException` 已經對應到 403，`LoginCommandHandler` 也用它來回避帳號列舉，語意相同，所以不新增型別，也不用改 `GlobalExceptionHandler`。

## 明確排除

- 前端：`/register` 路由、登入頁移除註冊切換、`?code=` 自動帶入、`models.ts` 的型別。這部分另寫規格與計畫，下一個 session 處理。
- I1 速率限制。
- 寄送邀請信、email 所有權驗證。
- 回補既有帳號的邀請紀錄。
