# 品類欄位改名：後端實作計畫

> For agentic workers: REQUIRED SUB-SKILL — 逐 Task 走 TDD（先紅、確認紅的理由、再綠）。一個 Task 一個 commit，`git add` 只用明確路徑。committed code 才是權威，本文件的 snippet 是草稿。

**Goal**：實作 [ADR-0012](../../adr/0012-field-key-is-identity-rename-is-explicit.md) 的後端：明確的改名命令（transaction 內搬移品項屬性）、來源欄位鎖定、有品項的品類不可刪。

**Architecture**：Clean Architecture + MediatR。Application 層新增兩個 port（`IProtectedFieldKeys`、`ICategoryFieldRenamer`），Infrastructure 實作；transaction 只活在 `MongoCategoryFieldRenamer` 內，不外露 session。錯誤語意見 spec §3.3。

**Tech Stack**：.NET 10、MongoDB.Driver 3.11.1、FluentValidation 12、xUnit + FluentAssertions + Moq、Testcontainers.MongoDb 4.15.0。

- 設計文件：`docs/superpowers/specs/2026-09-15-category-field-rename-design.md`
- 前端計畫：`docs/superpowers/plans/2026-09-15-category-field-rename-frontend.md`（依賴本計畫 Task 7 的端點，可在 Task 5 定案 DTO 形狀後並行）

## 執行前必讀

### 環境

- 路徑：`F:\VibeCode\MyCollection`
- 分支：**從 `master` 開 `feat/category-field-rename`**（不要直接在 master 動）
- 需要 Docker Desktop 啟動（Testcontainers）。本計畫撰寫時 Docker 未啟動，**基準線由執行者第一步補填**。
- 全部測試：`dotnet test tests/MyCollection.Tests/MyCollection.Tests.csproj`
- 單檔（class）：`dotnet test tests/MyCollection.Tests/MyCollection.Tests.csproj --filter "FullyQualifiedName~MyCollection.Tests.Unit.RenameCategoryFieldCommandTests"`
- Build：`dotnet build MyCollection.slnx -warnaserror`（專案已開 `TreatWarningsAsErrors`；solution 檔是 `.slnx`）

### 基準線

執行者在動任何檔案前先跑一次全部測試並把數字寫在這裡：

- 後端：**565 passed, 0 failed, 0 skipped**（2026-09-16 於 `997106c` 實測）；Task 1 後 566、Task 2 後 576（`c6f6b69`，實測）
- build：0 warnings

**任何時候數字低於基準線就是弄壞了東西。** 每個 Task 結束時的期望值是「基準線 + 該 Task 新增的測試數」。

### 絕對不要碰的檔案

- 工作樹目前已有兩個未提交變更：`CONTEXT.md`（新詞條）、`docs/adr/0012-…md`（新 ADR）。它們在 **Task 0** 單獨 commit，之後每個 Task 只 `git add` 該 Task 列出的路徑。**禁止 `git add .` / `git add -A`。**
- `web/` 整個目錄——屬於前端計畫。
- `src/MyCollection.Infrastructure/Mongo/SystemCategoryDefinitions.cs`、各 `*Fields.cs`——只讀取，不改。

### 慣例

- 註解與 commit message 用繁體中文；程式識別字英文。commit 格式 `feat(categories): …` / `test(categories): …` / `refactor(…)`，結尾加 `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`。
- 測試分層：`Unit/`（Moq，不碰 Mongo）、`Integration/`（`[Collection(MongoCollection.Name)]` + `fixture.ResetAsync()`）。
- DTO 用 `record`；Request 驗證用 FluentValidation；不在 handler 內 try-catch。
- 所有 Mongo filter 以 `OwnerId` 起頭。
- 例外一律 Domain 現有型別：`NotFoundException` / `ForbiddenException` / `ConflictException`；400 用 FluentValidation 的 `ValidationException(IEnumerable<ValidationFailure>)`。

## 檔案結構

### 新增

| 檔案 | 責任 |
|---|---|
| `src/MyCollection.Application/Categories/IProtectedFieldKeys.cs` | port：`IsProtected(key)` |
| `src/MyCollection.Application/Categories/ICategoryFieldRenamer.cs` | port：transaction 內改名，回搬移數 |
| `src/MyCollection.Application/Categories/RenameCategoryFieldCommand.cs` | command、DTO、validator、handler |
| `src/MyCollection.Infrastructure/Providers/ProviderFieldKeyCatalog.cs` | 靜態鎖定集合 |
| `src/MyCollection.Infrastructure/Mongo/MongoCategoryFieldRenamer.cs` | transaction 實作 |
| `tests/MyCollection.Tests/Integration/MongoTransactionSmokeTests.cs` | Task 1 的紅燈 |
| `tests/MyCollection.Tests/Unit/ProviderFieldKeyCatalogTests.cs` | Task 2 |
| `tests/MyCollection.Tests/Unit/RenameCategoryFieldCommandTests.cs` | Task 5 |
| `tests/MyCollection.Tests/Integration/MongoCategoryFieldRenamerTests.cs` | Task 6 |
| `tests/MyCollection.Tests/Integration/CategoryEndpointsTests.cs` | Task 7（目前不存在，已確認） |

### 修改

| 檔案 | 改動 |
|---|---|
| `tests/MyCollection.Tests/Fixtures/MongoFixture.cs` | `.WithReplicaSet("rs0")` |
| `src/MyCollection.Application/Categories/CategoryCommands.cs` | Update handler 注入 `IProtectedFieldKeys` 並擋撤回；Delete handler 注入 `ICategoryRepository`+`IItemRepository` 並擋有品項 |
| `src/MyCollection.Application/Items/IItemRepository.cs` | `CountByCategoryAsync` |
| `src/MyCollection.Infrastructure/Mongo/MongoItemRepository.cs` | 實作 `CountByCategoryAsync` |
| `src/MyCollection.Infrastructure/DependencyInjection.cs` | 註冊 catalog、renamer |
| `src/MyCollection.Api/Endpoints/CategoryEndpoints.cs` | rename 端點 |
| `tests/MyCollection.Tests/Unit/CategoryCommandTests.cs` | 第 119 行 `UpdateCategoryCommandHandler` 建構式多一個參數；新增 Update/Delete 的規則測試 |
| `tests/MyCollection.Tests/Integration/MongoItemRepositoryTests.cs` | `CountByCategoryAsync` 測試 |

