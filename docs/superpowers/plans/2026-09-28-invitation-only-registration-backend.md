# 邀請制註冊（後端）實作計畫

> For agentic workers: REQUIRED SUB-SKILL — 執行時使用 superpowers:test-driven-development + superpowers:subagent-driven-development。每個 Task 嚴格照 Step 1→6 執行，Step 2 不可省。

**Goal**：`/auth/register` 必須帶有效的邀請碼才能成功；邀請只能用 `maintenance invite create|revoke|list` 發出（ADR-0013、tech debt I2）。

**Architecture**：新增 `Invitation` entity 和 `invitations` collection。維運指令經 MediatR 呼叫 `Create/Revoke/ListInvitation` handler，再交給 `IInvitationRepository`。註冊時由 `IInvitationRedeemer`（Mongo 實作）在同一個 transaction 裡消耗邀請並插入 User，沿用 `MongoCategoryFieldRenamer` 的模式。

**Tech Stack**：.NET 10、MediatR、FluentValidation、MongoDB.Driver、xUnit + FluentAssertions + Moq + Testcontainers（replica set）。

- 設計文件：`docs/superpowers/specs/2026-09-28-invitation-only-registration-design.md`
- ADR：`docs/adr/0013-invitation-only-issued-by-maintenance-command.md`
- 前端計畫：尚未撰寫（見「後續」）

## 執行前必讀

### 環境

- 路徑：`F:\VibeCode\MyCollection`
- 分支：`feat/invitation-registration`（**不是 master**，Task 0 會建立）
- 全部測試：`dotnet test MyCollection.slnx`
- 單檔測試：`dotnet test MyCollection.slnx --filter "FullyQualifiedName~<TestClassName>"`
- 整合測試需要 Docker（Testcontainers `mongo:8.0` replica set）。Docker 沒開時整合測試會失敗，這時停下來請使用者開啟，不要跳過。

### 基準線（2026-09-28 在 `4d17a73` 親自執行）

- 後端：**627 passed**，0 failed，0 skipped
- build：0 warnings

任何時候數字低於基準線就是弄壞了東西。

### 絕對不要碰的檔案

- 前端 `web/**`：本計畫合併後，前端的註冊會回 400，這是預期中的結果，由前端計畫修正。
- 每個 Task 一律用明確路徑 `git add <path>`，**禁止 `git add .`／`git add -A`**。

### 慣例

- 註解與 commit message 用繁體中文，識別字用英文。註解寫「為什麼」。
- Command、validator、DTO 放在同一檔（例：`Application/Auth/InvitationCommands.cs`），DTO 用 `record`，mapping 手寫。
- 不在各層寫 try-catch。唯一的例外是把 Mongo 的 DuplicateKey 轉成 `ConflictException`（`MongoUserRepository` 已有前例），以及 CLI 入口 `InviteMaintenance` 當作 CLI 的全域例外處理。
- `DateTime` 寫入前必須是 `Kind=Utc`。
- 新索引加在 `MongoIndexInitializer`，並且必須冪等。
- 測試分層：純邏輯與 handler 寫在 `tests/MyCollection.Tests/Unit`（Moq），Mongo 行為寫在 `Integration`（`[Collection(MongoCollection.Name)]` + `fixture.ResetAsync()`）。
- TDD 順序：先寫失敗測試，確認它以**正確的理由**失敗，再寫最小實作。一個 Task 一個 commit。
- `invitations` 是系統層級的資料，沒有 `OwnerId`，所以不經過 `IUserContext` 的 owner filter。這不違反 AGENTS.md 的授權規則，因為這些資料只有匿名註冊和維運指令兩個入口，不存在「別人的邀請」。

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `src/MyCollection.Domain/Entities/Invitation.cs` | `Invitation` entity、`InvitationStatus` enum |
| `src/MyCollection.Application/Auth/InvitationCode.cs` | 產生、正規化、格式化邀請碼 |
| `src/MyCollection.Application/Auth/IInvitationRepository.cs` | 邀請的持久化介面 |
| `src/MyCollection.Application/Auth/IInvitationRedeemer.cs` | 「消耗邀請 + 建立 User」的原子操作介面 |
| `src/MyCollection.Application/Auth/InvitationCommands.cs` | Create/Revoke/List 的 command、query、validator、handler、`InvitationDto`、`InvitationMapper` |
| `src/MyCollection.Infrastructure/Mongo/MongoInvitationRepository.cs` | 實作 `IInvitationRepository` |
| `src/MyCollection.Infrastructure/Mongo/MongoInvitationRedeemer.cs` | 實作 `IInvitationRedeemer`（transaction） |
| `src/MyCollection.Api/Maintenance/InviteMaintenance.cs` | CLI 參數解析與輸出 |
| `tests/MyCollection.Tests/Unit/InvitationCodeTests.cs` | |
| `tests/MyCollection.Tests/Unit/InvitationCommandTests.cs` | |
| `tests/MyCollection.Tests/Unit/InviteMaintenanceTests.cs` | |
| `tests/MyCollection.Tests/Integration/MongoInvitationRepositoryTests.cs` | 包含索引行為 |
| `tests/MyCollection.Tests/Integration/MongoInvitationRedeemerTests.cs` | |

### 修改

| 檔案 | 改動 |
|---|---|
| `src/MyCollection.Infrastructure/Mongo/MongoContext.cs` | 新增 `Invitations` |
| `src/MyCollection.Infrastructure/Mongo/MongoIndexInitializer.cs` | 新增 `ux_invitations_code`、`ux_invitations_email_pending` |
| `src/MyCollection.Infrastructure/DependencyInjection.cs` | 註冊 repository 與 redeemer |
| `src/MyCollection.Application/Auth/RegisterCommand.cs` | 新增 `InvitationCode`；handler 改用 `IInvitationRedeemer` |
| `src/MyCollection.Api/Program.cs` | `maintenance` 分支 |
| `tests/MyCollection.Tests/Fixtures/MongoFixture.cs` | `ResetAsync` 清空 `Invitations` |
| `tests/MyCollection.Tests/Fixtures/AuthenticatedClient.cs` | 先發邀請再註冊；新增 `CreateInvitationAsync` |
| `tests/MyCollection.Tests/Unit/RegisterCommandTests.cs` | 改為 mock `IInvitationRedeemer` |
| `tests/MyCollection.Tests/Integration/AuthEndpointsTests.cs` | 註冊一律帶邀請碼；新增 400／403 案例 |
| `docs/architecture/90-tech-debt.md`、`docs/architecture/01-overview.md`、`AGENTS.md` | 標記 I2 已解決、加入 ADR-0013 索引、高風險區域移除 I2 |

### Task 相依順序

```
Task 0（分支 + 設計文件 commit）
  │
Task 1 InvitationCode
  │
Task 2 Invitation entity + 索引 ─────────┐
  │                                      │
Task 3 MongoInvitationRepository         │
  │                                      │
Task 4 Create/Revoke/List handlers    Task 5 MongoInvitationRedeemer   ← 4、5 可並行
  │           │                          │
  │           └──────────┬───────────────┘
  │                   Task 6 RegisterCommand + 測試 fixture（行為切換點）
Task 7 維運指令 CLI（只依賴 4）
  │
Task 8 文件（最後）

Checkpoint A：Task 3 之後（只有新增，沒有改變既有行為）
Checkpoint B：Task 6 之後（註冊已經需要邀請碼，測試全綠）
Checkpoint C：Task 8 之後（完成）
```

## Task 0：建立分支並 commit 設計文件

**Files:** 無程式碼

