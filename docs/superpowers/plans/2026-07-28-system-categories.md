# System Categories Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Seed four immutable system categories on every API startup and make Steam synchronization reuse the correct digital-game category.

**Architecture:** Canonical category definitions and MongoDB upsert behavior live in two focused Infrastructure files. `Program.cs` runs the seeder after index initialization. The Application sync handler selects an existing owner category first, the system category second, and retains its current fallback creation behavior.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, MongoDB.Driver 3.10, xUnit, FluentAssertions, Moq, Testcontainers MongoDB 8

## Global Constraints

- Implement the approved design in `docs/superpowers/specs/2026-07-28-system-categories-neon-grid-design.md`.
- System category IDs are fixed at `000000000000000000000001` through `000000000000000000000004`.
- System categories have `OwnerId = null` and remain immutable through the normal repository.
- All seeded category fields have `Required = false`.
- Seeder reruns update canonical schema without changing `CreatedAt` or creating duplicates.
- Existing user-owned `數位遊戲` categories take precedence over the system category during Steam sync.
- Do not modify or stage the existing user change in `web/angular.json`.
- Treat warnings as errors; do not add suppressions.

---

## File Structure

- Create `src/MyCollection.Infrastructure/Mongo/SystemCategoryDefinitions.cs`: fixed IDs and fresh canonical category objects.
- Create `src/MyCollection.Infrastructure/Mongo/SystemCategorySeeder.cs`: MongoDB upsert orchestration.
- Modify `src/MyCollection.Api/Program.cs`: invoke the seeder after index creation.
- Modify `src/MyCollection.Application/Categories/ICategoryRepository.cs`: remove the now-unused owner-only name lookup.
- Modify `src/MyCollection.Infrastructure/Mongo/MongoCategoryRepository.cs`: remove the corresponding implementation.
- Modify `src/MyCollection.Application/Ingestion/SyncCommand.cs`: owner-first/system-second category selection.
- Create `tests/MyCollection.Tests/Integration/SystemCategorySeederTests.cs`: seed idempotency and schema contract.
- Modify `tests/MyCollection.Tests/Integration/CatalogEndpointsTests.cs`: startup-to-API visibility contract.
- Modify `tests/MyCollection.Tests/Unit/SyncCommandTests.cs`: Steam category precedence and fallback behavior.

### Task 1: Canonical System Category Definitions and Idempotent Seeder

**Files:**
- Create: `src/MyCollection.Infrastructure/Mongo/SystemCategoryDefinitions.cs`
- Create: `src/MyCollection.Infrastructure/Mongo/SystemCategorySeeder.cs`
- Create: `tests/MyCollection.Tests/Integration/SystemCategorySeederTests.cs`

**Interfaces:**
- Produces: `SystemCategoryDefinitions.Create(DateTime now) : IReadOnlyList<Category>`
- Produces: `SystemCategorySeeder.SeedAsync(MongoContext context, TimeProvider timeProvider, CancellationToken ct) : Task`
- Consumes: `MongoContext.Categories`, `TimeProvider`

- [ ] **Step 1: Write the failing integration tests**