### Task 相依順序

```
Task 0  commit CONTEXT.md + ADR（獨立）
Task 1  fixture replica set ─────────────┐
Task 2  IProtectedFieldKeys + catalog ─┐ │
Task 3  Update handler 擋撤回 ←────────┤ │
Task 4  CountByCategory + Delete handler │ │   （獨立於 2/3）
Task 5  Rename command/handler ←───────┘ │   （renamer 用 Moq，不需 Mongo）
Task 6  MongoCategoryFieldRenamer ←──────┘   （需 Task 1）
Task 7  端點 + DI + 端點測試 ←── 5, 6
```

可並行：{2→3}、{4}、{1→6} 三條線互不相依；Task 5 只依賴 2。**Usage-safe checkpoint**：Task 4 後、Task 6 後。

---

## Task 0：提交語彙與 ADR ✅ `997106c`

**Files:** Modify: `CONTEXT.md`；Create: `docs/adr/0012-field-key-is-identity-rename-is-explicit.md`

這兩個檔案是 grilling session 的產物，已在工作樹。單獨 commit，讓後面每個 Task 的 diff 乾淨。

- [ ] Step 1：`git checkout -b feat/category-field-rename`
- [ ] Step 2：`git add CONTEXT.md docs/adr/0012-field-key-is-identity-rename-is-explicit.md`
- [ ] Step 3：Commit
  ```
  docs(adr): ADR-0012 欄位鍵是身分，改名是明確操作

  CONTEXT.md 新增 欄位鍵 / 改名 / 未宣告屬性 / 來源欄位 四個詞條，
  品類詞條補上「仍有品項不能刪除」。

  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  ```

---

## Task 1：測試 Mongo 改為 single-node replica set ✅ `eec3942`

**Files:** Create: `tests/MyCollection.Tests/Integration/MongoTransactionSmokeTests.cs`；Modify: `tests/MyCollection.Tests/Fixtures/MongoFixture.cs`

Transaction 在 standalone Mongo 上會直接失敗——**實測（driver 3.11.1）是 client 端先擋下 `NotSupportedException: Standalone servers do not support transactions`**（`CoreSession.EnsureTransactionsAreSupported`），指令根本沒送到 server；舊版 driver 才會看到 server 端的 `Transaction numbers are only allowed on a replica set member or mongos`。這個 Task 的價值是讓那個錯誤先在測試裡出現一次，證明 fixture 改動是有效的，而不是改了之後「反正 Task 6 過了」。

- [ ] Step 1：寫失敗測試

```csharp
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Domain.Entities;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoTransactionSmokeTests(MongoFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Test_container_supports_multi_document_transactions()
    {
        var context = fixture.Context;
        var categoryId = ObjectId.GenerateNewId();

        using var session = await context.Database.Client.StartSessionAsync();
        await session.WithTransactionAsync(async (s, ct) =>
        {
            await context.Categories.InsertOneAsync(s, new Category
            {
                Id = categoryId,
                OwnerId = ObjectId.GenerateNewId(),
                Name = "tx",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }, cancellationToken: ct);

            return true;
        });

        var stored = await context.Categories.Find(c => c.Id == categoryId).FirstOrDefaultAsync();
        stored.Should().NotBeNull();
    }
}
```

- [x] Step 2：跑 `--filter "FullyQualifiedName~MongoTransactionSmokeTests"`，**確認失敗訊息是 `Standalone servers do not support transactions`**。若失敗原因是別的（連不上 Docker、編譯錯），先修到看到正確的紅。
- [ ] Step 3：最小實作——`MongoFixture.cs`：
  ```csharp
  private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0")
      .WithReplicaSet("rs0")
      .Build();
  ```
- [ ] Step 4：單檔測試 → `Passed: 1`
- [ ] Step 5：全部測試 → 基準線 + 1。**注意**：replica set 會讓容器啟動變慢幾秒；若整體時間暴增或有測試因 `w:majority` 行為差異變紅，記在回寫裡。
- [ ] Step 6：Commit
  ```
  test(fixtures): 測試用 Mongo 改為 single-node replica set

  改名命令需要跨 collection transaction，standalone 容器擲
  「Transaction numbers are only allowed on a replica set member」。
  dev / prod 都在 Atlas replica set 上，只有測試容器需要補齊。

  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  ```
  `git add tests/MyCollection.Tests/Fixtures/MongoFixture.cs tests/MyCollection.Tests/Integration/MongoTransactionSmokeTests.cs`

---

## Task 2：`IProtectedFieldKeys` 與靜態目錄 ✅ `c6f6b69`

**Files:** Create: `src/MyCollection.Application/Categories/IProtectedFieldKeys.cs`、`src/MyCollection.Infrastructure/Providers/ProviderFieldKeyCatalog.cs`、`tests/MyCollection.Tests/Unit/ProviderFieldKeyCatalogTests.cs`；Modify: `src/MyCollection.Infrastructure/DependencyInjection.cs`

容易錯的地方：把集合來源寫成 `ProviderRegistry`。ADR §四明講要靜態——registry 只含本部署有註冊的來源，IGDB 曾整組缺席十八天。測試裡刻意不建 registry、不給任何 options，證明目錄不依賴部署設定。

- [ ] Step 1：寫失敗測試

```csharp
using FluentAssertions;
using MyCollection.Infrastructure.Providers;
using MyCollection.Infrastructure.Providers.Igdb;
using MyCollection.Infrastructure.Providers.Psn;

namespace MyCollection.Tests.Unit;

public class ProviderFieldKeyCatalogTests
{
    private readonly ProviderFieldKeyCatalog _sut = new();

    [Theory]
    [InlineData(SteamFields.AppIdKey)]
    [InlineData(SteamFields.StoreUpdatedAtKey)]
    [InlineData(SteamFields.GenresKey)]
    [InlineData(IgdbFields.MarkerKey)]
    [InlineData(PsnFields.ProgressKey)]
    [InlineData(PsnFields.LastPlayedAtKey)]
    [InlineData("platform")] // ADR-0006 白名單
    public void Provider_and_platform_keys_are_protected(string key)
    {
        _sut.IsProtected(key).Should().BeTrue();
    }

    [Theory]
    [InlineData("brand")]
    [InlineData("SteamAppId")] // 大小寫敏感：欄位鍵比對一律 Ordinal
    public void User_keys_are_not_protected(string key)
    {
        _sut.IsProtected(key).Should().BeFalse();
    }

    [Fact]
    public void Catalog_covers_every_declared_provider_field()
    {
        var allDeclared = SteamFields.All.Concat(IgdbFields.All).Concat(PsnFields.All).Select(f => f.Key);

        allDeclared.Should().OnlyContain(k => _sut.IsProtected(k));
    }
}
```