- [ ] `git switch -c feat/invitation-registration`
- [ ] `git add CONTEXT.md docs/adr/0013-invitation-only-issued-by-maintenance-command.md docs/superpowers/specs/2026-09-28-invitation-only-registration-design.md docs/superpowers/plans/2026-09-28-invitation-only-registration-backend.md`
- [ ] Commit：

```
docs(auth): 邀請制註冊的領域詞彙、ADR-0013 與後端設計、計畫

- CONTEXT.md 新增「帳號」：邀請、邀請碼
- ADR-0013：只由維運指令發出、綁定 email、兌換與建帳號同一 transaction、邀請碼存明文

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Task 1：InvitationCode

**Files:** Create: `src/MyCollection.Application/Auth/InvitationCode.cs`、`tests/MyCollection.Tests/Unit/InvitationCodeTests.cs`

邀請碼的強度是 I1 未解決前唯一的防暴力猜測防線，所以要驗證長度與字元集。`Normalise` 容易錯的地方：Crockford 解碼把 `I`／`L` 視為 `1`、`O` 視為 `0`，必須在**轉成大寫之後**才替換，否則小寫的 `l`、`o` 會漏掉。

- [ ] **Step 1：寫失敗測試**

```csharp
using FluentAssertions;
using MyCollection.Application.Auth;

namespace MyCollection.Tests.Unit;

public class InvitationCodeTests
{
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    [Fact]
    public void Generate_returns_20_crockford_characters()
    {
        var code = InvitationCode.Generate();

        code.Should().HaveLength(InvitationCode.Length);
        code.Should().Match(c => c.All(Crockford.Contains));
    }

    [Fact]
    public void Generate_does_not_repeat()
    {
        var codes = Enumerable.Range(0, 1000).Select(_ => InvitationCode.Generate());

        codes.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("abcde-fghjk-mnpqr-stvwx", "ABCDEFGHJKMNPQRSTVWX")]
    [InlineData("  ABCDE FGHJK\tMNPQR-STVWX ", "ABCDEFGHJKMNPQRSTVWX")]
    [InlineData("iIlL-oO", "111100")]
    [InlineData("", "")]
    public void Normalise_uppercases_strips_separators_and_maps_ambiguous_characters(string input, string expected)
    {
        InvitationCode.Normalise(input).Should().Be(expected);
    }

    [Fact]
    public void Format_groups_by_five()
    {
        InvitationCode.Format("ABCDEFGHJKMNPQRSTVWX").Should().Be("ABCDE-FGHJK-MNPQR-STVWX");
    }

    [Fact]
    public void Normalise_reverses_Format()
    {
        var code = InvitationCode.Generate();

        InvitationCode.Normalise(InvitationCode.Format(code)).Should().Be(code);
    }
}
```

- [ ] **Step 2：跑測試確認失敗**：`--filter "FullyQualifiedName~InvitationCodeTests"`。預期是編譯錯誤，錯誤訊息要是 `InvitationCode` 不存在，不能是其他錯誤。
- [ ] **Step 3：最小實作**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace MyCollection.Application.Auth;

/// <summary>
/// 邀請碼：Crockford base32、20 字元（100 bit）。
/// I1（認證端點沒有速率限制）解決之前，碼的強度是防暴力猜測的唯一防線，不可縮短（ADR-0013 §三）。
/// </summary>
public static class InvitationCode
{
    public const int Length = 20;
    private const int GroupSize = 5;

    // Crockford 字母表刻意排除 I、L、O、U，避免手動輸入時混淆
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>
    /// 依 Crockford 解碼規則正規化：不分大小寫、忽略連字號與空白，I／L 讀作 1、O 讀作 0。
    /// 產生的碼不含這些字元，所以這個對應只會救回打錯的輸入，不會造成碰撞。
    /// </summary>
    public static string Normalise(string input)
    {
        var builder = new StringBuilder(input.Length);

        foreach (var c in input.ToUpperInvariant())
        {
            if (c == '-' || char.IsWhiteSpace(c))
            {
                continue;
            }

            builder.Append(c switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                _ => c
            });
        }

        return builder.ToString();
    }

    public static string Format(string normalised) =>
        string.Join('-', normalised.Chunk(GroupSize).Select(chunk => new string(chunk)));
}
```

- [ ] **Step 4：跑單檔測試**：Expected `8 passed`
- [ ] **Step 5：跑全部測試**：Expected `635 passed`
- [ ] **Step 6：Commit**

```
git add src/MyCollection.Application/Auth/InvitationCode.cs tests/MyCollection.Tests/Unit/InvitationCodeTests.cs
```

```
feat(auth): 新增邀請碼的產生、正規化與格式化

Crockford base32 20 字元（100 bit）；正規化不分大小寫、忽略連字號與空白，I／L→1、O→0。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Task 2：Invitation entity、collection 與索引

**Files:** Create: `src/MyCollection.Domain/Entities/Invitation.cs`、`tests/MyCollection.Tests/Integration/MongoInvitationRepositoryTests.cs`（本 Task 先放索引測試，Task 3 再擴充）。Modify: `MongoContext.cs`、`MongoIndexInitializer.cs`、`tests/.../Fixtures/MongoFixture.cs`

partial unique 索引 `ux_invitations_email_pending` 是「同一個 email 同時只有一份有效邀請」在資料庫端的保證。容易錯的地方是 partial filter 裡的 enum 必須序列化成字串 `"Pending"`；如果序列化成數字，索引永遠不會命中，測試會以「插入成功」的方式失敗。

- [ ] **Step 1：寫失敗測試**

```csharp
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Domain.Entities;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoInvitationRepositoryTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    internal static Invitation NewInvitation(
        string email = "friend@example.com",
        string? code = null,
        InvitationStatus status = InvitationStatus.Pending,
        DateTime? createdAt = null) => new()
    {
        Id = ObjectId.GenerateNewId(),
        Email = email,
        Code = code ?? Application.Auth.InvitationCode.Generate(),
        Status = status,
        CreatedAt = createdAt ?? Now,
        ExpiresAt = (createdAt ?? Now).AddDays(7)
    };

    [Fact]
    public async Task Code_is_unique()
    {
        await fixture.Context.Invitations.InsertOneAsync(NewInvitation(email: "a@example.com", code: "SAMECODE"));

        var act = () => fixture.Context.Invitations.InsertOneAsync(NewInvitation(email: "b@example.com", code: "SAMECODE"));

        (await act.Should().ThrowAsync<MongoWriteException>())
            .Which.WriteError.Category.Should().Be(ServerErrorCategory.DuplicateKey);
    }

    [Fact]
    public async Task Only_one_pending_invitation_per_email()
    {
        await fixture.Context.Invitations.InsertOneAsync(NewInvitation());

        var act = () => fixture.Context.Invitations.InsertOneAsync(NewInvitation());

        (await act.Should().ThrowAsync<MongoWriteException>())
            .Which.WriteError.Category.Should().Be(ServerErrorCategory.DuplicateKey);
    }

    [Fact]
    public async Task Revoked_and_used_invitations_do_not_block_a_new_pending_one()
    {
        await fixture.Context.Invitations.InsertOneAsync(NewInvitation(status: InvitationStatus.Revoked));
        await fixture.Context.Invitations.InsertOneAsync(NewInvitation(status: InvitationStatus.Used));

        var act = () => fixture.Context.Invitations.InsertOneAsync(NewInvitation());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Status_is_stored_as_string()
    {
        var invitation = NewInvitation();
        await fixture.Context.Invitations.InsertOneAsync(invitation);

        var raw = await fixture.Context.Database.GetCollection<BsonDocument>("invitations")
            .Find(new BsonDocument("_id", invitation.Id)).SingleAsync();

        raw["status"].Should().Be(new BsonString("Pending"));
    }
}
```

- [ ] **Step 2：跑測試確認失敗**：預期是編譯錯誤，因為 `Invitation` 和 `Invitations` 都不存在。
- [ ] **Step 3：最小實作**

`Domain/Entities/Invitation.cs`：

```csharp
using MongoDB.Bson;