Create `tests/MyCollection.Tests/Integration/SystemCategorySeederTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Driver;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public sealed class SystemCategorySeederTests(MongoFixture fixture) : IAsyncLifetime
{
    private readonly FakeTimeProvider _time =
        new(new DateTimeOffset(2026, 7, 28, 6, 0, 0, TimeSpan.Zero));

    public Task DisposeAsync() => Task.CompletedTask;

    public Task InitializeAsync() => fixture.ResetAsync();

    [Fact]
    public async Task SeedAsync_creates_the_four_canonical_system_categories()
    {
        await SystemCategorySeeder.SeedAsync(fixture.Context, _time, CancellationToken.None);

        var categories = await fixture.Context.Categories
            .Find(Builders<Category>.Filter.Eq(x => x.OwnerId, null))
            .SortBy(x => x.Id)
            .ToListAsync();

        categories.Select(x => (x.Id.ToString(), x.Name, x.Kind)).Should().Equal(
            ("000000000000000000000001", "實體遊戲", CategoryKind.Physical),
            ("000000000000000000000002", "數位遊戲", CategoryKind.Digital),
            ("000000000000000000000003", "音樂專輯", CategoryKind.Physical),
            ("000000000000000000000004", "電影光碟", CategoryKind.Physical));

        categories.SelectMany(x => x.Fields).Should().OnlyContain(x => !x.Required);

        categories.Single(x => x.Name == "實體遊戲").Fields.Select(x => x.Key).Should().Equal(
            "platform", "edition", "region", "mediaFormat", "developer", "publisher",
            "releaseDate", "productCode", "barcode", "condition");
        categories.Single(x => x.Name == "數位遊戲").Fields.Select(x => x.Key).Should().Equal(
            "platform", "developer", "publisher", "releaseDate", "productCode",
            "playtimeForever", "headerUrl", "iconUrl");
        categories.Single(x => x.Name == "音樂專輯").Fields.Select(x => x.Key).Should().Equal(
            "artist", "mediaFormat", "albumType", "label", "catalogNumber",
            "country", "releaseDate", "genre", "style", "barcode");
        categories.Single(x => x.Name == "電影光碟").Fields.Select(x => x.Key).Should().Equal(
            "discFormat", "edition", "director", "studio", "regionCode",
            "country", "releaseDate", "genre", "barcode");
    }

    [Fact]
    public async Task SeedAsync_is_idempotent_and_preserves_created_at()
    {
        await SystemCategorySeeder.SeedAsync(fixture.Context, _time, CancellationToken.None);
        var first = await fixture.Context.Categories
            .Find(Builders<Category>.Filter.Empty)
            .SortBy(x => x.Id)
            .ToListAsync();

        _time.Advance(TimeSpan.FromHours(1));
        await SystemCategorySeeder.SeedAsync(fixture.Context, _time, CancellationToken.None);
        var second = await fixture.Context.Categories
            .Find(Builders<Category>.Filter.Empty)
            .SortBy(x => x.Id)
            .ToListAsync();

        second.Should().HaveCount(4);
        second.Select(x => x.CreatedAt).Should().Equal(first.Select(x => x.CreatedAt));
        second.Select(x => x.UpdatedAt).Should()
            .OnlyContain(x => x == new DateTime(2026, 7, 28, 7, 0, 0, DateTimeKind.Utc));
    }
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test tests/MyCollection.Tests/MyCollection.Tests.csproj --filter FullyQualifiedName~SystemCategorySeederTests
```

Expected: build fails because `SystemCategorySeeder` does not exist. This is the expected RED reason.

- [ ] **Step 3: Add the canonical definitions**

Create `src/MyCollection.Infrastructure/Mongo/SystemCategoryDefinitions.cs`. Use helpers so every call returns new mutable entities and option lists:

```csharp
using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Mongo;

public static class SystemCategoryDefinitions
{
    public static readonly ObjectId PhysicalGameId = ObjectId.Parse("000000000000000000000001");
    public static readonly ObjectId DigitalGameId = ObjectId.Parse("000000000000000000000002");
    public static readonly ObjectId MusicAlbumId = ObjectId.Parse("000000000000000000000003");
    public static readonly ObjectId MovieDiscId = ObjectId.Parse("000000000000000000000004");

    public static IReadOnlyList<Category> Create(DateTime now) =>
    [
        Category(PhysicalGameId, "實體遊戲", "gamepad-2", CategoryKind.Physical, now,
        [
            Text("platform", "平台", searchable: true, showOnCard: true),
            Text("edition", "版本", searchable: true, showOnCard: true),
            Text("region", "區域", searchable: true),
            Select("mediaFormat", "媒體格式", ["光碟", "卡匣", "記憶卡", "其他"], true, true),
            Text("developer", "開發商", searchable: true),
            Text("publisher", "發行商", searchable: true),
            Date("releaseDate", "發售日期"),
            Text("productCode", "產品編號", searchable: true),
            Text("barcode", "條碼", searchable: true),
            Select("condition", "保存狀況", ["全新", "近全新", "良好", "普通", "需修復"], true)
        ]),
        Category(DigitalGameId, "數位遊戲", "gamepad-2", CategoryKind.Digital, now,
        [
            Text("platform", "平台／商店", searchable: true, showOnCard: true),
            Text("developer", "開發商", searchable: true),
            Text("publisher", "發行商", searchable: true, showOnCard: true),
            Date("releaseDate", "發售日期"),
            Text("productCode", "產品編號", searchable: true),
            Number("playtimeForever", "遊玩時數（分鐘）", showOnCard: true),
            Url("headerUrl", "封面圖網址"),
            Url("iconUrl", "圖示網址")
        ]),
        Category(MusicAlbumId, "音樂專輯", "disc-3", CategoryKind.Physical, now,
        [
            Text("artist", "演出者", searchable: true, showOnCard: true),
            Select("mediaFormat", "媒體格式", ["CD", "黑膠唱片", "卡帶", "SACD", "其他"], true, true),
            Select("albumType", "專輯類型", ["專輯", "單曲", "EP", "精選輯", "原聲帶", "其他"], true),
            Text("label", "唱片公司", searchable: true, showOnCard: true),
            Text("catalogNumber", "目錄編號", searchable: true),
            Text("country", "國家／地區", searchable: true),
            Date("releaseDate", "發行日期"),
            Text("genre", "曲風", searchable: true),
            Text("style", "風格", searchable: true),
            Text("barcode", "條碼", searchable: true)
        ]),
        Category(MovieDiscId, "電影光碟", "film", CategoryKind.Physical, now,
        [
            Select("discFormat", "光碟格式", ["Blu-ray", "4K UHD", "DVD", "VCD", "其他"], true, true),
            Text("edition", "版本", searchable: true, showOnCard: true),
            Text("director", "導演", searchable: true, showOnCard: true),
            Text("studio", "片商", searchable: true),
            Text("regionCode", "區碼", searchable: true),
            Text("country", "國家／地區", searchable: true),
            Date("releaseDate", "發行日期"),
            Text("genre", "類型", searchable: true),
            Text("barcode", "條碼", searchable: true)
        ])
    ];

    private static Category Category(
        ObjectId id, string name, string icon, CategoryKind kind, DateTime now, List<CategoryField> fields) =>
        new()
        {
            Id = id,
            OwnerId = null,
            Name = name,
            Icon = icon,
            Kind = kind,
            Fields = fields,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static CategoryField Text(
        string key, string label, bool searchable = false, bool showOnCard = false) =>
        Field(key, label, FieldType.Text, searchable, showOnCard);

    private static CategoryField Date(string key, string label) =>
        Field(key, label, FieldType.Date);

    private static CategoryField Number(string key, string label, bool showOnCard = false) =>
        Field(key, label, FieldType.Number, showOnCard: showOnCard);

    private static CategoryField Url(string key, string label) =>
        Field(key, label, FieldType.Url);

    private static CategoryField Select(
        string key, string label, List<string> options, bool searchable, bool showOnCard = false) =>
        Field(key, label, FieldType.Select, searchable, showOnCard, options);

    private static CategoryField Field(
        string key,
        string label,
        FieldType type,
        bool searchable = false,
        bool showOnCard = false,
        List<string>? options = null) =>
        new()
        {
            Key = key,
            Label = label,
            Type = type,
            Options = options,
            Required = false,
            Searchable = searchable,
            ShowOnCard = showOnCard
        };
}
```

- [ ] **Step 4: Implement the MongoDB upsert seeder**

Create `src/MyCollection.Infrastructure/Mongo/SystemCategorySeeder.cs`:

```csharp
using MongoDB.Driver;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Mongo;

public static class SystemCategorySeeder
{
public static async Task SeedAsync(
        MongoContext context,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var writes = SystemCategoryDefinitions.Create(now)
            .Select(category => new UpdateOneModel<Category>(
                Builders<Category>.Filter.Eq(x => x.Id, category.Id),
                Builders<Category>.Update
                    .Set(x => x.OwnerId, (MongoDB.Bson.ObjectId?)null)
                    .Set(x => x.Name, category.Name)
                    .Set(x => x.Icon, category.Icon)
                    .Set(x => x.Kind, category.Kind)
                    .Set(x => x.Fields, category.Fields)
                    .Set(x => x.UpdatedAt, now)
                    .SetOnInsert(x => x.CreatedAt, now))
            {
                IsUpsert = true
            })
            .Cast<WriteModel<Category>>()
            .ToArray();

        await context.Categories.BulkWriteAsync(writes, cancellationToken: ct);
    }
}
```

- [ ] **Step 5: Run the focused tests and verify GREEN**

Run:

```powershell
dotnet test tests/MyCollection.Tests/MyCollection.Tests.csproj --filter FullyQualifiedName~SystemCategorySeederTests
```

Expected: 2 passed, 0 failed.

- [ ] **Step 6: Commit Task 1**

```powershell
git add src/MyCollection.Infrastructure/Mongo/SystemCategoryDefinitions.cs src/MyCollection.Infrastructure/Mongo/SystemCategorySeeder.cs tests/MyCollection.Tests/Integration/SystemCategorySeederTests.cs
git commit -m "feat: add idempotent system category seeder"
```

### Task 2: Seed Categories During API Startup

**Files:**
- Modify: `src/MyCollection.Api/Program.cs`
- Modify: `tests/MyCollection.Tests/Integration/CatalogEndpointsTests.cs`