- [ ] Step 2：跑單檔 → 編譯失敗（型別不存在）。這是正確的紅。
- [ ] Step 3：最小實作

`IProtectedFieldKeys.cs`：
```csharp
namespace MyCollection.Application.Categories;

/// <summary>
/// 受保護的欄位鍵：來源用字面值定址、不可改名也不可撤回宣告（ADR-0012 §四）。
/// 集合是靜態的，不隨哪些 provider 有註冊而變。
/// </summary>
public interface IProtectedFieldKeys
{
    bool IsProtected(string key);
}
```

`ProviderFieldKeyCatalog.cs`：
```csharp
using MyCollection.Application.Categories;
using MyCollection.Infrastructure.Providers.Igdb;
using MyCollection.Infrastructure.Providers.Psn;

namespace MyCollection.Infrastructure.Providers;

/// <summary>
/// 刻意不從 ProviderRegistry 取：registry 只含本部署有憑證的來源，
/// IGDB 曾因憑證缺失整組未註冊，那段期間若允許刪掉 igdbId，憑證補上後補完就寫不回宣告內。
/// "platform" 是 ADR-0006 的跨品類白名單，同樣以字面值定址。
/// </summary>
public sealed class ProviderFieldKeyCatalog : IProtectedFieldKeys
{
    private static readonly HashSet<string> Keys = SteamFields.All
        .Concat(IgdbFields.All)
        .Concat(PsnFields.All)
        .Select(f => f.Key)
        .Append("platform")
        .ToHashSet(StringComparer.Ordinal);

    public bool IsProtected(string key) => Keys.Contains(key);
}
```

`DependencyInjection.cs`（在 `AddSingleton<IAttributeValidator…>` 附近）：
```csharp
services.AddSingleton<IProtectedFieldKeys, ProviderFieldKeyCatalog>();
```

- [x] Step 4：單檔 → `Passed: 10`
- [x] Step 5：全部 → 基準線 + 10（原寫 +11 是筆誤：7 + 2 InlineData + 1 Fact = 10）
- [ ] Step 6：Commit `feat(categories): 受保護欄位鍵的靜態目錄`；`git add` 上列四個路徑。

---

## Task 3：`PUT /categories` 拒絕撤回受保護欄位

**Files:** Modify: `src/MyCollection.Application/Categories/CategoryCommands.cs`、`tests/MyCollection.Tests/Unit/CategoryCommandTests.cs`

Handler 從純覆寫變成 read-compare-write。容易錯：只比對「改了 key 的欄位」——PUT 不知道什麼叫改名，唯一能算的是 `existing − request` 的差集。既有第 119 行的 `new UpdateCategoryCommandHandler(_repository.Object, _time)` 要多傳一個參數。

- [ ] Step 1：在 `CategoryCommandTests` 加

```csharp
private sealed class StubProtectedKeys(params string[] keys) : IProtectedFieldKeys
{
    private readonly HashSet<string> _keys = new(keys, StringComparer.Ordinal);
    public bool IsProtected(string key) => _keys.Contains(key);
}

private static Category ExistingCategory(params string[] keys) => new()
{
    Id = ObjectId.GenerateNewId(),
    OwnerId = ObjectId.GenerateNewId(),
    Name = "自訂",
    Fields = keys.Select(k => new CategoryField { Key = k, Label = k, Type = FieldType.Text }).ToList(),
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
};

[Fact]
public async Task Update_rejects_withdrawing_a_protected_field()
{
    var existing = ExistingCategory("steamAppId", "brand");
    _repository.Setup(r => r.GetAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

    var command = new UpdateCategoryCommand(existing.Id.ToString(), "自訂", "box", "Physical", "List", [Field("brand")]);

    var act = () => new UpdateCategoryCommandHandler(_repository.Object, _time, new StubProtectedKeys("steamAppId"))
        .Handle(command, CancellationToken.None);

    var ex = await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    ex.Which.Errors.Should().ContainSingle(e => e.PropertyName == "Fields" && e.ErrorMessage.Contains("steamAppId"));
    _repository.Verify(r => r.UpdateAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Never);
}

[Fact]
public async Task Update_allows_withdrawing_an_unprotected_field()
{
    var existing = ExistingCategory("steamAppId", "brand");
    _repository.Setup(r => r.GetAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

    var command = new UpdateCategoryCommand(existing.Id.ToString(), "自訂", "box", "Physical", "List", [Field("steamAppId")]);

    var dto = await new UpdateCategoryCommandHandler(_repository.Object, _time, new StubProtectedKeys("steamAppId"))
        .Handle(command, CancellationToken.None);

    dto.Fields.Select(f => f.Key).Should().Equal("steamAppId");
    _repository.Verify(r => r.UpdateAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task Update_keeps_protected_field_when_it_is_still_declared()
{
    // 改 Label、改順序都不算撤回
    var existing = ExistingCategory("steamAppId", "brand");
    _repository.Setup(r => r.GetAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

    var command = new UpdateCategoryCommand(existing.Id.ToString(), "自訂", "box", "Physical", "List",
        [Field("brand"), new CategoryFieldDto("steamAppId", "改過的名稱", "Number", null, false, false, true)]);

    var dto = await new UpdateCategoryCommandHandler(_repository.Object, _time, new StubProtectedKeys("steamAppId"))
        .Handle(command, CancellationToken.None);

    dto.Fields.Should().Contain(f => f.Key == "steamAppId" && f.Label == "改過的名稱");
}
```

- [ ] Step 2：跑單檔 → 編譯失敗（建構式參數數量）。修第 119 行為 `new UpdateCategoryCommandHandler(_repository.Object, _time, new StubProtectedKeys())` 後再跑，應是第一個新測試因沒擲例外而紅。
- [ ] Step 3：最小實作——`UpdateCategoryCommandHandler`：