namespace MyCollection.Domain.Entities;

/// <summary>
/// 發給某個 email 的一次性註冊資格（CONTEXT.md「邀請」）。
/// 「已過期」不存成狀態，而是由 Pending 加上 ExpiresAt 推導：系統沒有排程可以去改狀態，推導出來的結果才不會和現實不一致。
/// </summary>
public sealed class Invitation
{
    public ObjectId Id { get; set; }

    /// <summary>已正規化（Trim、小寫）。</summary>
    public required string Email { get; set; }

    /// <summary>正規化後的邀請碼，明文存放（ADR-0013 §三）。</summary>
    public required string Code { get; set; }

    public InvitationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public ObjectId? UsedByUserId { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public enum InvitationStatus
{
    Pending,
    Used,
    Revoked
}
```

`MongoContext.cs` 加上：

```csharp
    public IMongoCollection<Invitation> Invitations => Database.GetCollection<Invitation>("invitations");
```

`MongoIndexInitializer.EnsureIndexesAsync` 最後加上：

```csharp
        await context.Invitations.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Invitation>(
                    Builders<Invitation>.IndexKeys.Ascending(x => x.Code),
                    new CreateIndexOptions { Name = "ux_invitations_code", Unique = true }),

                // 「同一 email 同時只有一份有效邀請」的資料庫端保證：
                // 撤銷舊邀請與插入新邀請是兩個動作，兩個維運指令同時執行時，由這個索引擋下第二個
                new CreateIndexModel<Invitation>(
                    Builders<Invitation>.IndexKeys.Ascending(x => x.Email),
                    new CreateIndexOptions<Invitation>
                    {
                        Name = "ux_invitations_email_pending",
                        Unique = true,
                        PartialFilterExpression = Builders<Invitation>.Filter.Eq(x => x.Status, InvitationStatus.Pending)
                    })
            ],
            cancellationToken: ct);
```

`MongoFixture.ResetAsync` 加上：

```csharp
        await Context.Invitations.DeleteManyAsync(FilterDefinition<Invitation>.Empty);
```

- [ ] **Step 4：跑單檔測試**：Expected `4 passed`
- [ ] **Step 5：跑全部測試**：Expected `639 passed`
- [ ] **Step 6：Commit**

```
git add src/MyCollection.Domain/Entities/Invitation.cs src/MyCollection.Infrastructure/Mongo/MongoContext.cs src/MyCollection.Infrastructure/Mongo/MongoIndexInitializer.cs tests/MyCollection.Tests/Fixtures/MongoFixture.cs tests/MyCollection.Tests/Integration/MongoInvitationRepositoryTests.cs
```

```
feat(auth): 新增 Invitation entity 與 invitations collection 的索引

code 唯一；email 以 status=Pending 為條件的 partial unique，確保同一 email 同時只有一份有效邀請。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Task 3：MongoInvitationRepository

**Files:** Create: `src/MyCollection.Application/Auth/IInvitationRepository.cs`、`src/MyCollection.Infrastructure/Mongo/MongoInvitationRepository.cs`。Modify: `MongoInvitationRepositoryTests.cs`、`Infrastructure/DependencyInjection.cs`

容易錯的地方是 `RevokePendingAsync` 只能撤銷**該 email 的 Pending** 邀請；如果誤撤了已使用的邀請，就會抹掉「這個帳號是怎麼進來的」紀錄。

- [ ] **Step 1：寫失敗測試**（加進 `MongoInvitationRepositoryTests`）

```csharp
    private MongoInvitationRepository CreateSut() => new(fixture.Context);

    [Fact]
    public async Task Insert_second_pending_for_same_email_throws_conflict()
    {
        await CreateSut().InsertAsync(NewInvitation(), CancellationToken.None);

        var act = () => CreateSut().InsertAsync(NewInvitation(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task RevokePending_revokes_only_pending_invitations_of_that_email()
    {
        var used = NewInvitation(status: InvitationStatus.Used);
        var pending = NewInvitation();
        var other = NewInvitation(email: "other@example.com");
        await fixture.Context.Invitations.InsertManyAsync([used, pending, other]);

        var revoked = await CreateSut().RevokePendingAsync("friend@example.com", Now, CancellationToken.None);

        revoked.Should().Be(1);
        var all = await CreateSut().ListAsync(CancellationToken.None);
        all.Single(x => x.Id == pending.Id).Status.Should().Be(InvitationStatus.Revoked);
        all.Single(x => x.Id == pending.Id).RevokedAt.Should().Be(Now);
        all.Single(x => x.Id == used.Id).Status.Should().Be(InvitationStatus.Used);
        all.Single(x => x.Id == other.Id).Status.Should().Be(InvitationStatus.Pending);
    }

    [Fact]
    public async Task List_returns_newest_first()
    {
        var older = NewInvitation(email: "a@example.com", createdAt: Now.AddDays(-1));
        var newer = NewInvitation(email: "b@example.com", createdAt: Now);
        await fixture.Context.Invitations.InsertManyAsync([older, newer]);

        var list = await CreateSut().ListAsync(CancellationToken.None);

        list.Select(x => x.Id).Should().Equal(newer.Id, older.Id);
    }
```

檔頭補上 `using MyCollection.Domain.Exceptions;`、`using MyCollection.Infrastructure.Mongo;`。

- [ ] **Step 2：跑測試確認失敗**：預期是編譯錯誤，因為 `MongoInvitationRepository` 不存在。
- [ ] **Step 3：最小實作**

```csharp
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Auth;

public interface IInvitationRepository
{
    /// <summary>同一 email 已有 Pending 邀請時擲出 <see cref="Domain.Exceptions.ConflictException"/>。</summary>
    Task InsertAsync(Invitation invitation, CancellationToken ct);

    /// <summary>撤銷該 email 所有 Pending 邀請（含已過期但未處理者），回傳筆數。</summary>
    Task<long> RevokePendingAsync(string email, DateTime revokedAt, CancellationToken ct);

    /// <summary>全部邀請，新的在前。</summary>
    Task<IReadOnlyList<Invitation>> ListAsync(CancellationToken ct);
}
```

```csharp
using MongoDB.Driver;
using MyCollection.Application.Auth;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoInvitationRepository(MongoContext context) : IInvitationRepository
{
    private static readonly FilterDefinitionBuilder<Invitation> Filter = Builders<Invitation>.Filter;

    public async Task InsertAsync(Invitation invitation, CancellationToken ct)
    {
        try
        {
            await context.Invitations.InsertOneAsync(invitation, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ConflictException($"A pending invitation for '{invitation.Email}' already exists.");
        }
    }

    public async Task<long> RevokePendingAsync(string email, DateTime revokedAt, CancellationToken ct)
    {
        var result = await context.Invitations.UpdateManyAsync(
            Filter.And(
                Filter.Eq(x => x.Email, email),
                Filter.Eq(x => x.Status, InvitationStatus.Pending)),
            Builders<Invitation>.Update
                .Set(x => x.Status, InvitationStatus.Revoked)
                .Set(x => x.RevokedAt, revokedAt),
            cancellationToken: ct);

        return result.ModifiedCount;
    }

    public async Task<IReadOnlyList<Invitation>> ListAsync(CancellationToken ct) =>
        await context.Invitations.Find(Filter.Empty)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
}
```