**Interfaces:**
- Consumes: `SystemCategorySeeder.SeedAsync(MongoContext, TimeProvider, CancellationToken)`
- Produces: authenticated `GET /categories` includes four `IsSystem = true` DTOs on a clean database

- [ ] **Step 1: Add the failing API startup test**

Add to `CatalogEndpointsTests`:

```csharp
[Fact]
public async Task Api_startup_exposes_the_four_system_categories()
{
    var categories = await _client.GetFromJsonAsync<CategoryDto[]>("/categories");

    categories!
        .Where(x => x.IsSystem)
        .Select(x => x.Name)
        .Should()
        .BeEquivalentTo("實體遊戲", "數位遊戲", "音樂專輯", "電影光碟");
}
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet test tests/MyCollection.Tests/MyCollection.Tests.csproj --filter FullyQualifiedName~Api_startup_exposes_the_four_system_categories
```

Expected: FAIL because the API startup currently initializes only indexes.

- [ ] **Step 3: Wire the seeder into `Program.cs`**

Change the existing startup scope:

```csharp
await using (var scope = app.Services.CreateAsyncScope())
{
    var context = scope.ServiceProvider.GetRequiredService<MongoContext>();
    var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

    await MongoIndexInitializer.EnsureIndexesAsync(context, CancellationToken.None);
    await SystemCategorySeeder.SeedAsync(context, timeProvider, CancellationToken.None);
}
```

- [ ] **Step 4: Run the startup test and the catalog integration class**

Run:

```powershell
dotnet test tests/MyCollection.Tests/MyCollection.Tests.csproj --filter FullyQualifiedName~CatalogEndpointsTests
```

Expected: all `CatalogEndpointsTests` pass. The existing custom category tests continue to work with the four additional visible system categories.

- [ ] **Step 5: Commit Task 2**

```powershell
git add src/MyCollection.Api/Program.cs tests/MyCollection.Tests/Integration/CatalogEndpointsTests.cs
git commit -m "feat: seed system categories at api startup"
```

### Task 3: Prefer Existing Owner Category, Then System Category, During Steam Sync

**Files:**
- Modify: `tests/MyCollection.Tests/Unit/SyncCommandTests.cs`
- Modify: `src/MyCollection.Application/Ingestion/SyncCommand.cs`
- Modify: `src/MyCollection.Application/Categories/ICategoryRepository.cs`
- Modify: `src/MyCollection.Infrastructure/Mongo/MongoCategoryRepository.cs`

**Interfaces:**
- Consumes: `ICategoryRepository.ListAsync(CancellationToken) : Task<IReadOnlyList<Category>>`
- Produces: category selection order owner-owned `數位遊戲` → system `數位遊戲` → newly created fallback
- Removes: `ICategoryRepository.FindByNameAsync`

- [ ] **Step 1: Replace the existing sync lookup setup with owner-first/system-second tests**

In the test constructor, replace the `FindByNameAsync` setup with:

```csharp
_categories.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
    .ReturnsAsync(
    [
        new Category
        {
            Id = GameCategoryId,
            OwnerId = Owner,
            Name = "數位遊戲",
            Kind = CategoryKind.Digital
        }
    ]);
```

Replace `Creates_the_digital_category_when_missing` lookup setup with:

```csharp
_categories.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
    .ReturnsAsync([]);
```

Make the same replacement in `Auto_created_category_declares_the_fields_steam_produces`.

Add these tests:

```csharp
[Fact]
public async Task Uses_the_system_digital_category_when_no_owner_category_exists()
{
    var systemId = ObjectId.Parse("000000000000000000000002");
    _categories.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(
        [
            new Category
            {
                Id = systemId,
                OwnerId = null,
                Name = "數位遊戲",
                Kind = CategoryKind.Digital
            }
        ]);

    ObjectId? usedCategory = null;
    _writer.Setup(w => w.UpsertAsync(
            Owner, It.IsAny<ObjectId>(), ItemSource.Steam, "steam",
            It.IsAny<IReadOnlyList<ExternalItem>>(), It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
        .Callback<ObjectId, ObjectId, ItemSource, string, IReadOnlyList<ExternalItem>, DateTime, CancellationToken>(
            (_, categoryId, _, _, _, _, _) => usedCategory = categoryId)
        .ReturnsAsync(new SyncOutcome(1, 0, 0));

    await CreateSut().Handle(new SyncCommand("steam"), CancellationToken.None);

    usedCategory.Should().Be(systemId);
    _categories.Verify(
        x => x.InsertAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()),
        Times.Never);
}

[Fact]
public async Task Owner_digital_category_takes_precedence_over_system_category()
{
    var systemId = ObjectId.Parse("000000000000000000000002");
    _categories.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(
        [
            new Category { Id = systemId, OwnerId = null, Name = "數位遊戲", Kind = CategoryKind.Digital },
            new Category { Id = GameCategoryId, OwnerId = Owner, Name = "數位遊戲", Kind = CategoryKind.Digital }
        ]);

    ObjectId? usedCategory = null;
    _writer.Setup(w => w.UpsertAsync(
            Owner, It.IsAny<ObjectId>(), ItemSource.Steam, "steam",
            It.IsAny<IReadOnlyList<ExternalItem>>(), It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
        .Callback<ObjectId, ObjectId, ItemSource, string, IReadOnlyList<ExternalItem>, DateTime, CancellationToken>(
            (_, categoryId, _, _, _, _, _) => usedCategory = categoryId)
        .ReturnsAsync(new SyncOutcome(1, 0, 0));

    await CreateSut().Handle(new SyncCommand("steam"), CancellationToken.None);

    usedCategory.Should().Be(GameCategoryId);
}
```