```csharp
public sealed class UpdateCategoryCommandHandler(
    ICategoryRepository repository,
    TimeProvider timeProvider,
    IProtectedFieldKeys protectedKeys)
    : IRequestHandler<UpdateCategoryCommand, CategoryDto>
{
    public async Task<CategoryDto> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var id = ObjectId.Parse(request.Id);
        var existing = await repository.GetAsync(id, cancellationToken)
                       ?? throw new NotFoundException(nameof(Category), request.Id);

        // PUT 的語意是宣告集合的置換：請求裡沒有的既有鍵就是撤回宣告（ADR-0012 §二）。
        // 撤回不刪品項上的值，但受保護的鍵連撤回都不行——來源是用它找到欄位的。
        var requested = request.Fields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var withdrawnProtected = existing.Fields
            .Select(f => f.Key)
            .Where(k => !requested.Contains(k) && protectedKeys.IsProtected(k))
            .ToArray();

        if (withdrawnProtected.Length > 0)
        {
            throw new ValidationException(withdrawnProtected.Select(k =>
                new ValidationFailure("Fields", $"'{k}' is a provider field and cannot be removed.")));
        }

        existing.Name = request.Name.Trim();
        // …其餘照舊
    }
}
```

（`using FluentValidation.Results;` 需補。）

- [ ] Step 4：單檔 → 既有 + 3 全綠
- [ ] Step 5：全部 → 576 + 3 = 579
- [ ] Step 6：Commit `feat(categories): PUT 不得撤回受保護欄位`；`git add src/MyCollection.Application/Categories/CategoryCommands.cs tests/MyCollection.Tests/Unit/CategoryCommandTests.cs`

---

## Task 4：有品項的品類不可刪除

**Files:** Modify: `src/MyCollection.Application/Items/IItemRepository.cs`、`src/MyCollection.Infrastructure/Mongo/MongoItemRepository.cs`、`src/MyCollection.Application/Categories/CategoryCommands.cs`、`tests/MyCollection.Tests/Unit/CategoryCommandTests.cs`、`tests/MyCollection.Tests/Integration/MongoItemRepositoryTests.cs`

順序要對：先 `GetAsync` 確認存在與擁有權（系統品類 → 403，這比現況的 404 更誠實），再計數，再刪。若先計數，使用者在系統品類下有品項時會拿到 409 而不是 403。

- [ ] Step 1a：整合測試（`MongoItemRepositoryTests`，沿用該檔既有的 `NewItem` helper 與 `_sut`）

```csharp
[Fact]
public async Task CountByCategoryAsync_counts_only_own_items_in_that_category()
{
    var category = ObjectId.GenerateNewId();
    var otherCategory = ObjectId.GenerateNewId();
    await fixture.Context.Items.InsertManyAsync(
    [
        NewItem(Owner, "a", category),
        NewItem(Owner, "b", category),
        NewItem(Owner, "c", otherCategory),
        NewItem(OtherOwner, "d", category)
    ]);

    var count = await _sut.CountByCategoryAsync(category, CancellationToken.None);

    count.Should().Be(2);
}
```

（該檔已有 `Owner` / `OtherOwner` 與 `NewItem(owner, name, categoryId)` helper，直接沿用。）

- [ ] Step 1b：單元測試（`CategoryCommandTests`）

```csharp
private readonly Mock<IItemRepository> _items = new();

[Fact]
public async Task Delete_rejects_category_that_still_has_items()
{
    var existing = ExistingCategory("brand");
    _repository.Setup(r => r.GetAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
    _items.Setup(i => i.CountByCategoryAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(3);

    var act = () => new DeleteCategoryCommandHandler(_repository.Object, _items.Object)
        .Handle(new DeleteCategoryCommand(existing.Id.ToString()), CancellationToken.None);

    var ex = await act.Should().ThrowAsync<ConflictException>();
    ex.Which.Message.Should().Contain("3");
    _repository.Verify(r => r.DeleteAsync(It.IsAny<ObjectId>(), It.IsAny<CancellationToken>()), Times.Never);
}

[Fact]
public async Task Delete_removes_category_without_items()
{
    var existing = ExistingCategory("brand");
    _repository.Setup(r => r.GetAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
    _items.Setup(i => i.CountByCategoryAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);

    await new DeleteCategoryCommandHandler(_repository.Object, _items.Object)
        .Handle(new DeleteCategoryCommand(existing.Id.ToString()), CancellationToken.None);

    _repository.Verify(r => r.DeleteAsync(existing.Id, It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task Delete_forbids_system_category_before_counting()
{
    var system = ExistingCategory("platform");
    system.OwnerId = null;
    _repository.Setup(r => r.GetAsync(system.Id, It.IsAny<CancellationToken>())).ReturnsAsync(system);

    var act = () => new DeleteCategoryCommandHandler(_repository.Object, _items.Object)
        .Handle(new DeleteCategoryCommand(system.Id.ToString()), CancellationToken.None);

    await act.Should().ThrowAsync<ForbiddenException>();
    _items.Verify(i => i.CountByCategoryAsync(It.IsAny<ObjectId>(), It.IsAny<CancellationToken>()), Times.Never);
}
```

- [ ] Step 2：跑兩個檔 → 編譯失敗（介面無此方法、建構式參數）。
- [ ] Step 3：最小實作

`IItemRepository`：
```csharp
/// <summary>自己在該品類下的品項數。刪除品類前的守門。</summary>
Task<long> CountByCategoryAsync(ObjectId categoryId, CancellationToken ct);
```

`MongoItemRepository`（沿用該檔的 owner filter 寫法）：
```csharp
public Task<long> CountByCategoryAsync(ObjectId categoryId, CancellationToken ct) =>
    Items.CountDocumentsAsync(
        Filter.And(
            Filter.Eq(x => x.OwnerId, userContext.UserId),
            Filter.Eq(x => x.CategoryId, categoryId)),
        cancellationToken: ct);
```

`DeleteCategoryCommandHandler`：
```csharp
public sealed class DeleteCategoryCommandHandler(ICategoryRepository repository, IItemRepository items)
    : IRequestHandler<DeleteCategoryCommand>
{
    public async Task Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(request.Id, out var id))
        {
            throw new NotFoundException(nameof(Category), request.Id);
        }

        var existing = await repository.GetAsync(id, cancellationToken)
                       ?? throw new NotFoundException(nameof(Category), request.Id);

        if (existing.OwnerId is null)
        {
            throw new ForbiddenException("System categories cannot be deleted.");
        }

        // 品項不會失去品類、也不會被連帶刪掉（ADR-0012 §五）
        var count = await items.CountByCategoryAsync(id, cancellationToken);
        if (count > 0)
        {
            throw new ConflictException($"Category still has {count} item(s); move or delete them first.");
        }

        await repository.DeleteAsync(id, cancellationToken);
    }
}
```