`Infrastructure/DependencyInjection.cs`，在 `IUserRepository` 那一行下方加上：

```csharp
        services.AddScoped<IInvitationRepository, MongoInvitationRepository>();
```

- [ ] **Step 4：跑單檔測試**：Expected `7 passed`
- [ ] **Step 5：跑全部測試**：Expected `642 passed`
- [ ] **Step 6：Commit**

```
git add src/MyCollection.Application/Auth/IInvitationRepository.cs src/MyCollection.Infrastructure/Mongo/MongoInvitationRepository.cs src/MyCollection.Infrastructure/DependencyInjection.cs tests/MyCollection.Tests/Integration/MongoInvitationRepositoryTests.cs
```

```
feat(auth): 新增邀請的 repository（新增、撤銷未使用、列出）

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

**— Checkpoint A：只有新增、沒有改變既有行為，可以在這裡收工 —**

## Task 4：Create／Revoke／List handlers

**Files:** Create: `src/MyCollection.Application/Auth/InvitationCommands.cs`、`tests/MyCollection.Tests/Unit/InvitationCommandTests.cs`

容易錯的地方：
1. 「已註冊的 email 不發邀請」這項檢查必須放在撤銷舊邀請**之前**，否則被拒絕的請求也會作廢一份有效的邀請。
2. `Status` 推導必須用同一個 `now`，DTO 才不會自相矛盾。

- [ ] **Step 1：寫失敗測試**

```csharp
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Auth;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class InvitationCommandTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 3, 0, 0, DateTimeKind.Utc);
    private readonly Mock<IInvitationRepository> _invitations = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Now));

    [Theory]
    [InlineData("", 7)]
    [InlineData("not-an-email", 7)]
    [InlineData("a@b.c", 0)]
    [InlineData("a@b.c", 31)]
    public void Create_validator_rejects_invalid_input(string email, int days)
    {
        new CreateInvitationCommandValidator().Validate(new CreateInvitationCommand(email, days))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Create_rejects_email_that_is_already_registered_without_revoking_anything()
    {
        _users.Setup(u => u.GetByEmailAsync("friend@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Email = "friend@example.com", PasswordHash = "h", DisplayName = "F" });

        var act = () => new CreateInvitationCommandHandler(_invitations.Object, _users.Object, _time)
            .Handle(new CreateInvitationCommand(" Friend@Example.com ", 7), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        _invitations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_revokes_previous_pending_then_inserts_normalised_invitation()
    {
        Invitation? inserted = null;
        var sequence = new MockSequence();
        _invitations.InSequence(sequence)
            .Setup(r => r.RevokePendingAsync("friend@example.com", Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _invitations.InSequence(sequence)
            .Setup(r => r.InsertAsync(It.IsAny<Invitation>(), It.IsAny<CancellationToken>()))
            .Callback<Invitation, CancellationToken>((i, _) => inserted = i)
            .Returns(Task.CompletedTask);

        var dto = await new CreateInvitationCommandHandler(_invitations.Object, _users.Object, _time)
            .Handle(new CreateInvitationCommand(" Friend@Example.com ", 3), CancellationToken.None);

        inserted!.Email.Should().Be("friend@example.com");
        inserted.Code.Should().HaveLength(InvitationCode.Length);
        inserted.Status.Should().Be(InvitationStatus.Pending);
        inserted.ExpiresAt.Should().Be(Now.AddDays(3));
        dto.Code.Should().Be(InvitationCode.Format(inserted.Code));
        dto.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task Revoke_returns_number_of_revoked_invitations()
    {
        _invitations.Setup(r => r.RevokePendingAsync("friend@example.com", Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var revoked = await new RevokeInvitationCommandHandler(_invitations.Object, _time)
            .Handle(new RevokeInvitationCommand("Friend@example.com"), CancellationToken.None);

        revoked.Should().Be(1);
    }

    [Fact]
    public async Task List_derives_expired_from_pending_past_expiry()
    {
        var expired = new Invitation
        {
            Id = ObjectId.GenerateNewId(), Email = "a@example.com", Code = "ABCDEFGHJKMNPQRSTVWX",
            Status = InvitationStatus.Pending, CreatedAt = Now.AddDays(-8), ExpiresAt = Now.AddDays(-1)
        };
        _invitations.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([expired]);

        var list = await new ListInvitationsQueryHandler(_invitations.Object, _time)
            .Handle(new ListInvitationsQuery(), CancellationToken.None);

        list.Single().Status.Should().Be("Expired");
        list.Single().Code.Should().Be("ABCDE-FGHJK-MNPQR-STVWX");
    }
}
```

- [ ] **Step 2：跑測試確認失敗**：預期是編譯錯誤，因為這幾個 command 型別都不存在。
- [ ] **Step 3：最小實作**：`Application/Auth/InvitationCommands.cs`

```csharp
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Auth;

public record InvitationDto(
    string Email,
    string Code,
    string Status,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? UsedAt,
    string? UsedByUserId);

public static class InvitationMapper
{
    /// <summary>「已過期」只存在於讀取端（見 <see cref="Invitation"/> 註解）。</summary>
    public static string StatusOf(Invitation invitation, DateTime now) =>
        invitation.Status == InvitationStatus.Pending && invitation.ExpiresAt <= now
            ? "Expired"
            : invitation.Status.ToString();

    public static InvitationDto ToDto(Invitation invitation, DateTime now) => new(
        invitation.Email,
        InvitationCode.Format(invitation.Code),
        StatusOf(invitation, now),
        invitation.CreatedAt,
        invitation.ExpiresAt,
        invitation.UsedAt,
        invitation.UsedByUserId?.ToString());

    public static string NormaliseEmail(string email) => email.Trim().ToLowerInvariant();
}

public record CreateInvitationCommand(string Email, int Days = 7) : IRequest<InvitationDto>;

public sealed class CreateInvitationCommandValidator : AbstractValidator<CreateInvitationCommand>
{
    public CreateInvitationCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Days).InclusiveBetween(1, 30);
    }
}

public sealed class CreateInvitationCommandHandler(
    IInvitationRepository invitations,
    IUserRepository users,
    TimeProvider timeProvider) : IRequestHandler<CreateInvitationCommand, InvitationDto>
{
    public async Task<InvitationDto> Handle(CreateInvitationCommand request, CancellationToken cancellationToken)
    {
        var email = InvitationMapper.NormaliseEmail(request.Email);

        // 必須在撤銷之前：被拒絕的請求不應作廢既有的有效邀請
        if (await users.GetByEmailAsync(email, cancellationToken) is not null)
        {
            throw new ConflictException($"Email '{email}' is already registered.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // 同一 email 同時只有一份有效邀請；兩步之間的競態由 ux_invitations_email_pending 擋下
        await invitations.RevokePendingAsync(email, now, cancellationToken);

        var invitation = new Invitation
        {
            Id = ObjectId.GenerateNewId(),
            Email = email,
            Code = InvitationCode.Generate(),
            Status = InvitationStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.AddDays(request.Days)
        };

        await invitations.InsertAsync(invitation, cancellationToken);

        return InvitationMapper.ToDto(invitation, now);
    }
}

public record RevokeInvitationCommand(string Email) : IRequest<long>;

public sealed class RevokeInvitationCommandValidator : AbstractValidator<RevokeInvitationCommand>
{
    public RevokeInvitationCommandValidator() => RuleFor(x => x.Email).NotEmpty();
}

public sealed class RevokeInvitationCommandHandler(
    IInvitationRepository invitations,
    TimeProvider timeProvider) : IRequestHandler<RevokeInvitationCommand, long>
{
    public Task<long> Handle(RevokeInvitationCommand request, CancellationToken cancellationToken) =>
        invitations.RevokePendingAsync(
            InvitationMapper.NormaliseEmail(request.Email),
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
}

public record ListInvitationsQuery : IRequest<IReadOnlyList<InvitationDto>>;

public sealed class ListInvitationsQueryHandler(
    IInvitationRepository invitations,
    TimeProvider timeProvider) : IRequestHandler<ListInvitationsQuery, IReadOnlyList<InvitationDto>>
{
    public async Task<IReadOnlyList<InvitationDto>> Handle(ListInvitationsQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var all = await invitations.ListAsync(cancellationToken);
        return all.Select(i => InvitationMapper.ToDto(i, now)).ToList();
    }
}
```

- [ ] **Step 4：跑單檔測試**：Expected `8 passed`
- [ ] **Step 5：跑全部測試**：Expected `650 passed`
- [ ] **Step 6：Commit**

```
git add src/MyCollection.Application/Auth/InvitationCommands.cs tests/MyCollection.Tests/Unit/InvitationCommandTests.cs
```

```
feat(auth): 新增建立、撤銷、列出邀請的 command 與 query

建立時拒發已註冊的 email，並先撤銷同一 email 未使用的舊邀請；「已過期」由讀取端推導。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Task 5：MongoInvitationRedeemer

**Files:** Create: `src/MyCollection.Application/Auth/IInvitationRedeemer.cs`、`src/MyCollection.Infrastructure/Mongo/MongoInvitationRedeemer.cs`、`tests/MyCollection.Tests/Integration/MongoInvitationRedeemerTests.cs`。Modify: `Infrastructure/DependencyInjection.cs`

這是本計畫的安全邊界。有三個地方容易錯：
1. 五種失敗原因必須回**同一個**例外和同一個訊息。
2. 插入 User 失敗時，邀請必須保持 Pending，也就是 transaction 真的有回滾。
3. 兩個請求同時使用同一份邀請時，只能有一人成功。在 transaction 裡，後到的請求會先遇到 WriteConflict；driver 會自動重試，重試時條件更新已經命中 0 筆，於是拋出 Forbidden。

- [ ] **Step 1：寫失敗測試**

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Auth;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoInvitationRedeemerTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);
    private const string Email = "friend@example.com";

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private MongoInvitationRedeemer CreateSut() => new(fixture.Context, NullLogger<MongoInvitationRedeemer>.Instance);

    private static User NewUser(string email = Email) => new()
    {
        Id = ObjectId.GenerateNewId(),
        Email = email,
        PasswordHash = "hash",
        DisplayName = "Friend",
        CreatedAt = Now
    };

    private async Task<Invitation> SeedAsync(Action<Invitation>? tweak = null)
    {
        var invitation = new Invitation
        {
            Id = ObjectId.GenerateNewId(),
            Email = Email,
            Code = InvitationCode.Generate(),
            Status = InvitationStatus.Pending,
            CreatedAt = Now.AddDays(-1),
            ExpiresAt = Now.AddDays(6)
        };
        tweak?.Invoke(invitation);
        await fixture.Context.Invitations.InsertOneAsync(invitation);
        return invitation;
    }

    private Task<Invitation> ReloadAsync(ObjectId id) =>
        fixture.Context.Invitations.Find(x => x.Id == id).SingleAsync();

    [Fact]
    public async Task Redeem_consumes_invitation_and_creates_user_together()
    {
        var invitation = await SeedAsync();
        var user = NewUser();

        await CreateSut().RedeemAsync(invitation.Code, user, Now, CancellationToken.None);

        var stored = await ReloadAsync(invitation.Id);
        stored.Status.Should().Be(InvitationStatus.Used);
        stored.UsedAt.Should().Be(Now);
        stored.UsedByUserId.Should().Be(user.Id);
        (await fixture.Context.Users.CountDocumentsAsync(x => x.Id == user.Id)).Should().Be(1);
    }

    public enum Failure { UnknownCode, Expired, Used, Revoked, EmailMismatch }

    [Theory]
    [InlineData(Failure.UnknownCode)]
    [InlineData(Failure.Expired)]
    [InlineData(Failure.Used)]
    [InlineData(Failure.Revoked)]
    [InlineData(Failure.EmailMismatch)]
    public async Task Every_failure_reason_looks_the_same_and_creates_no_user(Failure failure)
    {
        var invitation = await SeedAsync(i =>
        {
            if (failure == Failure.Expired) i.ExpiresAt = Now;
            if (failure == Failure.Used) i.Status = InvitationStatus.Used;
            if (failure == Failure.Revoked) i.Status = InvitationStatus.Revoked;
        });
        var code = failure == Failure.UnknownCode ? InvitationCode.Generate() : invitation.Code;
        var user = NewUser(failure == Failure.EmailMismatch ? "stranger@example.com" : Email);

        var act = () => CreateSut().RedeemAsync(code, user, Now, CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .WithMessage(MongoInvitationRedeemer.InvalidInvitation);
        (await fixture.Context.Users.CountDocumentsAsync(FilterDefinition<User>.Empty)).Should().Be(0);
    }

    [Fact]
    public async Task Duplicate_user_email_rolls_back_and_keeps_invitation_pending()
    {
        var invitation = await SeedAsync();
        await fixture.Context.Users.InsertOneAsync(NewUser());

        var act = () => CreateSut().RedeemAsync(invitation.Code, NewUser(), Now, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        (await ReloadAsync(invitation.Id)).Status.Should().Be(InvitationStatus.Pending);
    }

    [Fact]
    public async Task Concurrent_redemptions_of_one_invitation_create_exactly_one_user()
    {
        var invitation = await SeedAsync();

        var attempts = Enumerable.Range(0, 2).Select(async _ =>
        {
            try
            {
                await CreateSut().RedeemAsync(invitation.Code, NewUser(), Now, CancellationToken.None);
                return true;
            }
            catch (Exception ex) when (ex is ForbiddenException or ConflictException)
            {
                return false;
            }
        });

        var results = await Task.WhenAll(attempts);

        results.Count(ok => ok).Should().Be(1);
        (await fixture.Context.Users.CountDocumentsAsync(FilterDefinition<User>.Empty)).Should().Be(1);
    }
}
```

- [ ] **Step 2：跑測試確認失敗**：預期是編譯錯誤，因為 `MongoInvitationRedeemer` 不存在。
- [ ] **Step 3：最小實作**

```csharp
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Auth;

/// <summary>
/// 消耗邀請並建立帳號，兩者同時成功或同時失敗（ADR-0013 §二）。
/// 邀請無效（不存在、過期、已使用、已撤銷、email 不符）一律擲出同一訊息的
/// <see cref="Domain.Exceptions.ForbiddenException"/>；email 已被註冊擲出 <see cref="Domain.Exceptions.ConflictException"/>。
/// </summary>
public interface IInvitationRedeemer
{
    /// <param name="code">已經過 <see cref="InvitationCode.Normalise"/>。</param>
    /// <param name="newUser">Id 與正規化後的 Email 已填好。</param>
    Task RedeemAsync(string code, User newUser, DateTime now, CancellationToken ct);
}
```

```csharp
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MyCollection.Application.Auth;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

/// <summary>
/// 全案第二個跨 collection 的寫入，模式同 <see cref="MongoCategoryFieldRenamer"/>：
/// 單一操作自帶 session，不做通用 UnitOfWork。
/// </summary>
public sealed class MongoInvitationRedeemer(MongoContext context, ILogger<MongoInvitationRedeemer> logger)
    : IInvitationRedeemer
{
    /// <summary>所有失敗原因共用，避免外人藉由不同回應來試探碼或 email。</summary>
    public const string InvalidInvitation = "The invitation is invalid or no longer valid.";

    private static readonly FilterDefinitionBuilder<Invitation> Filter = Builders<Invitation>.Filter;

    public async Task RedeemAsync(string code, User newUser, DateTime now, CancellationToken ct)
    {
        using var session = await context.Database.Client.StartSessionAsync(cancellationToken: ct);

        await session.WithTransactionAsync(async (s, token) =>
        {
            // User 的 Id 已事先產生，所以 usedByUserId 能在同一個條件更新裡寫入
            var consumed = await context.Invitations.UpdateOneAsync(s,
                Filter.And(
                    Filter.Eq(x => x.Code, code),
                    Filter.Eq(x => x.Email, newUser.Email),
                    Filter.Eq(x => x.Status, InvitationStatus.Pending),
                    Filter.Gt(x => x.ExpiresAt, now)),
                Builders<Invitation>.Update
                    .Set(x => x.Status, InvitationStatus.Used)
                    .Set(x => x.UsedAt, now)
                    .Set(x => x.UsedByUserId, newUser.Id),
                cancellationToken: token);

            if (consumed.MatchedCount == 0)
            {
                var existing = await context.Invitations.Find(s, Filter.Eq(x => x.Code, code))
                    .FirstOrDefaultAsync(token);

                // 真實原因只記在 log；不記 email，避免個資進入 Cloud Logging
                logger.LogWarning("Invitation redemption rejected: {Reason} (invitation {InvitationId})",
                    ReasonFor(existing, newUser.Email, now), existing?.Id.ToString() ?? "none");

                throw new ForbiddenException(InvalidInvitation);
            }

            try
            {
                await context.Users.InsertOneAsync(s, newUser, cancellationToken: token);
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // 例外中止 transaction，上面的消耗一併回滾
                throw new ConflictException($"Email '{newUser.Email}' is already registered.");
            }

            return true;
        }, cancellationToken: ct);
    }

    private static string ReasonFor(Invitation? invitation, string email, DateTime now) => invitation switch
    {
        null => "unknown-code",
        { Status: InvitationStatus.Used } => "used",
        { Status: InvitationStatus.Revoked } => "revoked",
        _ when invitation.Email != email => "email-mismatch",
        _ when invitation.ExpiresAt <= now => "expired",
        _ => "concurrently-consumed"
    };
}
```

`Infrastructure/DependencyInjection.cs`，在 `ICategoryFieldRenamer` 那一行下方加上：

```csharp
        services.AddScoped<IInvitationRedeemer, MongoInvitationRedeemer>();
```

- [ ] **Step 4：跑單檔測試**：Expected `8 passed`
- [ ] **Step 5：跑全部測試**：Expected `658 passed`
- [ ] **Step 6：Commit**

```
git add src/MyCollection.Application/Auth/IInvitationRedeemer.cs src/MyCollection.Infrastructure/Mongo/MongoInvitationRedeemer.cs src/MyCollection.Infrastructure/DependencyInjection.cs tests/MyCollection.Tests/Integration/MongoInvitationRedeemerTests.cs
```

```
feat(auth): 兌換邀請與建立帳號在同一個 transaction 內完成

五種邀請失敗原因回同一訊息（403），真實原因只記 log；插入 User 失敗時邀請保持 Pending；同一邀請並行兌換只有一人成功。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Task 6：RegisterCommand 需要邀請碼（行為切換點）

**Files:** Modify: `src/MyCollection.Application/Auth/RegisterCommand.cs`、`tests/.../Unit/RegisterCommandTests.cs`、`tests/.../Fixtures/AuthenticatedClient.cs`、`tests/.../Integration/AuthEndpointsTests.cs`

這個 Task 之後，所有需要登入的整合測試都要經過邀請，所以 `AuthenticatedClient` 必須在**同一個 commit** 裡修好，否則全部整合測試都會拿到 400。

另一個容易錯的地方：舊的 `Register_with_duplicate_email_returns_409` 已經無法照原樣重現。邀請用過就失效，而已註冊的 email 也拿不到新邀請。改成驗證「重複使用已用過的邀請」會回 403。email 重複時回 409 的路徑，已由 Task 5 的 `Duplicate_user_email_rolls_back_...` 在 repository 層涵蓋。

- [ ] **Step 1：寫失敗測試**

`RegisterCommandTests`：`_users` 改為 `Mock<IInvitationRedeemer> _redeemer`，`CreateSut()` 改為 `new(_redeemer.Object, _hasher.Object, _tokens.Object, _time)`。所有 `new RegisterCommand(e, p, d)` 補上第四個參數 `"ABCDE-FGHJK-MNPQR-STVWX"`。validator 的 Theory 新增一列：

```csharp
    [InlineData("a@b.c", "P@ssw0rd!", "Adam", "")]
```

（Theory 的簽章要加上 `string invitationCode`，原本四列都補上 `"ABCDE-FGHJK-MNPQR-STVWX"`。）

原本的 handler 測試改為攔截 redeemer：

```csharp
    [Fact]
    public async Task Handler_redeems_normalised_code_with_hashed_user()
    {
        string? redeemedCode = null;
        User? redeemedUser = null;
        _redeemer.Setup(r => r.RedeemAsync(It.IsAny<string>(), It.IsAny<User>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<string, User, DateTime, CancellationToken>((c, u, _, _) => (redeemedCode, redeemedUser) = (c, u))
            .Returns(Task.CompletedTask);

        var response = await CreateSut().Handle(
            new RegisterCommand(" Adam@Example.com ", "P@ssw0rd!", "Adam", "abcde-fghjk-mnpqr-stvwx"), CancellationToken.None);

        redeemedCode.Should().Be("ABCDEFGHJKMNPQRSTVWX");
        redeemedUser!.Email.Should().Be("adam@example.com");
        redeemedUser.PasswordHash.Should().Be("hashed");
        redeemedUser.RefreshTokenHash.Should().Be("refresh-hash");
        redeemedUser.CreatedAt.Should().Be(new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc));
        response.User.Id.Should().Be(redeemedUser.Id.ToString());
        response.ExpiresAt.Should().Be(new DateTime(2026, 7, 25, 3, 30, 0, DateTimeKind.Utc));
    }
```

`AuthenticatedClient.cs`：

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MyCollection.Application.Auth;

namespace MyCollection.Tests.Fixtures;

public static class AuthenticatedClient
{
    /// <summary>以正式的維運路徑（CreateInvitationCommand）發出邀請，回傳格式化後的邀請碼。</summary>
    public static async Task<string> CreateInvitationAsync(ApiFactory factory, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var invitation = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new CreateInvitationCommand(email));
        return invitation.Code;
    }

    /// <summary>發邀請、註冊一個新帳號，並回傳已帶好 Bearer token 的 client。</summary>
    public static async Task<HttpClient> CreateAsync(ApiFactory factory, string email)
    {
        var invitationCode = await CreateInvitationAsync(factory, email);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/register", new { email, password = "P@ssw0rd!", displayName = "Tester", invitationCode });
        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        return client;
    }
}
```

`AuthEndpointsTests`：把 `RegisterPayload` 換成下面的 helper，原本 7 處 `PostAsJsonAsync("/auth/register", RegisterPayload())` 都改成 `await RegisterAsync()`（只有 `Register_with_invalid_payload_returns_400_with_errors` 保持直接送出無效的 payload）：

```csharp
    private async Task<HttpResponseMessage> RegisterAsync(string email = "adam@example.com", string? code = null)
    {
        code ??= await AuthenticatedClient.CreateInvitationAsync(_factory, email);
        return await _client.PostAsJsonAsync(
            "/auth/register", new { email, password = "P@ssw0rd!", displayName = "Adam", invitationCode = code });
    }
```

`Register_with_duplicate_email_returns_409` 改名並改寫：

```csharp
    [Fact]
    public async Task Register_reusing_a_used_invitation_returns_403()
    {
        var code = await AuthenticatedClient.CreateInvitationAsync(_factory, "adam@example.com");
        await RegisterAsync(code: code);

        var response = await RegisterAsync(code: code);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
```

新增：

```csharp
    [Fact]
    public async Task Register_without_invitation_code_returns_400()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/register", new { email = "adam@example.com", password = "P@ssw0rd!", displayName = "Adam" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_with_unknown_invitation_code_returns_403()
    {
        var response = await RegisterAsync(code: "ABCDE-FGHJK-MNPQR-STVWX");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Register_with_invitation_for_another_email_returns_403()
    {
        var code = await AuthenticatedClient.CreateInvitationAsync(_factory, "friend@example.com");

        var response = await RegisterAsync(email: "stranger@example.com", code: code);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
```

- [ ] **Step 2：跑測試確認失敗**：`--filter "FullyQualifiedName~RegisterCommandTests|FullyQualifiedName~AuthEndpointsTests"`。預期先出現編譯錯誤：`RegisterCommand` 沒有 4 參數的建構式、handler 不接受 `IInvitationRedeemer`。
- [ ] **Step 3：最小實作**：`RegisterCommand.cs`

```diff
-public record RegisterCommand(string Email, string Password, string DisplayName) : IRequest<AuthResponse>;
+public record RegisterCommand(string Email, string Password, string DisplayName, string InvitationCode)
+    : IRequest<AuthResponse>;
@@ RegisterCommandValidator
         RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(64);
+        RuleFor(x => x.InvitationCode).NotEmpty().MaximumLength(64);
@@
 public sealed class RegisterCommandHandler(
-    IUserRepository users,
+    IInvitationRedeemer invitations,
     IPasswordHasher passwordHasher,
@@
-        // email 重複時 Repository 擲 ConflictException → 409
-        await users.InsertAsync(user, cancellationToken);
+        // 邀請無效 → ForbiddenException（403，訊息不分原因）；email 重複 → ConflictException（409）。
+        // 兩者都會讓整筆 transaction 回滾（ADR-0013 §二）
+        await invitations.RedeemAsync(
+            Auth.InvitationCode.Normalise(request.InvitationCode), user, now, cancellationToken);
```

`Auth.InvitationCode` 用命名空間限定，是為了和 `RegisterCommand.InvitationCode` 屬性區分，讓讀者一眼看出這裡呼叫的是型別。另外，`Email` 原本就在 handler 裡 `Trim().ToLowerInvariant()`，所以不用改。

- [ ] **Step 4：跑單檔測試**：Expected `RegisterCommandTests 7 passed`、`AuthEndpointsTests` 比原本多 3 筆（淨增：Unit +1、Integration +3）
- [ ] **Step 5：跑全部測試**：Expected `662 passed`
- [ ] **Step 6：Commit**

```
git add src/MyCollection.Application/Auth/RegisterCommand.cs tests/MyCollection.Tests/Unit/RegisterCommandTests.cs tests/MyCollection.Tests/Fixtures/AuthenticatedClient.cs tests/MyCollection.Tests/Integration/AuthEndpointsTests.cs
```

```
feat(auth): 註冊必須出示有效邀請碼（I2）

RegisterCommand 新增 InvitationCode，handler 改經 IInvitationRedeemer 在同一 transaction 內消耗邀請並建立帳號。
測試 fixture 改走 CreateInvitationCommand 發邀請後再註冊。

前端尚未帶 invitationCode，部署前需一併完成前端計畫。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

**— Checkpoint B：註冊已切換為邀請制，測試全綠，可以在這裡收工（但不可單獨部署）—**

## Task 7：維運指令 `maintenance invite`

**Files:** Create: `src/MyCollection.Api/Maintenance/InviteMaintenance.cs`、`tests/MyCollection.Tests/Unit/InviteMaintenanceTests.cs`。Modify: `src/MyCollection.Api/Program.cs`

`InviteMaintenance` 接收 `ISender`、`TextWriter`，不直接依賴 `Console` 和 DI 容器，這樣可以單元測試。`Program.cs` 只負責組裝，照 `6afd6de` 的寫法：`maintenance` 參數不能交給設定系統，否則 `--email` 會被當成設定鍵。`Build()` 之後要先執行 `EnsureIndexesAsync`，因為全新的資料庫需要 partial unique 索引才能保證規則 4。

- [ ] **Step 1：寫失敗測試**

```csharp
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Moq;
using MyCollection.Api.Maintenance;
using MyCollection.Application.Auth;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class InviteMaintenanceTests
{
    private readonly Mock<ISender> _sender = new();
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    private static readonly InvitationDto Dto = new(
        "friend@example.com", "ABCDE-FGHJK-MNPQR-STVWX", "Pending",
        new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
        null, null);

    private Task<int> Run(params string[] args) =>
        InviteMaintenance.RunAsync(args, _sender.Object, "https://collection.example", _out, _err, CancellationToken.None);

    [Fact]
    public async Task Create_prints_code_and_register_link()
    {
        _sender.Setup(s => s.Send(new CreateInvitationCommand("friend@example.com", 3), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Dto);

        var exit = await Run("invite", "create", "--email", "friend@example.com", "--days", "3");

        exit.Should().Be(0);
        _out.ToString().Should().Contain("ABCDE-FGHJK-MNPQR-STVWX")
            .And.Contain("https://collection.example/register?code=ABCDE-FGHJK-MNPQR-STVWX");
    }

    [Theory]
    [InlineData("invite", "create")]
    [InlineData("invite", "create", "--email", "a@b.c", "--days", "x")]
    [InlineData("invite", "delete")]
    [InlineData("other")]
    public async Task Bad_arguments_print_usage_and_exit_2(params string[] args)
    {
        var exit = await Run(args);

        exit.Should().Be(2);
        _err.ToString().Should().Contain("maintenance invite create");
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Conflict_is_reported_with_exit_1()
    {
        _sender.Setup(s => s.Send(It.IsAny<CreateInvitationCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Email 'friend@example.com' is already registered."));

        var exit = await Run("invite", "create", "--email", "friend@example.com");

        exit.Should().Be(1);
        _err.ToString().Should().Contain("already registered");
    }

    [Fact]
    public async Task Validation_failure_is_reported_with_exit_1()
    {
        _sender.Setup(s => s.Send(It.IsAny<CreateInvitationCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationException([new ValidationFailure("Email", "bad email")]));

        var exit = await Run("invite", "create", "--email", "nope");

        exit.Should().Be(1);
        _err.ToString().Should().Contain("bad email");
    }

    [Fact]
    public async Task List_prints_one_line_per_invitation()
    {
        _sender.Setup(s => s.Send(It.IsAny<ListInvitationsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Dto, Dto with { Email = "other@example.com", Status = "Used" }]);

        var exit = await Run("invite", "list");

        exit.Should().Be(0);
        _out.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(2);
    }
}
```

- [ ] **Step 2：跑測試確認失敗**：預期是編譯錯誤，因為 `InviteMaintenance` 不存在。
- [ ] **Step 3：最小實作**

```csharp
using FluentValidation;
using MediatR;
using MyCollection.Application.Auth;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Api.Maintenance;

/// <summary>
/// 邀請只能由維運指令發出，系統內沒有管理者角色（ADR-0013 §一）。
/// 這裡是 CLI 的最外層，等同 HTTP 的 GlobalExceptionHandler，所以在這裡把預期中的例外轉成 exit code。
/// </summary>
public static class InviteMaintenance
{
    public const string Usage = """
        Usage:
          maintenance invite create --email <email> [--days <1-30>]
          maintenance invite revoke --email <email>
          maintenance invite list
        """;

    public static async Task<int> RunAsync(
        string[] args, ISender sender, string? webOrigin, TextWriter output, TextWriter error, CancellationToken ct)
    {
        try
        {
            switch (args)
            {
                case ["invite", "create", ..] when Option(args, "--email") is { } email:
                    var daysText = Option(args, "--days");
                    if (daysText is not null && !int.TryParse(daysText, out _))
                    {
                        break;
                    }

                    var created = await sender.Send(
                        new CreateInvitationCommand(email, daysText is null ? 7 : int.Parse(daysText)), ct);
                    await output.WriteLineAsync($"Email:   {created.Email}");
                    await output.WriteLineAsync($"Code:    {created.Code}");
                    await output.WriteLineAsync($"Expires: {created.ExpiresAt:u}");
                    await output.WriteLineAsync($"Link:    {webOrigin?.TrimEnd('/')}/register?code={created.Code}");
                    return 0;

                case ["invite", "revoke", ..] when Option(args, "--email") is { } email:
                    var revoked = await sender.Send(new RevokeInvitationCommand(email), ct);
                    await output.WriteLineAsync(revoked == 0
                        ? $"No pending invitation for {email}."
                        : $"Revoked {revoked} pending invitation(s) for {email}.");
                    return 0;

                case ["invite", "list"]:
                    foreach (var i in await sender.Send(new ListInvitationsQuery(), ct))
                    {
                        await output.WriteLineAsync(
                            $"{i.Status,-8} {i.Email,-40} {i.Code}  expires {i.ExpiresAt:u}  used {i.UsedAt:u}");
                    }

                    return 0;
            }
        }
        catch (ValidationException ex)
        {
            await error.WriteLineAsync(string.Join(Environment.NewLine, ex.Errors.Select(e => e.ErrorMessage)));
            return 1;
        }
        catch (ConflictException ex)
        {
            await error.WriteLineAsync(ex.Message);
            return 1;
        }

        await error.WriteLineAsync(Usage);
        return 2;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
```

`Program.cs`：

```diff
-var builder = WebApplication.CreateBuilder(args);
+// 維運指令（ADR-0013：邀請只能由這裡發出）。
+// 維運參數不交給設定系統，避免 "--email" 被當成設定鍵。
+var isMaintenance = args is ["maintenance", ..];
+var builder = WebApplication.CreateBuilder(isMaintenance ? [] : args);
@@
 var app = builder.Build();
 
+if (isMaintenance)
+{
+    await using var maintenanceScope = app.Services.CreateAsyncScope();
+    var services = maintenanceScope.ServiceProvider;
+
+    // 全新的資料庫也要先有 ux_invitations_email_pending，才能保證同一 email 只有一份有效邀請
+    await MongoIndexInitializer.EnsureIndexesAsync(services.GetRequiredService<MongoContext>(), CancellationToken.None);
+
+    Environment.ExitCode = await InviteMaintenance.RunAsync(
+        args[1..],
+        services.GetRequiredService<MediatR.ISender>(),
+        app.Configuration["Cors:AllowedOrigins:0"],
+        Console.Out,
+        Console.Error,
+        CancellationToken.None);
+    return;
+}
+
 app.UseForwardedHeaders();
```

並在檔頭加上 `using MyCollection.Api.Maintenance;`。

- [ ] **Step 4：跑單檔測試**：Expected `8 passed`
- [ ] **Step 5：跑全部測試**：Expected `670 passed`；`dotnet build MyCollection.slnx` 0 warnings
- [ ] **Step 6：Commit**

```
git add src/MyCollection.Api/Maintenance/InviteMaintenance.cs src/MyCollection.Api/Program.cs tests/MyCollection.Tests/Unit/InviteMaintenanceTests.cs
```

```
feat(auth): 新增維運指令 maintenance invite create|revoke|list

`dotnet run --project src/MyCollection.Api -- maintenance invite create --email <e> [--days <n>]`
- create 輸出邀請碼與 {Cors:AllowedOrigins:0}/register?code= 連結
- 執行前先確保索引，全新資料庫也能保證同一 email 只有一份有效邀請

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Task 8：文件

**Files:** Modify: `docs/architecture/90-tech-debt.md`、`docs/architecture/01-overview.md`、`AGENTS.md`

- [ ] `90-tech-debt.md`：I2 那一列前面加上「【已解決 2026-09-28，`<Task 6 SHA>`】」，建議方向欄寫實作摘要，並指向 ADR-0013。「建議處理順序」第 2 點把 I2 加上刪除線。
- [ ] `01-overview.md`：在 ADR 索引表加入 0013 一列，並把「共 12 份」改成「共 13 份」。
- [ ] `AGENTS.md`：「高風險區域」的 `I1／I2／R4` 改成 `I1／R4`，並把「註冊開放」改寫為「註冊已改為邀請制（ADR-0013），邀請只能由 `maintenance invite` 發出」。「架構速覽」的第一段「未來改為邀請制」改成「採邀請制」。
- [ ] `git add` 上述三個檔案，然後 commit：

```
docs(architecture): 標記 I2 已解決，ADR 索引加入 ADR-0013，AGENTS.md 高風險區域移除 I2

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## 完成後的驗證

- [ ] `dotnet test MyCollection.slnx`：**670 passed**，0 failed
- [ ] `dotnet build MyCollection.slnx`：0 warnings
- [ ] `git status`：乾淨
- [ ] `git log master..HEAD --oneline`：9 顆 commit（Task 0～8）
- [ ] `git diff master..HEAD` 沒有連線字串、金鑰、個人 email

## 手動驗證

需要本機 Mongo，連線設定放在 user secrets，並確認連到的是**開發資料庫**。

1. `dotnet run --project src/MyCollection.Api -- maintenance invite create --email me@example.test`：輸出 Code 和 Link，exit code 0。
2. 對同一個 email 再執行一次：輸出新的碼；`maintenance invite list` 應該看到舊的是 `Revoked`、新的是 `Pending`。
3. `maintenance invite create --email <已註冊的 email>`：stderr 顯示 already registered，exit code 1。
4. `maintenance invite bogus`：印出 Usage，exit code 2。
5. 啟動 API，`POST /auth/register` 帶入第 2 步的碼，email 用**大寫**、碼用**小寫加空白**：回 200。再送一次相同內容：回 403，ProblemDetails 的 detail 是通用訊息；API log 有 `Reason: used`。
6. `maintenance invite revoke --email me@example.test`：顯示 `No pending invitation`，因為這份邀請已經用掉了。

## 後續（不在本計畫內）

- **前端計畫（必須和本計畫一起部署）**：新增 `/register` 路由並讀取 `?code=` 帶入；登入頁移除註冊切換；`auth.service.register` 帶上 `invitationCode`；`models.ts` 同步；403 時顯示通用訊息。在新的 session 用 `/tddplan` 產出。
- 正式環境的第一份邀請（部署之後由維運者執行）。
- I1 速率限制。
- `docs/deployment/production-operations.md` 補上「如何發邀請」的維運步驟，可以和前端計畫一起做。