- [ ] **Step 2: Run the sync tests and verify RED**

Run:

```powershell
dotnet test tests/MyCollection.Tests/MyCollection.Tests.csproj --filter FullyQualifiedName~SyncCommandTests
```

Expected: FAIL because `SyncCommandHandler` still calls `FindByNameAsync` and cannot select the system category.

- [ ] **Step 3: Implement the selection order**

Replace `EnsureDigitalCategoryAsync` lookup with:

```csharp
private async Task<Category> EnsureDigitalCategoryAsync(DateTime now, CancellationToken ct)
{
    var existing = (await categories.ListAsync(ct))
        .Where(x => string.Equals(x.Name, DigitalCategoryName, StringComparison.Ordinal))
        .OrderBy(x => x.OwnerId is null)
        .FirstOrDefault();

    if (existing is not null)
    {
        return existing;
    }

    var category = new Category
    {
        Id = ObjectId.GenerateNewId(),
        Name = DigitalCategoryName,
        Icon = "gamepad-2",
        Kind = CategoryKind.Digital,
        Fields = DigitalCategoryFields(),
        CreatedAt = now,
        UpdatedAt = now
    };

    await categories.InsertAsync(category, ct);
    return category;
}
```

`OrderBy(x => x.OwnerId is null)` sorts `false` before `true`, so the owner category wins.

- [ ] **Step 4: Remove the dead name lookup interface**

Delete this member from `ICategoryRepository`:

```csharp
Task<Category?> FindByNameAsync(string name, CancellationToken ct);
```

Delete the full expression-bodied `FindByNameAsync` member from `MongoCategoryRepository`. It starts with the following signature and ends at its `FirstOrDefaultAsync(ct)!;` call:

```csharp
public Task<Category?> FindByNameAsync(string name, CancellationToken ct)
```

Run:

```powershell
rg "FindByNameAsync" src tests
```

Expected: no matches.

- [ ] **Step 5: Run the sync tests and verify GREEN**

Run:

```powershell
dotnet test tests/MyCollection.Tests/MyCollection.Tests.csproj --filter FullyQualifiedName~SyncCommandTests
```

Expected: all `SyncCommandTests` pass, including fallback creation tests.

- [ ] **Step 6: Commit Task 3**

```powershell
git add tests/MyCollection.Tests/Unit/SyncCommandTests.cs src/MyCollection.Application/Ingestion/SyncCommand.cs src/MyCollection.Application/Categories/ICategoryRepository.cs src/MyCollection.Infrastructure/Mongo/MongoCategoryRepository.cs
git commit -m "fix: reuse system category during steam sync"
```

### Task 4: Backend Regression Verification

**Files:**
- No production file changes expected.
- Update this plan only if execution discovers an inaccurate command or interface.

**Interfaces:**
- Verifies the complete backend behavior delivered by Tasks 1–3.

- [ ] **Step 1: Run all .NET tests**

```powershell
dotnet test
```

Expected: all tests pass with 0 warnings and 0 failures. Docker must be running for Testcontainers.

- [ ] **Step 2: Inspect the final diff**

```powershell
git status --short
git diff --check
git diff HEAD~3 -- src tests
```

Expected:

- No accidental change to `web/angular.json`.
- No `.superpowers/` files staged.
- Only the files listed in this plan changed in the three implementation commits.
- `git diff --check` produces no output.

- [ ] **Step 3: Record the backend checkpoint**

Do not create an empty commit. Record the `dotnet test` command and pass count in the execution handoff before starting the frontend plan.