- [ ] Step 4：兩個檔各自綠
- [ ] Step 5：全部 → 579 + 4 = 583
- [ ] Step 6：Commit `feat(categories): 仍有品項的品類不可刪除`；`git add` 上列五個路徑。

> **Checkpoint A**：到這裡 repo 可收工。回寫：若 Task 3/4 的例外訊息或方法簽章在 review 中改了，更新 Task 5/7 的 snippet。

---

## Task 5：`RenameCategoryFieldCommand`

**Files:** Create: `src/MyCollection.Application/Categories/ICategoryFieldRenamer.cs`、`src/MyCollection.Application/Categories/RenameCategoryFieldCommand.cs`、`tests/MyCollection.Tests/Unit/RenameCategoryFieldCommandTests.cs`

Handler 只做前置檢查，把「全做或全不做」交給 port。容易錯：handler 在呼叫 renamer **之前**就改 `category.Fields` 的 key——那樣 renamer 擲 409 時，回傳前的 in-memory 物件已經被改，雖然沒寫回 DB，但測試若斷言 `dto` 就會誤判。順序是：先 renamer，成功後才改 in-memory 供 DTO。

- [ ] Step 1：寫失敗測試

```csharp
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Categories;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class RenameCategoryFieldCommandTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<ICategoryFieldRenamer> _renamer = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Now));

    private sealed class StubProtectedKeys(params string[] keys) : IProtectedFieldKeys
    {
        private readonly HashSet<string> _keys = new(keys, StringComparer.Ordinal);
        public bool IsProtected(string key) => _keys.Contains(key);
    }

    private static Category Custom(params string[] keys) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = ObjectId.GenerateNewId(),
        Name = "自訂",
        Fields = keys.Select(k => new CategoryField { Key = k, Label = k, Type = FieldType.Text }).ToList(),
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now.AddDays(-1)
    };

    private RenameCategoryFieldCommandHandler Sut(params string[] protectedKeys) =>
        new(_categories.Object, _renamer.Object, new StubProtectedKeys(protectedKeys), _time);

    private void Seed(Category category) =>
        _categories.Setup(r => r.GetAsync(category.Id, It.IsAny<CancellationToken>())).ReturnsAsync(category);

    // ---- validator ----

    [Theory]
    [InlineData("PurchasePrice")]
    [InlineData("purchase price")]
    [InlineData("")]
    public void Validator_rejects_new_key_that_is_not_camel_case(string newKey)
    {
        var result = new RenameCategoryFieldCommandValidator()
            .Validate(new RenameCategoryFieldCommand(ObjectId.GenerateNewId().ToString(), "price", newKey));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_rejects_same_key()
    {
        var result = new RenameCategoryFieldCommandValidator()
            .Validate(new RenameCategoryFieldCommand(ObjectId.GenerateNewId().ToString(), "price", "price"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "NewKey");
    }

    // ---- handler ----

    [Fact]
    public async Task Renames_field_and_reports_moved_items()
    {
        var category = Custom("price", "brand");
        Seed(category);
        _renamer.Setup(r => r.RenameAsync(category.Id, "price", "purchasePrice", Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(12);

        var result = await Sut().Handle(
            new RenameCategoryFieldCommand(category.Id.ToString(), "price", "purchasePrice"), CancellationToken.None);

        result.MovedItemCount.Should().Be(12);
        result.Category.Fields.Select(f => f.Key).Should().Equal("purchasePrice", "brand");
        _renamer.VerifyAll();
    }

    [Fact]
    public async Task Throws_not_found_when_old_key_is_not_declared()
    {
        var category = Custom("brand");
        Seed(category);

        var act = () => Sut().Handle(
            new RenameCategoryFieldCommand(category.Id.ToString(), "price", "purchasePrice"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        _renamer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Rejects_protected_old_key()
    {
        var category = Custom("steamAppId");
        Seed(category);

        var act = () => Sut("steamAppId").Handle(
            new RenameCategoryFieldCommand(category.Id.ToString(), "steamAppId", "appId"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainSingle(e => e.PropertyName == "Key");
        _renamer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Rejects_new_key_that_is_already_declared()
    {
        var category = Custom("price", "purchasePrice");
        Seed(category);

        var act = () => Sut().Handle(
            new RenameCategoryFieldCommand(category.Id.ToString(), "price", "purchasePrice"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainSingle(e => e.PropertyName == "NewKey");
        _renamer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Forbids_system_category()
    {
        var system = Custom("platform");
        system.OwnerId = null;
        Seed(system);

        var act = () => Sut().Handle(
            new RenameCategoryFieldCommand(system.Id.ToString(), "platform", "store"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        _renamer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Does_not_mutate_category_when_renamer_conflicts()
    {
        var category = Custom("price");
        Seed(category);
        _renamer.Setup(r => r.RenameAsync(It.IsAny<ObjectId>(), "price", "purchasePrice", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("2 item(s) already carry 'purchasePrice'."));

        var act = () => Sut().Handle(
            new RenameCategoryFieldCommand(category.Id.ToString(), "price", "purchasePrice"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        category.Fields.Single().Key.Should().Be("price");
    }
}
```

- [ ] Step 2：編譯失敗 → 正確的紅。
- [ ] Step 3：最小實作

`ICategoryFieldRenamer.cs`：
```csharp
using MongoDB.Bson;

namespace MyCollection.Application.Categories;

/// <summary>
/// 改名的「全做或全不做」承諾：schema 的鍵與該品類下所有品項的屬性一起搬，或一起不動。
/// 實作自己管 transaction，不把 session 外露給 Application。
/// </summary>
public interface ICategoryFieldRenamer
{
    /// <summary>
    /// 回傳搬移的品項數。若任一品項同時帶有 oldKey 與 newKey，擲 ConflictException 且不改任何東西——
    /// 未宣告屬性是資料，覆蓋等於刪（ADR-0012 §三）。
    /// </summary>
    Task<long> RenameAsync(ObjectId categoryId, string oldKey, string newKey, DateTime updatedAt, CancellationToken ct);
}
```

`RenameCategoryFieldCommand.cs`：
```csharp
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Categories;

public record RenameCategoryFieldCommand(string CategoryId, string Key, string NewKey) : IRequest<RenameFieldResultDto>;

/// <summary>MovedItemCount 是使用者唯一能確認「真的動到資料」的證據，一定要回。</summary>
public record RenameFieldResultDto(CategoryDto Category, long MovedItemCount);

public sealed class RenameCategoryFieldCommandValidator : AbstractValidator<RenameCategoryFieldCommand>
{
    public RenameCategoryFieldCommandValidator()
    {
        RuleFor(x => x.CategoryId).Must(id => ObjectId.TryParse(id, out _)).WithMessage("Invalid category id.");
        RuleFor(x => x.Key).NotEmpty();
        RuleFor(x => x.NewKey)
            .NotEmpty()
            .Must(k => CategoryRules.FieldKeyPattern.IsMatch(k))
            .WithMessage("Field key must be camelCase (letters and digits, starting with a lowercase letter).")
            .NotEqual(x => x.Key).WithMessage("New key must differ from the current key.");
    }
}

public sealed class RenameCategoryFieldCommandHandler(
    ICategoryRepository categories,
    ICategoryFieldRenamer renamer,
    IProtectedFieldKeys protectedKeys,
    TimeProvider timeProvider)
    : IRequestHandler<RenameCategoryFieldCommand, RenameFieldResultDto>
{
    public async Task<RenameFieldResultDto> Handle(RenameCategoryFieldCommand request, CancellationToken cancellationToken)
    {
        var id = ObjectId.Parse(request.CategoryId);
        var category = await categories.GetAsync(id, cancellationToken)
                       ?? throw new NotFoundException(nameof(Category), request.CategoryId);

        if (category.OwnerId is null)
        {
            throw new ForbiddenException("System categories cannot be modified.");
        }

        var field = category.Fields.FirstOrDefault(f => string.Equals(f.Key, request.Key, StringComparison.Ordinal))
                    ?? throw new NotFoundException(nameof(CategoryField), request.Key);

        if (protectedKeys.IsProtected(request.Key))
        {
            throw new ValidationException([new ValidationFailure("Key", $"'{request.Key}' is a provider field and cannot be renamed.")]);
        }

        if (category.Fields.Any(f => string.Equals(f.Key, request.NewKey, StringComparison.Ordinal)))
        {
            throw new ValidationException([new ValidationFailure("NewKey", $"'{request.NewKey}' is already declared.")]);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var moved = await renamer.RenameAsync(id, request.Key, request.NewKey, now, cancellationToken);

        // renamer 成功後才改 in-memory，供回傳 DTO；擲例外時物件保持原狀
        field.Key = request.NewKey;
        category.UpdatedAt = now;

        return new RenameFieldResultDto(CategoryMapper.ToDto(category), moved);
    }
}
```

- [ ] Step 4：單檔 → `Passed: 10`
- [ ] Step 5：全部 → 583 + 10 = 593
- [ ] Step 6：Commit `feat(categories): 改名命令與 ICategoryFieldRenamer port`；`git add` 三個路徑。

---

## Task 6：`MongoCategoryFieldRenamer`（transaction）

**Files:** Create: `src/MyCollection.Infrastructure/Mongo/MongoCategoryFieldRenamer.cs`、`tests/MyCollection.Tests/Integration/MongoCategoryFieldRenamerTests.cs`；Modify: `src/MyCollection.Infrastructure/DependencyInjection.cs`

三個容易錯的點：(1) `$rename` 會覆蓋目標，所以衝突計數必須在 transaction 內、在 rename 之前；(2) 品項 filter 必須同時帶 `OwnerId` + `CategoryId` + `Exists(old)`——漏 `CategoryId` 會搬到別的品類（不同品類可以宣告同名鍵）；(3) category 的更新用 `arrayFilters`，不要讀出整份 `Fields` 再 `$set` 回去——那會把 transaction 外的併發修改蓋掉。

- [ ] Step 1：寫失敗測試

```csharp
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoCategoryFieldRenamerTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId OtherOwner = ObjectId.GenerateNewId();
    private static readonly DateTime Now = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private MongoCategoryFieldRenamer _sut = null!;
    private Category _category = null!;
    private Category _otherCategory = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(c => c.UserId).Returns(Owner);
        userContext.SetupGet(c => c.IsAuthenticated).Returns(true);
        _sut = new MongoCategoryFieldRenamer(fixture.Context, userContext.Object);

        _category = NewCategory(Owner, "price", "brand");
        _otherCategory = NewCategory(Owner, "price");
        await fixture.Context.Categories.InsertManyAsync([_category, _otherCategory]);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Category NewCategory(ObjectId owner, params string[] keys) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = owner,
        Name = "c",
        Fields = keys.Select(k => new CategoryField { Key = k, Label = k, Type = FieldType.Text }).ToList(),
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now.AddDays(-1)
    };

    private static Item NewItem(ObjectId owner, ObjectId categoryId, BsonDocument attributes) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = owner,
        CategoryId = categoryId,
        Name = "i",
        Attributes = attributes,
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now.AddDays(-1)
    };

    private Task<Item> Load(ObjectId id) => fixture.Context.Items.Find(i => i.Id == id).SingleAsync();

    [Fact]
    public async Task Moves_attribute_on_own_items_of_that_category_only()
    {
        var mine = NewItem(Owner, _category.Id, new BsonDocument { ["price"] = 100, ["brand"] = "GSC" });
        var mineWithout = NewItem(Owner, _category.Id, new BsonDocument { ["brand"] = "ALTER" });
        var otherCategory = NewItem(Owner, _otherCategory.Id, new BsonDocument { ["price"] = 5 });
        var otherOwner = NewItem(OtherOwner, _category.Id, new BsonDocument { ["price"] = 7 });
        await fixture.Context.Items.InsertManyAsync([mine, mineWithout, otherCategory, otherOwner]);

        var moved = await _sut.RenameAsync(_category.Id, "price", "purchasePrice", Now, CancellationToken.None);

        moved.Should().Be(1);
        (await Load(mine.Id)).Attributes.Should().Be(new BsonDocument { ["brand"] = "GSC", ["purchasePrice"] = 100 });
        (await Load(mineWithout.Id)).Attributes.Should().Be(new BsonDocument { ["brand"] = "ALTER" });
        (await Load(otherCategory.Id)).Attributes.Should().Be(new BsonDocument { ["price"] = 5 });
        (await Load(otherOwner.Id)).Attributes.Should().Be(new BsonDocument { ["price"] = 7 });
    }

    [Fact]
    public async Task Updates_schema_key_in_place_and_touches_updated_at()
    {
        await _sut.RenameAsync(_category.Id, "price", "purchasePrice", Now, CancellationToken.None);

        var stored = await fixture.Context.Categories.Find(c => c.Id == _category.Id).SingleAsync();
        stored.Fields.Select(f => f.Key).Should().Equal("purchasePrice", "brand");
        stored.Fields[0].Label.Should().Be("price"); // 只動 key
        stored.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Leaves_items_that_only_carry_the_new_key_untouched()
    {
        // Q6 情況 3：新鍵是未宣告屬性、沒有舊鍵 → 放行，它自然變成宣告過的值
        var redeclared = NewItem(Owner, _category.Id, new BsonDocument { ["purchasePrice"] = 42 });
        await fixture.Context.Items.InsertOneAsync(redeclared);

        var moved = await _sut.RenameAsync(_category.Id, "price", "purchasePrice", Now, CancellationToken.None);

        moved.Should().Be(0);
        (await Load(redeclared.Id)).Attributes.Should().Be(new BsonDocument { ["purchasePrice"] = 42 });
    }

    [Fact]
    public async Task Conflict_when_an_item_carries_both_keys_and_nothing_changes()
    {
        var clean = NewItem(Owner, _category.Id, new BsonDocument { ["price"] = 1 });
        var both = NewItem(Owner, _category.Id, new BsonDocument { ["price"] = 2, ["purchasePrice"] = 3 });
        await fixture.Context.Items.InsertManyAsync([clean, both]);

        var act = () => _sut.RenameAsync(_category.Id, "price", "purchasePrice", Now, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Message.Should().Contain("1");

        // 全不做：schema 與所有品項原封不動
        var stored = await fixture.Context.Categories.Find(c => c.Id == _category.Id).SingleAsync();
        stored.Fields.Select(f => f.Key).Should().Equal("price", "brand");
        (await Load(clean.Id)).Attributes.Should().Be(new BsonDocument { ["price"] = 1 });
        (await Load(both.Id)).Attributes.Should().Be(new BsonDocument { ["price"] = 2, ["purchasePrice"] = 3 });
    }

    [Fact]
    public async Task Throws_not_found_for_category_of_another_owner()
    {
        var foreign = NewCategory(OtherOwner, "price");
        await fixture.Context.Categories.InsertOneAsync(foreign);

        var act = () => _sut.RenameAsync(foreign.Id, "price", "purchasePrice", Now, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
```

- [ ] Step 2：編譯失敗 → 正確的紅。
- [ ] Step 3：最小實作

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

/// <summary>
/// 全案第一個跨 collection 的寫入。刻意不做成通用 UnitOfWork：
/// 為一個操作改動每個 repository 的 session 傳遞太貴（ADR-0012 §六 d）。
/// </summary>
public sealed class MongoCategoryFieldRenamer(MongoContext context, IUserContext userContext) : ICategoryFieldRenamer
{
    public async Task<long> RenameAsync(ObjectId categoryId, string oldKey, string newKey, DateTime updatedAt, CancellationToken ct)
    {
        var ownerId = userContext.UserId;
        var oldPath = $"attributes.{oldKey}";
        var newPath = $"attributes.{newKey}";

        var itemFilter = Builders<Item>.Filter;
        var inCategory = itemFilter.And(
            itemFilter.Eq(x => x.OwnerId, ownerId),
            itemFilter.Eq(x => x.CategoryId, categoryId));

        using var session = await context.Database.Client.StartSessionAsync(cancellationToken: ct);

        return await session.WithTransactionAsync(async (s, token) =>
        {
            // 衝突計數必須在 $rename 之前：$rename 會覆蓋目標欄位
            var conflicts = await context.Items.CountDocumentsAsync(s,
                itemFilter.And(inCategory, itemFilter.Exists(oldPath), itemFilter.Exists(newPath)),
                cancellationToken: token);

            if (conflicts > 0)
            {
                throw new ConflictException(
                    $"{conflicts} item(s) already carry '{newKey}' alongside '{oldKey}'; resolve them before renaming.");
            }

            var moved = await context.Items.UpdateManyAsync(s,
                itemFilter.And(inCategory, itemFilter.Exists(oldPath)),
                Builders<Item>.Update.Rename(oldPath, newPath),
                cancellationToken: token);

            // arrayFilters 只改命中的那個元素，不整包置換 fields
            var categoryResult = await context.Categories.UpdateOneAsync(s,
                Builders<Category>.Filter.And(
                    Builders<Category>.Filter.Eq(x => x.Id, categoryId),
                    Builders<Category>.Filter.Eq(x => x.OwnerId, ownerId)),
                Builders<Category>.Update
                    .Set("fields.$[f].key", newKey)
                    .Set(x => x.UpdatedAt, updatedAt),
                new UpdateOptions
                {
                    ArrayFilters = [new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument("f.key", oldKey))]
                },
                token);

            if (categoryResult.MatchedCount == 0)
            {
                throw new NotFoundException(nameof(Category), categoryId);
            }

            return moved.ModifiedCount;
        }, cancellationToken: ct);
    }
}
```

`DependencyInjection.cs`：`services.AddScoped<ICategoryFieldRenamer, MongoCategoryFieldRenamer>();`

- [ ] Step 4：單檔 → `Passed: 5`
- [ ] Step 5：全部 → 593 + 5 = 598
- [ ] Step 6：Commit `feat(categories): MongoCategoryFieldRenamer 在 transaction 內搬移屬性`；`git add` 三個路徑。

> **Checkpoint B**：可收工。回寫：`WithTransactionAsync` 的 callback 內擲例外時 driver 會 abort 並 rethrow——若實測發現被包成 `MongoException`，Task 7 的端點測試會抓到，屆時在此處記錄修法。

---

## Task 7：端點、DI、端點整合測試

**Files:** Create: `tests/MyCollection.Tests/Integration/CategoryEndpointsTests.cs`；Modify: `src/MyCollection.Api/Endpoints/CategoryEndpoints.cs`

端到端走一次：建品類 → 建品項 → 改名 → 200 帶 `movedItemCount` → 重讀品項看到新鍵 → 刪有品項的品類 409。這是唯一能證明 `GlobalExceptionHandler`、validator pipeline、transaction 三者接在一起的測試。

- [ ] Step 1：寫失敗測試

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MyCollection.Application.Categories;
using MyCollection.Application.Items;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class CategoryEndpointsTests(MongoFixture mongo) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await mongo.ResetAsync();
        _factory = new ApiFactory(mongo);
        _client = await AuthenticatedClient.CreateAsync(_factory, "owner@example.com");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<CategoryDto> CreateCategoryAsync(params string[] keys)
    {
        var response = await _client.PostAsJsonAsync("/categories", new
        {
            name = "公仔",
            icon = "figure",
            kind = "Physical",
            defaultDisplayMode = "List",
            fields = keys.Select(k => new { key = k, label = k, type = "Text", options = (string[]?)null, required = false, searchable = false, showOnCard = false })
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CategoryDto>())!;
    }

    private async Task<string> CreateItemAsync(string categoryId, object attributes)
    {
        var response = await _client.PostAsJsonAsync("/items", new
        {
            categoryId, name = "x", description = (string?)null, tags = Array.Empty<string>(),
            isShowcased = false, attributes, acquisition = (object?)null
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Rename_moves_item_attributes_and_reports_count()
    {
        var category = await CreateCategoryAsync("price", "brand");
        var itemId = await CreateItemAsync(category.Id, new { price = "100", brand = "GSC" });

        var response = await _client.PostAsJsonAsync($"/categories/{category.Id}/fields/price/rename", new { newKey = "purchasePrice" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<RenameFieldResultDto>())!;
        result.MovedItemCount.Should().Be(1);
        result.Category.Fields.Select(f => f.Key).Should().Equal("purchasePrice", "brand");

        var item = await _client.GetFromJsonAsync<JsonElement>($"/items/{itemId}");
        var attributes = item.GetProperty("attributes");
        attributes.GetProperty("purchasePrice").GetString().Should().Be("100");
        attributes.TryGetProperty("price", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Rename_to_declared_key_is_400()
    {
        var category = await CreateCategoryAsync("price", "purchasePrice");

        var response = await _client.PostAsJsonAsync($"/categories/{category.Id}/fields/price/rename", new { newKey = "purchasePrice" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("NewKey");
    }

    [Fact]
    public async Task Rename_of_undeclared_key_is_404()
    {
        var category = await CreateCategoryAsync("brand");

        var response = await _client.PostAsJsonAsync($"/categories/{category.Id}/fields/price/rename", new { newKey = "purchasePrice" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_category_with_items_is_409()
    {
        var category = await CreateCategoryAsync("brand");
        await CreateItemAsync(category.Id, new { brand = "GSC" });

        var response = await _client.DeleteAsync($"/categories/{category.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("1 item");
    }

    [Fact]
    public async Task Put_that_withdraws_provider_field_is_400()
    {
        var category = await CreateCategoryAsync("steamAppId", "brand");

        var response = await _client.PutAsJsonAsync($"/categories/{category.Id}", new
        {
            name = "公仔", icon = "figure", kind = "Physical", defaultDisplayMode = "List",
            fields = new[] { new { key = "brand", label = "brand", type = "Text", options = (string[]?)null, required = false, searchable = false, showOnCard = false } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("steamAppId");
    }
}
```

- [ ] Step 2：第一個測試回 404（路由不存在）→ 正確的紅；`Delete…409` 與 `Put…400` 在 Task 3/4 之後應已綠——這是預期的，它們在此檔是為了端到端覆蓋。
- [ ] Step 3：最小實作——`CategoryEndpoints.cs` 加：

```csharp
group.MapPost("/{id}/fields/{key}/rename", async (
    string id, string key, RenameFieldRequest body, ISender sender, CancellationToken ct) =>
    Results.Ok(await sender.Send(new RenameCategoryFieldCommand(id, key, body.NewKey), ct)));
```
與 `public record RenameFieldRequest(string NewKey);`

- [ ] Step 4：單檔 → `Passed: 5`
- [ ] Step 5：全部 → 598 + 5 = 603；`dotnet build MyCollection.slnx -warnaserror` 0 warnings
- [ ] Step 6：Commit `feat(api): POST /categories/{id}/fields/{key}/rename`；`git add src/MyCollection.Api/Endpoints/CategoryEndpoints.cs tests/MyCollection.Tests/Integration/CategoryEndpointsTests.cs`

---

## 完成後的驗證

- [ ] `dotnet test` 全綠，總數 = 603（565 基準 + 38）
- [ ] `dotnet build MyCollection.slnx -warnaserror` 0 warnings
- [ ] `git status` 乾淨（`web/` 不應有任何變更）
- [ ] `git log master..HEAD --oneline` 恰好 8 顆 commit（Task 0–7）
- [ ] `git diff master..HEAD --stat` 不含 `web/`、`*Fields.cs`、`SystemCategoryDefinitions.cs`
- [ ] Testcontainers 整體執行時間記錄在回寫（replica set 啟動成本）

## 手動驗證

自動測試涵蓋不到的，部署後（走 canary workflow，**不要 `terraform apply`**）在正式環境做：

1. 自訂品類建一個 `price` 欄位、兩筆品項各填值 → 改名為 `purchasePrice` → 回應 `movedItemCount = 2`，庫存頁兩筆品項的值都在新欄位下。
2. 對自訂品類按「接上 Steam」長出 `steamAppId` → 嘗試 PUT 拿掉它 → 400 且訊息點名 `steamAppId`。
3. 刪除仍有品項的品類 → 409 且訊息含品項數。
4. 改名前用 Atlas 直接在某品項寫入未宣告的 `purchasePrice`（同時保留 `price`）→ 改名 → 409，Atlas 上兩個欄位都沒動。

## 後續（不在本計畫內）

- 前端（另一份計畫）。
- Dashboard 🔴 條目更新與 `docs/deployment` 無需變更。
- 未宣告屬性的診斷／清理工具；`CategoryDto.itemCount`；改名同時改型別；新鍵命中受保護集合的規則；通用 UnitOfWork——全部見 spec §3.4。
