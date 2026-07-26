# Plan 2：Schema + Catalog 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **前置：** Plan 1 已完成並全綠。

**Goal:** 實作 `categories` schema 定義 CRUD 與 `items` 完整 CRUD，含由 schema 動態產生的 `attributes` 驗證、標籤/品類篩選、全文搜尋與分頁，授權在 Repository 層以 `ownerId` 強制。

**Architecture:** `Category.Fields`（`CategoryField[]`）是唯一真相來源，同時餵給後端驗證與（Plan 5 的）前端動態表單。`attributes` 以 `BsonDocument` 存放，API 邊界用 `JsonElement` ⇄ `BsonDocument` 雙向轉換。所有 Mongo filter 由 `Builders<T>.Filter.Eq(x => x.OwnerId, userContext.UserId)` 起頭。

**Tech Stack:** 同 Plan 1。

---

## 檔案結構

| 檔案 | 職責 |
|---|---|
| `src/MyCollection.Domain/Entities/Category.cs` | `Category` `CategoryField` `CategoryKind` `FieldType` |
| `src/MyCollection.Domain/Entities/Item.cs` | `Item` `ItemImage` `ExternalRef` `Acquisition` `Money` |
| `src/MyCollection.Application/Common/BsonJson.cs` | `JsonElement` ⇄ `BsonDocument` 轉換 |
| `src/MyCollection.Application/Common/PagedResult.cs` | 分頁結果 record |
| `src/MyCollection.Application/Categories/ICategoryRepository.cs` | 品類存取契約 |
| `src/MyCollection.Application/Categories/CategoryDtos.cs` | DTO |
| `src/MyCollection.Application/Categories/*Command.cs` `*Query.cs` | 品類 CRUD |
| `src/MyCollection.Application/Items/IItemRepository.cs` | 品項存取契約（含 `ItemQuerySpec`） |
| `src/MyCollection.Application/Items/AttributeValidator.cs` | schema → `attributes` 驗證 |
| `src/MyCollection.Application/Items/ItemDtos.cs` `ItemMapper.cs` | DTO 與映射 |
| `src/MyCollection.Application/Items/*Command.cs` `*Query.cs` | 品項 CRUD 與搜尋 |
| `src/MyCollection.Infrastructure/Mongo/MongoCategoryRepository.cs` | |
| `src/MyCollection.Infrastructure/Mongo/MongoItemRepository.cs` | |
| `src/MyCollection.Api/Endpoints/CategoryEndpoints.cs` `ItemEndpoints.cs` | 路由 |

---

### Task 1：Category 與 Item 實體

**Files:**
- Create: `src/MyCollection.Domain/Entities/Category.cs`
- Create: `src/MyCollection.Domain/Entities/Item.cs`
- Modify: `src/MyCollection.Infrastructure/Mongo/MongoContext.cs`
- Test: `tests/MyCollection.Tests/Unit/EntitySerializationTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/EntitySerializationTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;

namespace MyCollection.Tests.Unit;

public class EntitySerializationTests
{
    public EntitySerializationTests() => MongoConventions.Register();

    [Fact]
    public void Category_serialises_enums_as_strings()
    {
        var category = new Category
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = ObjectId.GenerateNewId(),
            Name = "公仔",
            Icon = "figure",
            Kind = CategoryKind.Physical,
            Fields =
            [
                new CategoryField
                {
                    Key = "brand", Label = "廠商", Type = FieldType.Select,
                    Options = ["Good Smile", "ALTER"],
                    Required = true, Searchable = true, ShowOnCard = true
                }
            ],
            // 必填：未指定的 DateTime 是 MinValue/Unspecified，會被 UtcOnlyDateTimeSerializer 拒絕
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var doc = category.ToBsonDocument();

        doc["kind"].AsString.Should().Be("Physical");
        doc["fields"][0]["type"].AsString.Should().Be("Select");
        doc["fields"][0]["key"].AsString.Should().Be("brand");
    }

    [Fact]
    public void Item_roundtrips_nested_attributes_document()
    {
        var item = new Item
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = ObjectId.GenerateNewId(),
            CategoryId = ObjectId.GenerateNewId(),
            Name = "初音ミク 1/8 スケール",
            Source = ItemSource.Manual,
            Attributes = new BsonDocument
            {
                { "brand", "Good Smile" },
                { "spec", new BsonDocument { { "scale", "1/8" }, { "height", 200 } } }
            },
            Acquisition = new Acquisition
            {
                AcquiredAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Price = new Money(12800m, "TWD"),
                Vendor = "GSC 官網"
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var restored = BsonSerializer.Deserialize<Item>(item.ToBsonDocument());

        restored.Attributes["spec"]["scale"].AsString.Should().Be("1/8");
        restored.Acquisition!.Price!.Amount.Should().Be(12800m);
        restored.Acquisition.Price.Currency.Should().Be("TWD");
        restored.Source.Should().Be(ItemSource.Manual);
        restored.LocationId.Should().BeNull();
        restored.Tags.Should().BeEmpty();
    }
}
```

需 `using MongoDB.Bson.Serialization;`。

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter EntitySerializationTests`
Expected: 編譯失敗，找不到 `Category` / `Item`。

- [ ] **Step 3: 實作實體**

`src/MyCollection.Domain/Entities/Category.cs`：

```csharp
using MongoDB.Bson;

namespace MyCollection.Domain.Entities;

public enum CategoryKind
{
    /// <summary>實體收藏，可有位置與購入資訊。</summary>
    Physical,

    /// <summary>數位收藏，LocationId 恆為 null，可被 Provider 同步。</summary>
    Digital
}

public enum FieldType
{
    Text,
    Number,
    Date,
    Select,
    Bool,
    Url
}

public sealed class CategoryField
{
    public required string Key { get; set; }
    public required string Label { get; set; }
    public FieldType Type { get; set; }

    /// <summary>僅 <see cref="FieldType.Select"/> 使用。</summary>
    public List<string>? Options { get; set; }

    public bool Required { get; set; }
    public bool Searchable { get; set; }
    public bool ShowOnCard { get; set; }
}

public sealed class Category
{
    public ObjectId Id { get; set; }

    /// <summary>null = 系統內建品類，所有使用者可見但不可編輯。</summary>
    public ObjectId? OwnerId { get; set; }

    public required string Name { get; set; }
    public string Icon { get; set; } = "box";
    public CategoryKind Kind { get; set; } = CategoryKind.Physical;
    public List<CategoryField> Fields { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

`src/MyCollection.Domain/Entities/Item.cs`：

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCollection.Domain.Entities;

public enum ItemSource
{
    Manual,
    Steam,
    OpenGraph
}

public sealed class ItemImage
{
    /// <summary>圖片在品項內的識別碼，用於 DELETE 路由。</summary>
    public required string Id { get; set; }

    /// <summary>full 尺寸的儲存相對路徑。</summary>
    public required string Path { get; set; }

    public required string CardPath { get; set; }
    public required string ThumbPath { get; set; }

    public bool IsPrimary { get; set; }
    public int Order { get; set; }
}

public sealed class ExternalRef
{
    public required string Provider { get; set; }
    public required string ExternalId { get; set; }
    public string? Url { get; set; }
    public DateTime LastSyncedAt { get; set; }
}

public sealed record Money(decimal Amount, string Currency);

public sealed class Acquisition
{
    public DateTime? AcquiredAt { get; set; }
    public Money? Price { get; set; }
    public string? Vendor { get; set; }
}

public sealed class Item
{
    public ObjectId Id { get; set; }
    public ObjectId OwnerId { get; set; }
    public ObjectId CategoryId { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }

    public List<ItemImage> Images { get; set; } = [];
    public List<string> Tags { get; set; } = [];

    public bool IsShowcased { get; set; }
    public ItemSource Source { get; set; } = ItemSource.Manual;
    public ExternalRef? ExternalRef { get; set; }
    public Acquisition? Acquisition { get; set; }

    /// <summary>位置階層第一版不實作，欄位先保留。Digital 品類恆為 null。</summary>
    public ObjectId? LocationId { get; set; }

    /// <summary>品類 schema 定義的自訂欄位。BsonDocument 天然支援巢狀結構。</summary>
    [BsonElement("attributes")]
    public BsonDocument Attributes { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

`Money` 的 `decimal` 需以 `Decimal128` 存放；在 `MongoConventions.Register()` 內追加：

```csharp
        BsonSerializer.RegisterSerializer(new DecimalSerializer(BsonType.Decimal128));
        BsonSerializer.RegisterSerializer(new NullableSerializer<decimal>(new DecimalSerializer(BsonType.Decimal128)));
```

（`MongoConventions.cs` 頂端已有 `using MongoDB.Bson.Serialization.Serializers;`。）

- [ ] **Step 4: 擴充 MongoContext**

`src/MyCollection.Infrastructure/Mongo/MongoContext.cs` 新增屬性：

```csharp
    public IMongoCollection<Category> Categories => Database.GetCollection<Category>("categories");

    public IMongoCollection<Item> Items => Database.GetCollection<Item>("items");
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test --filter EntitySerializationTests`
Expected: `Passed: 2`

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(domain): 新增 Category 與 Item 實體"
```

---

### Task 2：索引擴充

**Files:**
- Modify: `src/MyCollection.Infrastructure/Mongo/MongoIndexInitializer.cs`
- Modify: `tests/MyCollection.Tests/Fixtures/MongoFixture.cs`
- Test: `tests/MyCollection.Tests/Integration/MongoIndexTests.cs`（追加）

- [ ] **Step 1: 寫失敗測試**

在 `tests/MyCollection.Tests/Integration/MongoIndexTests.cs` 類別內追加：

```csharp
    [Theory]
    [InlineData("ix_items_showcase")]
    [InlineData("ix_items_category")]
    [InlineData("ix_items_tags")]
    [InlineData("ux_items_externalRef")]
    [InlineData("tx_items_text")]
    public async Task Items_collection_has_expected_index(string name)
    {
        var cursor = await fixture.Context.Items.Indexes.ListAsync();
        var indexes = await cursor.ToListAsync();

        indexes.Should().Contain(i => i["name"] == name);
    }

    [Fact]
    public async Task ExternalRef_index_is_unique_and_partial()
    {
        var cursor = await fixture.Context.Items.Indexes.ListAsync();
        var index = (await cursor.ToListAsync()).Single(i => i["name"] == "ux_items_externalRef");

        index["unique"].AsBoolean.Should().BeTrue();

        // 複合索引的 sparse 只在所有索引欄位都缺席時才跳過文件，而 ownerId 恆存在——
        // 手動品項仍會以 (ownerId, null, null) 進索引並互相衝突。
        // 要達成「手動品項沒有 externalRef，不應互相衝突」只能用 partialFilterExpression。
        index.Contains("sparse").Should().BeFalse("sparse 無法排除複合索引中的手動品項");
        index["partialFilterExpression"].Should().Be(
            (BsonValue)new BsonDocument("externalRef.provider", new BsonDocument("$exists", true)));
    }

    [Fact]
    public async Task Categories_collection_has_owner_index()
    {
        var cursor = await fixture.Context.Categories.Indexes.ListAsync();
        var indexes = await cursor.ToListAsync();

        indexes.Should().Contain(i => i["name"] == "ix_categories_owner");
    }
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter MongoIndexTests`
Expected: 7 筆新測試 FAIL（索引不存在）。

- [ ] **Step 3: 實作索引**

`src/MyCollection.Infrastructure/Mongo/MongoIndexInitializer.cs` 的 `EnsureIndexesAsync` 末尾追加：

```csharp
        await context.Categories.Indexes.CreateOneAsync(
            new CreateIndexModel<Category>(
                Builders<Category>.IndexKeys.Ascending(x => x.OwnerId).Ascending(x => x.Name),
                new CreateIndexOptions { Name = "ix_categories_owner" }),
            cancellationToken: ct);

        await context.Items.Indexes.CreateManyAsync(
            [
                // 首頁牆面
                new CreateIndexModel<Item>(
                    Builders<Item>.IndexKeys
                        .Ascending(x => x.OwnerId)
                        .Ascending(x => x.IsShowcased)
                        .Descending(x => x.UpdatedAt),
                    new CreateIndexOptions { Name = "ix_items_showcase" }),

                // 品類瀏覽
                new CreateIndexModel<Item>(
                    Builders<Item>.IndexKeys
                        .Ascending(x => x.OwnerId)
                        .Ascending(x => x.CategoryId)
                        .Descending(x => x.UpdatedAt),
                    new CreateIndexOptions { Name = "ix_items_category" }),

                // 標籤篩選
                new CreateIndexModel<Item>(
                    Builders<Item>.IndexKeys
                        .Ascending(x => x.OwnerId)
                        .Ascending(x => x.Tags),
                    new CreateIndexOptions { Name = "ix_items_tags" }),

                // 同步冪等性的地基：upsert 依賴此唯一索引避免重複品項。
                //
                // 用 partial 而非 sparse。複合索引的 sparse 只在「所有」索引欄位都缺席時才跳過該文件，
                // 而 ownerId 恆存在，於是每筆手動品項都會以 (ownerId, null, null) 進入索引——
                // 同一使用者建立第二筆手動品項就會撞 duplicate key。
                // partialFilterExpression 才能真正把沒有 externalRef 的文件排除在唯一性檢查外。
                new CreateIndexModel<Item>(
                    Builders<Item>.IndexKeys
                        .Ascending(x => x.OwnerId)
                        .Ascending("externalRef.provider")
                        .Ascending("externalRef.externalId"),
                    new CreateIndexOptions<Item>
                    {
                        Name = "ux_items_externalRef",
                        Unique = true,
                        PartialFilterExpression = Builders<Item>.Filter.Exists("externalRef.provider")
                    }),

                // 全文搜尋
                new CreateIndexModel<Item>(
                    Builders<Item>.IndexKeys.Text(x => x.Name).Text(x => x.Description),
                    new CreateIndexOptions { Name = "tx_items_text" })
            ],
            cancellationToken: ct);
```

檔案頂端補 `using MyCollection.Domain.Entities;`（已存在則略）。

- [ ] **Step 4: 擴充 fixture 的 ResetAsync**

`tests/MyCollection.Tests/Fixtures/MongoFixture.cs` 的 `ResetAsync` 改為：

```csharp
    public async Task ResetAsync()
    {
        await Context.Users.DeleteManyAsync(FilterDefinition<Domain.Entities.User>.Empty);
        await Context.Categories.DeleteManyAsync(FilterDefinition<Domain.Entities.Category>.Empty);
        await Context.Items.DeleteManyAsync(FilterDefinition<Domain.Entities.Item>.Empty);
    }
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test --filter MongoIndexTests`
Expected: `Passed: 8`

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(infra): 新增 items 與 categories 索引"
```

---

### Task 3：ICategoryRepository 與 MongoDB 實作

**Files:**
- Create: `src/MyCollection.Application/Categories/ICategoryRepository.cs`
- Create: `src/MyCollection.Infrastructure/Mongo/MongoCategoryRepository.cs`
- Modify: `src/MyCollection.Infrastructure/DependencyInjection.cs`
- Test: `tests/MyCollection.Tests/Integration/MongoCategoryRepositoryTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Integration/MongoCategoryRepositoryTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoCategoryRepositoryTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId OtherOwner = ObjectId.GenerateNewId();

    private MongoCategoryRepository _sut = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(c => c.UserId).Returns(Owner);
        userContext.SetupGet(c => c.IsAuthenticated).Returns(true);

        _sut = new MongoCategoryRepository(fixture.Context, userContext.Object);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Category NewCategory(ObjectId? ownerId, string name) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = ownerId,
        Name = name,
        Icon = "figure",
        Kind = CategoryKind.Physical,
        Fields = [new CategoryField { Key = "brand", Label = "廠商", Type = FieldType.Text }],
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task ListAsync_returns_own_and_system_categories_only()
    {
        await _sut.InsertAsync(NewCategory(Owner, "公仔"), CancellationToken.None);
        await fixture.Context.Categories.InsertOneAsync(NewCategory(null, "數位遊戲"));
        await fixture.Context.Categories.InsertOneAsync(NewCategory(OtherOwner, "別人的品類"));

        var result = await _sut.ListAsync(CancellationToken.None);

        result.Select(c => c.Name).Should().BeEquivalentTo("公仔", "數位遊戲");
    }

    [Fact]
    public async Task GetAsync_returns_null_for_other_owners_category()
    {
        var foreign = NewCategory(OtherOwner, "別人的品類");
        await fixture.Context.Categories.InsertOneAsync(foreign);

        var result = await _sut.GetAsync(foreign.Id, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_throws_NotFound_for_other_owners_category()
    {
        var foreign = NewCategory(OtherOwner, "別人的品類");
        await fixture.Context.Categories.InsertOneAsync(foreign);
        foreign.Name = "hijacked";

        var act = () => _sut.UpdateAsync(foreign, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_throws_Forbidden_for_system_category()
    {
        var system = NewCategory(null, "數位遊戲");
        await fixture.Context.Categories.InsertOneAsync(system);
        system.Name = "hijacked";

        var act = () => _sut.UpdateAsync(system, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task DeleteAsync_removes_own_category()
    {
        var category = NewCategory(Owner, "公仔");
        await _sut.InsertAsync(category, CancellationToken.None);

        await _sut.DeleteAsync(category.Id, CancellationToken.None);

        (await _sut.GetAsync(category.Id, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_throws_NotFound_when_missing()
    {
        var act = () => _sut.DeleteAsync(ObjectId.GenerateNewId(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter MongoCategoryRepositoryTests`
Expected: 編譯失敗，找不到 `MongoCategoryRepository`。

- [ ] **Step 3: 實作**

`src/MyCollection.Application/Categories/ICategoryRepository.cs`：

```csharp
using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Categories;

public interface ICategoryRepository
{
    /// <summary>自己的品類 + 系統內建品類（ownerId = null）。</summary>
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct);

    /// <summary>非自己也非系統內建時回傳 null。</summary>
    Task<Category?> GetAsync(ObjectId id, CancellationToken ct);

    Task InsertAsync(Category category, CancellationToken ct);

    /// <summary>找不到擲 NotFoundException；系統內建品類擲 ForbiddenException。</summary>
    Task UpdateAsync(Category category, CancellationToken ct);

    Task DeleteAsync(ObjectId id, CancellationToken ct);
}
```

`src/MyCollection.Infrastructure/Mongo/MongoCategoryRepository.cs`：

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoCategoryRepository(MongoContext context, IUserContext userContext) : ICategoryRepository
{
    private IMongoCollection<Category> Categories => context.Categories;

    /// <summary>可見範圍：自己的 + 系統內建。所有查詢一律從這裡起頭。</summary>
    // 集合運算式 [userContext.UserId, null] 在此處型別推斷有歧義（ObjectId 與 null 混用），
    // 必須寫明 ObjectId?[]。
    private FilterDefinition<Category> VisibleFilter =>
        Builders<Category>.Filter.In(x => x.OwnerId, new ObjectId?[] { userContext.UserId, null });

    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct) =>
        await Categories.Find(VisibleFilter).SortBy(x => x.Name).ToListAsync(ct);

    public Task<Category?> GetAsync(ObjectId id, CancellationToken ct) =>
        Categories
            .Find(Builders<Category>.Filter.And(VisibleFilter, Builders<Category>.Filter.Eq(x => x.Id, id)))
            .FirstOrDefaultAsync(ct)!;

    public Task InsertAsync(Category category, CancellationToken ct)
    {
        category.OwnerId = userContext.UserId;
        return Categories.InsertOneAsync(category, cancellationToken: ct);
    }

    public async Task UpdateAsync(Category category, CancellationToken ct)
    {
        var existing = await GetAsync(category.Id, ct)
                       ?? throw new NotFoundException(nameof(Category), category.Id);

        if (existing.OwnerId is null)
        {
            throw new ForbiddenException("System categories cannot be modified.");
        }

        category.OwnerId = userContext.UserId;

        // 與 MongoItemRepository 同理：$set 具名欄位，不用 ReplaceOne，
        // 避免 IgnoreExtraElements 造成文件既有欄位被靜默刪除。
        var update = Builders<Category>.Update
            .Set(x => x.Name, category.Name)
            .Set(x => x.Icon, category.Icon)
            .Set(x => x.Kind, category.Kind)
            .Set(x => x.Fields, category.Fields)
            .Set(x => x.UpdatedAt, category.UpdatedAt);

        await Categories.UpdateOneAsync(
            Builders<Category>.Filter.And(
                Builders<Category>.Filter.Eq(x => x.Id, category.Id),
                Builders<Category>.Filter.Eq(x => x.OwnerId, userContext.UserId)),
            update,
            cancellationToken: ct);
    }

    public async Task DeleteAsync(ObjectId id, CancellationToken ct)
    {
        var result = await Categories.DeleteOneAsync(
            Builders<Category>.Filter.And(
                Builders<Category>.Filter.Eq(x => x.Id, id),
                Builders<Category>.Filter.Eq(x => x.OwnerId, userContext.UserId)),
            ct);

        if (result.DeletedCount == 0)
        {
            throw new NotFoundException(nameof(Category), id);
        }
    }
}
```

- [ ] **Step 4: 註冊 DI**

`src/MyCollection.Infrastructure/DependencyInjection.cs` 的 `AddInfrastructure` 內追加：

```csharp
        services.AddScoped<ICategoryRepository, MongoCategoryRepository>();
```

並補 `using MyCollection.Application.Categories;`。

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test --filter MongoCategoryRepositoryTests`
Expected: `Passed: 6`

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(categories): 新增品類 repository 與擁有權隔離"
```

---

### Task 4：品類 CRUD Command / Query

**Files:**
- Create: `src/MyCollection.Application/Categories/CategoryDtos.cs`
- Create: `src/MyCollection.Application/Categories/CategoryCommands.cs`
- Create: `src/MyCollection.Application/Categories/ListCategoriesQuery.cs`
- Test: `tests/MyCollection.Tests/Unit/CategoryCommandTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/CategoryCommandTests.cs`：

```csharp
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Categories;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class CategoryCommandTests
{
    private readonly Mock<ICategoryRepository> _repository = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    private static CategoryFieldDto Field(string key, string type = "Text", string[]? options = null) =>
        new(key, $"{key} label", type, options, false, false, false);

    private static CreateCategoryCommand ValidCommand(params CategoryFieldDto[] fields) =>
        new("公仔", "figure", "Physical", fields.Length == 0 ? [Field("brand")] : fields);

    [Fact]
    public void Validator_accepts_valid_command()
    {
        new CreateCategoryCommandValidator().Validate(ValidCommand()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_rejects_duplicate_field_keys()
    {
        var result = new CreateCategoryCommandValidator()
            .Validate(ValidCommand(Field("brand"), Field("brand")));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Brand")]      // 大寫
    [InlineData("my brand")]   // 空白
    [InlineData("brand-name")] // 連字號
    [InlineData("1brand")]     // 數字開頭
    public void Validator_rejects_non_camel_case_field_key(string key)
    {
        new CreateCategoryCommandValidator().Validate(ValidCommand(Field(key))).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_rejects_unknown_field_type()
    {
        new CreateCategoryCommandValidator().Validate(ValidCommand(Field("brand", "Colour"))).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_requires_options_for_select_field()
    {
        new CreateCategoryCommandValidator()
            .Validate(ValidCommand(Field("brand", "Select"))).IsValid.Should().BeFalse();

        new CreateCategoryCommandValidator()
            .Validate(ValidCommand(Field("brand", "Select", ["GSC"]))).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CreateHandler_persists_category_with_timestamps()
    {
        Category? saved = null;
        _repository.Setup(r => r.InsertAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()))
            .Callback<Category, CancellationToken>((c, _) => saved = c)
            .Returns(Task.CompletedTask);

        var dto = await new CreateCategoryCommandHandler(_repository.Object, _time)
            .Handle(ValidCommand(Field("brand", "Select", ["GSC", "ALTER"])), CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.Name.Should().Be("公仔");
        saved.Kind.Should().Be(CategoryKind.Physical);
        saved.Fields.Should().ContainSingle();
        saved.Fields[0].Type.Should().Be(FieldType.Select);
        saved.Fields[0].Options.Should().BeEquivalentTo("GSC", "ALTER");
        saved.CreatedAt.Should().Be(new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc));

        dto.Id.Should().Be(saved.Id.ToString());
    }

    [Fact]
    public async Task UpdateHandler_throws_NotFound_when_missing()
    {
        _repository.Setup(r => r.GetAsync(It.IsAny<ObjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var command = new UpdateCategoryCommand(
            ObjectId.GenerateNewId().ToString(), "公仔", "figure", "Physical", [Field("brand")]);

        var act = () => new UpdateCategoryCommandHandler(_repository.Object, _time)
            .Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<Domain.Exceptions.NotFoundException>();
    }
}
```

需 `using MyCollection.Domain;`（`Domain.Exceptions.NotFoundException` 已由完整命名空間指出，可改為直接 `using MyCollection.Domain.Exceptions;` 並寫 `NotFoundException`）。

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter CategoryCommandTests`
Expected: 編譯失敗，找不到 `CreateCategoryCommand` 等型別。

- [ ] **Step 3: 實作 DTO**

`src/MyCollection.Application/Categories/CategoryDtos.cs`：

```csharp
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Categories;

public record CategoryFieldDto(
    string Key,
    string Label,
    string Type,
    IReadOnlyList<string>? Options,
    bool Required,
    bool Searchable,
    bool ShowOnCard);

public record CategoryDto(
    string Id,
    string Name,
    string Icon,
    string Kind,
    bool IsSystem,
    IReadOnlyList<CategoryFieldDto> Fields);

public static class CategoryMapper
{
    public static CategoryDto ToDto(Category category) => new(
        category.Id.ToString(),
        category.Name,
        category.Icon,
        category.Kind.ToString(),
        category.OwnerId is null,
        category.Fields.Select(ToDto).ToArray());

    public static CategoryFieldDto ToDto(CategoryField field) => new(
        field.Key,
        field.Label,
        field.Type.ToString(),
        field.Options,
        field.Required,
        field.Searchable,
        field.ShowOnCard);

    public static CategoryField ToEntity(CategoryFieldDto dto) => new()
    {
        Key = dto.Key,
        Label = dto.Label,
        Type = Enum.Parse<FieldType>(dto.Type, ignoreCase: true),
        Options = dto.Options?.ToList(),
        Required = dto.Required,
        Searchable = dto.Searchable,
        ShowOnCard = dto.ShowOnCard
    };
}
```

- [ ] **Step 4: 實作 Command 與 Query**

`src/MyCollection.Application/Categories/CategoryCommands.cs`：

```csharp
using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Categories;

public record CreateCategoryCommand(
    string Name,
    string Icon,
    string Kind,
    IReadOnlyList<CategoryFieldDto> Fields) : IRequest<CategoryDto>;

public record UpdateCategoryCommand(
    string Id,
    string Name,
    string Icon,
    string Kind,
    IReadOnlyList<CategoryFieldDto> Fields) : IRequest<CategoryDto>;

public record DeleteCategoryCommand(string Id) : IRequest;

/// <summary>Create/Update 共用的欄位規則。</summary>
public static partial class CategoryRules
{
    [GeneratedRegex("^[a-z][a-zA-Z0-9]*$")]
    public static partial Regex FieldKeyPattern { get; }

    public static void ApplyTo<T>(AbstractValidator<T> validator, Func<T, string> kind, Func<T, IReadOnlyList<CategoryFieldDto>> fields)
    {
        validator.RuleFor(x => kind(x))
            .Must(k => Enum.TryParse<CategoryKind>(k, ignoreCase: true, out _))
            .WithName("Kind")
            .WithMessage("Kind must be 'Physical' or 'Digital'.");

        validator.RuleFor(x => fields(x))
            .NotNull()
            .WithName("Fields")
            .Must(f => f.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() == f.Count)
            .WithMessage("Field keys contain duplicate entries.");

        validator.RuleForEach(x => fields(x)).ChildRules(field =>
        {
            field.RuleFor(f => f.Key)
                .NotEmpty()
                .Must(k => FieldKeyPattern.IsMatch(k))
                .WithMessage("Field key must be camelCase (letters and digits, starting with a lowercase letter).");

            field.RuleFor(f => f.Label).NotEmpty().MaximumLength(64);

            field.RuleFor(f => f.Type)
                .Must(t => Enum.TryParse<FieldType>(t, ignoreCase: true, out _))
                .WithMessage("Unknown field type.");

            field.RuleFor(f => f.Options)
                .NotNull().NotEmpty()
                .When(f => string.Equals(f.Type, nameof(FieldType.Select), StringComparison.OrdinalIgnoreCase))
                .WithMessage("A Select field requires at least one option.");
        }).WithName("Fields");
    }
}

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Icon).NotEmpty().MaximumLength(32);
        CategoryRules.ApplyTo(this, x => x.Kind, x => x.Fields);
    }
}

public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(x => x.Id).Must(id => ObjectId.TryParse(id, out _)).WithMessage("Invalid category id.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Icon).NotEmpty().MaximumLength(32);
        CategoryRules.ApplyTo(this, x => x.Kind, x => x.Fields);
    }
}

public sealed class CreateCategoryCommandHandler(ICategoryRepository repository, TimeProvider timeProvider)
    : IRequestHandler<CreateCategoryCommand, CategoryDto>
{
    public async Task<CategoryDto> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var category = new Category
        {
            Id = ObjectId.GenerateNewId(),
            Name = request.Name.Trim(),
            Icon = request.Icon,
            Kind = Enum.Parse<CategoryKind>(request.Kind, ignoreCase: true),
            Fields = request.Fields.Select(CategoryMapper.ToEntity).ToList(),
            CreatedAt = now,
            UpdatedAt = now
        };

        await repository.InsertAsync(category, cancellationToken);

        return CategoryMapper.ToDto(category);
    }
}

public sealed class UpdateCategoryCommandHandler(ICategoryRepository repository, TimeProvider timeProvider)
    : IRequestHandler<UpdateCategoryCommand, CategoryDto>
{
    public async Task<CategoryDto> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var id = ObjectId.Parse(request.Id);
        var existing = await repository.GetAsync(id, cancellationToken)
                       ?? throw new NotFoundException(nameof(Category), request.Id);

        existing.Name = request.Name.Trim();
        existing.Icon = request.Icon;
        existing.Kind = Enum.Parse<CategoryKind>(request.Kind, ignoreCase: true);
        existing.Fields = request.Fields.Select(CategoryMapper.ToEntity).ToList();
        existing.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await repository.UpdateAsync(existing, cancellationToken);

        return CategoryMapper.ToDto(existing);
    }
}

public sealed class DeleteCategoryCommandHandler(ICategoryRepository repository)
    : IRequestHandler<DeleteCategoryCommand>
{
    public Task Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        // DeleteCategoryCommand 無 validator，不合法 id 必須回 404 而非 500
        if (!ObjectId.TryParse(request.Id, out var id))
        {
            throw new NotFoundException(nameof(Category), request.Id);
        }

        return repository.DeleteAsync(id, cancellationToken);
    }
}
```

`src/MyCollection.Application/Categories/ListCategoriesQuery.cs`：

```csharp
using MediatR;

namespace MyCollection.Application.Categories;

public record ListCategoriesQuery : IRequest<IReadOnlyList<CategoryDto>>;

public sealed class ListCategoriesQueryHandler(ICategoryRepository repository)
    : IRequestHandler<ListCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    public async Task<IReadOnlyList<CategoryDto>> Handle(ListCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = await repository.ListAsync(cancellationToken);

        return categories.Select(CategoryMapper.ToDto).ToArray();
    }
}
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test --filter CategoryCommandTests`
Expected: `Passed: 10`

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(categories): 新增品類 CRUD command 與 query"
```

---

### Task 5：Schema 驅動的 attributes 驗證

**Files:**
- Create: `src/MyCollection.Application/Common/BsonJson.cs`
- Create: `src/MyCollection.Application/Items/AttributeValidator.cs`
- Test: `tests/MyCollection.Tests/Unit/AttributeValidatorTests.cs`

這是整個 JSON + Schema 決策的核心：schema 定義直接轉成驗證規則，新增品類不需要改任何 code。

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/AttributeValidatorTests.cs`：

```csharp
using System.Text.Json;
using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class AttributeValidatorTests
{
    private readonly AttributeValidator _sut = new();

    private static Category CategoryWith(params CategoryField[] fields) => new()
    {
        Id = ObjectId.GenerateNewId(),
        Name = "公仔",
        Kind = CategoryKind.Physical,
        Fields = fields.ToList()
    };

    private static CategoryField Field(string key, FieldType type, bool required = false, string[]? options = null) =>
        new() { Key = key, Label = key, Type = type, Required = required, Options = options?.ToList() };

    private static BsonDocument Attributes(string json) => BsonJson.ToBson(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void Accepts_valid_attributes()
    {
        var category = CategoryWith(
            Field("brand", FieldType.Select, required: true, options: ["GSC", "ALTER"]),
            Field("scale", FieldType.Text),
            Field("height", FieldType.Number),
            Field("releasedAt", FieldType.Date),
            Field("isLimited", FieldType.Bool),
            Field("productUrl", FieldType.Url));

        var failures = _sut.Validate(category, Attributes("""
            {
              "brand": "GSC",
              "scale": "1/8",
              "height": 200,
              "releasedAt": "2026-01-15T00:00:00Z",
              "isLimited": true,
              "productUrl": "https://www.goodsmile.com/x"
            }
            """));

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Rejects_missing_required_field()
    {
        var category = CategoryWith(Field("brand", FieldType.Text, required: true));

        var failures = _sut.Validate(category, Attributes("{}"));

        failures.Should().ContainSingle();
        failures[0].PropertyName.Should().Be("attributes.brand");
        failures[0].ErrorMessage.Should().Contain("required");
    }

    [Fact]
    public void Rejects_null_for_required_field()
    {
        var category = CategoryWith(Field("brand", FieldType.Text, required: true));

        var failures = _sut.Validate(category, Attributes("""{ "brand": null }"""));

        failures.Should().ContainSingle();
    }

    [Fact]
    public void Allows_null_for_optional_field()
    {
        var category = CategoryWith(Field("brand", FieldType.Text));

        _sut.Validate(category, Attributes("""{ "brand": null }""")).Should().BeEmpty();
    }

    [Fact]
    public void Rejects_unknown_attribute_key()
    {
        var category = CategoryWith(Field("brand", FieldType.Text));

        var failures = _sut.Validate(category, Attributes("""{ "brand": "GSC", "colour": "red" }"""));

        failures.Should().ContainSingle();
        failures[0].PropertyName.Should().Be("attributes.colour");
        failures[0].ErrorMessage.Should().Contain("not defined");
    }

    [Fact]
    public void Rejects_value_outside_select_options()
    {
        var category = CategoryWith(Field("brand", FieldType.Select, options: ["GSC", "ALTER"]));

        var failures = _sut.Validate(category, Attributes("""{ "brand": "MegaHouse" }"""));

        failures.Should().ContainSingle();
        failures[0].ErrorMessage.Should().Contain("GSC");
    }

    [Theory]
    [InlineData("Number", "\"not-a-number\"")]
    [InlineData("Bool", "\"yes\"")]
    [InlineData("Date", "\"15 January\"")]
    [InlineData("Url", "\"not a url\"")]
    [InlineData("Text", "123")]
    public void Rejects_wrong_type(string type, string jsonValue)
    {
        var category = CategoryWith(Field("value", Enum.Parse<FieldType>(type)));

        var failures = _sut.Validate(category, Attributes($$"""{ "value": {{jsonValue}} }"""));

        failures.Should().ContainSingle();
        failures[0].PropertyName.Should().Be("attributes.value");
    }

    [Fact]
    public void Reports_every_failure_not_just_the_first()
    {
        var category = CategoryWith(
            Field("brand", FieldType.Text, required: true),
            Field("height", FieldType.Number));

        var failures = _sut.Validate(category, Attributes("""{ "height": "tall", "colour": "red" }"""));

        failures.Should().HaveCount(3);
    }

    [Fact]
    public void BsonJson_roundtrips_nested_structures()
    {
        var bson = Attributes("""{ "spec": { "scale": "1/8", "tags": ["a", "b"] }, "count": 3 }""");

        var dictionary = BsonJson.ToDictionary(bson);

        dictionary["count"].Should().Be(3);
        bson["spec"]["tags"].AsBsonArray.Select(x => x.AsString).Should().BeEquivalentTo("a", "b");
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter AttributeValidatorTests`
Expected: 編譯失敗，找不到 `AttributeValidator` / `BsonJson`。

- [ ] **Step 3: 實作 BsonJson**

`src/MyCollection.Application/Common/BsonJson.cs`：

```csharp
using System.Text.Json;
using MongoDB.Bson;

namespace MyCollection.Application.Common;

/// <summary>
/// API 邊界的 JSON ⇄ BSON 轉換。System.Text.Json 的 JsonElement 不能直接餵給 driver，
/// 而 BsonDocument 序列化成 JSON 時會產生 Extended JSON（$date、$numberLong），
/// 因此兩個方向都要自己走一遍。
/// </summary>
public static class BsonJson
{
    public static BsonDocument ToBson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Attributes must be a JSON object.", nameof(element));
        }

        var document = new BsonDocument();
        foreach (var property in element.EnumerateObject())
        {
            document[property.Name] = ToBsonValue(property.Value);
        }

        return document;
    }

    public static Dictionary<string, object?> ToDictionary(BsonDocument document) =>
        document.Elements.ToDictionary(e => e.Name, e => ToClrValue(e.Value));

    private static BsonValue ToBsonValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ToBson(element),
        JsonValueKind.Array => new BsonArray(element.EnumerateArray().Select(ToBsonValue)),
        JsonValueKind.String => new BsonString(element.GetString()!),
        JsonValueKind.Number => element.TryGetInt32(out var i)
            ? new BsonInt32(i)
            : element.TryGetInt64(out var l)
                ? new BsonInt64(l)
                : new BsonDouble(element.GetDouble()),
        JsonValueKind.True => BsonBoolean.True,
        JsonValueKind.False => BsonBoolean.False,
        JsonValueKind.Null or JsonValueKind.Undefined => BsonNull.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(element), element.ValueKind, "Unsupported JSON value.")
    };

    private static object? ToClrValue(BsonValue value) => value.BsonType switch
    {
        BsonType.Document => ToDictionary(value.AsBsonDocument),
        BsonType.Array => value.AsBsonArray.Select(ToClrValue).ToArray(),
        BsonType.String => value.AsString,
        BsonType.Int32 => value.AsInt32,
        BsonType.Int64 => value.AsInt64,
        BsonType.Double => value.AsDouble,
        BsonType.Decimal128 => (decimal)value.AsDecimal128,
        BsonType.Boolean => value.AsBoolean,
        BsonType.DateTime => value.ToUniversalTime(),
        BsonType.Null => null,
        _ => value.ToString()
    };
}
```

- [ ] **Step 3b: 實作 UTC 邊界歸一化**

`src/MyCollection.Application/Common/UtcDate.cs`：

```csharp
namespace MyCollection.Application.Common;

/// <summary>
/// API 邊界的 DateTime 歸一化。
///
/// 資料層的 UtcOnlyDateTimeSerializer 會拒絕任何 Kind != Utc 的值（避免 UTC+8 機器把
/// 03:00 靜默存成前一天 19:00）。但 System.Text.Json 反序列化沒帶 'Z' 的字串會得到
/// Unspecified，前端只要少寫一個 Z 就會讓請求 500。這裡在進入 Handler 前先歸一化：
/// 沒有時區資訊的輸入一律視為 UTC，帶時區的則換算成 UTC。
/// </summary>
public static class UtcDate
{
    public static DateTime Normalise(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public static DateTime? Normalise(DateTime? value) => value is null ? null : Normalise(value.Value);
}
```

搭配測試 `tests/MyCollection.Tests/Unit/UtcDateTests.cs`：

```csharp
using FluentAssertions;
using MyCollection.Application.Common;

namespace MyCollection.Tests.Unit;

public class UtcDateTests
{
    [Fact]
    public void Treats_naive_input_as_utc_without_shifting_the_clock()
    {
        var naive = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var result = UtcDate.Normalise(naive);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Converts_local_input_to_utc()
    {
        var local = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);

        var result = UtcDate.Normalise(local);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(local.ToUniversalTime());
    }

    [Fact]
    public void Leaves_utc_input_untouched()
    {
        var utc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        UtcDate.Normalise(utc).Should().Be(utc);
    }

    [Fact]
    public void Passes_null_through()
    {
        UtcDate.Normalise((DateTime?)null).Should().BeNull();
    }
}
```

- [ ] **Step 4: 實作 AttributeValidator**

`src/MyCollection.Application/Items/AttributeValidator.cs`：

```csharp
using System.Globalization;
using FluentValidation.Results;
using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Items;

public interface IAttributeValidator
{
    /// <summary>依品類 schema 檢查 attributes，回傳全部失敗（不短路）。</summary>
    IReadOnlyList<ValidationFailure> Validate(Category category, BsonDocument attributes);
}

public sealed class AttributeValidator : IAttributeValidator
{
    public IReadOnlyList<ValidationFailure> Validate(Category category, BsonDocument attributes)
    {
        var failures = new List<ValidationFailure>();
        var definedKeys = category.Fields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var element in attributes.Elements)
        {
            if (!definedKeys.Contains(element.Name))
            {
                failures.Add(new ValidationFailure(
                    $"attributes.{element.Name}",
                    $"'{element.Name}' is not defined in category '{category.Name}'."));
            }
        }

        foreach (var field in category.Fields)
        {
            var property = $"attributes.{field.Key}";
            var present = attributes.TryGetValue(field.Key, out var value) && !value.IsBsonNull;

            if (!present)
            {
                if (field.Required)
                {
                    failures.Add(new ValidationFailure(property, $"'{field.Label}' is required."));
                }

                continue;
            }

            var error = CheckType(field, value!);
            if (error is not null)
            {
                failures.Add(new ValidationFailure(property, error));
            }
        }

        return failures;
    }

    private static string? CheckType(CategoryField field, BsonValue value) => field.Type switch
    {
        FieldType.Text => value.IsString ? null : $"'{field.Label}' must be text.",

        FieldType.Number => value.IsNumeric ? null : $"'{field.Label}' must be a number.",

        FieldType.Bool => value.IsBoolean ? null : $"'{field.Label}' must be true or false.",

        FieldType.Date => IsDate(value) ? null : $"'{field.Label}' must be an ISO-8601 date.",

        FieldType.Url => IsAbsoluteUrl(value) ? null : $"'{field.Label}' must be an absolute http(s) URL.",

        FieldType.Select => !value.IsString
            ? $"'{field.Label}' must be text."
            : field.Options is not null && field.Options.Contains(value.AsString, StringComparer.Ordinal)
                ? null
                : $"'{field.Label}' must be one of: {string.Join(", ", field.Options ?? [])}.",

        _ => $"'{field.Label}' has an unsupported field type."
    };

    /// <summary>
    /// 只接受 ISO-8601。用 TryParseExact 而非 TryParse：後者過於寬鬆，
    /// InvariantCulture 下 "15 January" 會被解析成 2015-01-01，型別驗證形同虛設。
    /// </summary>
    private static readonly string[] IsoFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFFK"
    ];

    private static bool IsDate(BsonValue value) =>
        value.IsValidDateTime
        || (value.IsString && DateTime.TryParseExact(
            value.AsString,
            IsoFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out _));

    private static bool IsAbsoluteUrl(BsonValue value) =>
        value.IsString
        && Uri.TryCreate(value.AsString, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test --filter AttributeValidatorTests`
Expected: `Passed: 13`

**實測記錄（此處原本有兩個錯誤，已修正）：**

1. `DateTimeStyles.RoundtripKind | DateTimeStyles.AdjustToUniversal` 是**非法組合**，`DateTime.TryParse` 會擲 `ArgumentException: The DateTimeStyles value RoundtripKind cannot be used with the values AssumeLocal, AssumeUniversal or AdjustToUniversal.`——每一次 Date 欄位驗證都會炸。
2. 即使只用 `RoundtripKind`，`DateTime.TryParse("15 January", InvariantCulture, ...)` 也會**成功**解析成 `2015-01-01`。用 `TryParse` 做型別驗證等於沒驗。

正解是 `TryParseExact` + 明確的 ISO-8601 格式清單（見上）。實測該清單接受 `2026-01-15`、`2026-01-15T10:30:00`、`2026-01-15T00:00:00Z`、`2026-01-15T10:30:00+08:00`、含小數秒的變體；拒絕 `15 January`、`01/15/2026`、`2026-13-01`、`2026-01-15 10:30:00`（空格分隔非 ISO-8601）與空字串。

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(items): 新增 schema 驅動的 attributes 驗證"
```

---

### Task 6：IItemRepository 與 MongoDB 實作

**Files:**
- Create: `src/MyCollection.Application/Common/PagedResult.cs`
- Create: `src/MyCollection.Application/Items/IItemRepository.cs`
- Create: `src/MyCollection.Infrastructure/Mongo/MongoItemRepository.cs`
- Modify: `src/MyCollection.Infrastructure/DependencyInjection.cs`
- Test: `tests/MyCollection.Tests/Integration/MongoItemRepositoryTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Integration/MongoItemRepositoryTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoItemRepositoryTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId OtherOwner = ObjectId.GenerateNewId();
    private static readonly ObjectId FigureCategory = ObjectId.GenerateNewId();
    private static readonly ObjectId GameCategory = ObjectId.GenerateNewId();

    private MongoItemRepository _sut = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(c => c.UserId).Returns(Owner);
        _sut = new MongoItemRepository(fixture.Context, userContext.Object);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Item NewItem(
        ObjectId ownerId,
        string name,
        ObjectId categoryId,
        bool showcased = false,
        string[]? tags = null,
        string? description = null) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = ownerId,
        CategoryId = categoryId,
        Name = name,
        Description = description,
        Tags = (tags ?? []).ToList(),
        IsShowcased = showcased,
        Source = ItemSource.Manual,
        Attributes = new BsonDocument("brand", "GSC"),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private async Task SeedAsync()
    {
        await fixture.Context.Items.InsertManyAsync(
        [
            NewItem(Owner, "初音ミク Figure", FigureCategory, showcased: true, tags: ["GSC", "VOCALOID"]),
            NewItem(Owner, "Team Fortress 2", GameCategory, tags: ["FPS"], description: "Valve shooter"),
            NewItem(Owner, "Portal 2", GameCategory, showcased: true, tags: ["Puzzle"]),
            NewItem(OtherOwner, "別人的公仔", FigureCategory, showcased: true, tags: ["GSC"])
        ]);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_other_owners_item()
    {
        await SeedAsync();
        var foreign = await fixture.Context.Items
            .Find(MongoDB.Driver.Builders<Item>.Filter.Eq(x => x.OwnerId, OtherOwner)).FirstAsync();

        (await _sut.GetAsync(foreign.Id, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_never_returns_other_owners_items()
    {
        await SeedAsync();

        var result = await _sut.SearchAsync(new ItemQuerySpec(), CancellationToken.None);

        result.Total.Should().Be(3);
        result.Items.Should().NotContain(i => i.Name == "別人的公仔");
    }

    [Fact]
    public async Task SearchAsync_filters_by_category()
    {
        await SeedAsync();

        var result = await _sut.SearchAsync(new ItemQuerySpec { CategoryId = GameCategory }, CancellationToken.None);

        result.Total.Should().Be(2);
        result.Items.Select(i => i.Name).Should().BeEquivalentTo("Team Fortress 2", "Portal 2");
    }

    [Fact]
    public async Task SearchAsync_filters_by_showcased()
    {
        await SeedAsync();

        var result = await _sut.SearchAsync(new ItemQuerySpec { IsShowcased = true }, CancellationToken.None);

        result.Total.Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_filters_by_all_supplied_tags()
    {
        await SeedAsync();

        var both = await _sut.SearchAsync(new ItemQuerySpec { Tags = ["GSC", "VOCALOID"] }, CancellationToken.None);
        var missing = await _sut.SearchAsync(new ItemQuerySpec { Tags = ["GSC", "FPS"] }, CancellationToken.None);

        both.Total.Should().Be(1);
        missing.Total.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_full_text_matches_name_and_description()
    {
        await SeedAsync();

        var byName = await _sut.SearchAsync(new ItemQuerySpec { Search = "Portal" }, CancellationToken.None);
        var byDescription = await _sut.SearchAsync(new ItemQuerySpec { Search = "Valve" }, CancellationToken.None);

        byName.Items.Should().ContainSingle().Which.Name.Should().Be("Portal 2");
        byDescription.Items.Should().ContainSingle().Which.Name.Should().Be("Team Fortress 2");
    }

    [Fact]
    public async Task SearchAsync_pages_results()
    {
        await SeedAsync();

        var page1 = await _sut.SearchAsync(new ItemQuerySpec { Page = 1, PageSize = 2 }, CancellationToken.None);
        var page2 = await _sut.SearchAsync(new ItemQuerySpec { Page = 2, PageSize = 2 }, CancellationToken.None);

        page1.Total.Should().Be(3);
        page1.Items.Should().HaveCount(2);
        page2.Items.Should().HaveCount(1);
        page1.Items.Select(i => i.Id).Should().NotIntersectWith(page2.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task UpdateAsync_throws_NotFound_for_other_owners_item()
    {
        await SeedAsync();
        var foreign = await fixture.Context.Items
            .Find(MongoDB.Driver.Builders<Item>.Filter.Eq(x => x.OwnerId, OtherOwner)).FirstAsync();
        foreign.Name = "hijacked";

        var act = () => _sut.UpdateAsync(foreign, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_throws_NotFound_for_other_owners_item()
    {
        await SeedAsync();
        var foreign = await fixture.Context.Items
            .Find(MongoDB.Driver.Builders<Item>.Filter.Eq(x => x.OwnerId, OtherOwner)).FirstAsync();

        var act = () => _sut.DeleteAsync(foreign.Id, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ListTagsAsync_returns_distinct_owner_tags()
    {
        await SeedAsync();

        var tags = await _sut.ListTagsAsync(CancellationToken.None);

        tags.Should().BeEquivalentTo("FPS", "GSC", "Puzzle", "VOCALOID");
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter MongoItemRepositoryTests`
Expected: 編譯失敗，找不到 `MongoItemRepository` / `ItemQuerySpec`。

- [ ] **Step 3: 實作契約**

`src/MyCollection.Application/Common/PagedResult.cs`：

```csharp
namespace MyCollection.Application.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize)
{
    public static PagedResult<T> Empty(int page, int pageSize) => new([], 0, page, pageSize);
}
```

`src/MyCollection.Application/Items/IItemRepository.cs`：

```csharp
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Items;

/// <summary>Repository 層的查詢條件。ownerId 不在此，由 Repository 自 IUserContext 強制加上。</summary>
public sealed class ItemQuerySpec
{
    public string? Search { get; init; }
    public ObjectId? CategoryId { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public bool? IsShowcased { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 24;
}

public interface IItemRepository
{
    Task<Item?> GetAsync(ObjectId id, CancellationToken ct);

    Task<PagedResult<Item>> SearchAsync(ItemQuerySpec spec, CancellationToken ct);

    Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken ct);

    Task InsertAsync(Item item, CancellationToken ct);

    /// <summary>找不到（含不屬於自己）擲 NotFoundException。</summary>
    Task UpdateAsync(Item item, CancellationToken ct);

    Task DeleteAsync(ObjectId id, CancellationToken ct);
}
```

- [ ] **Step 4: 實作 MongoItemRepository**

`src/MyCollection.Infrastructure/Mongo/MongoItemRepository.cs`：

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoItemRepository(MongoContext context, IUserContext userContext) : IItemRepository
{
    private static readonly FilterDefinitionBuilder<Item> Filter = Builders<Item>.Filter;

    private IMongoCollection<Item> Items => context.Items;

    /// <summary>
    /// 所有查詢的起點。忘記加條件的後果是查不到資料，而不是洩漏資料。
    /// </summary>
    private FilterDefinition<Item> OwnerFilter => Filter.Eq(x => x.OwnerId, userContext.UserId);

    public Task<Item?> GetAsync(ObjectId id, CancellationToken ct) =>
        Items.Find(Filter.And(OwnerFilter, Filter.Eq(x => x.Id, id))).FirstOrDefaultAsync(ct)!;

    public async Task<PagedResult<Item>> SearchAsync(ItemQuerySpec spec, CancellationToken ct)
    {
        var filters = new List<FilterDefinition<Item>> { OwnerFilter };

        if (spec.CategoryId is { } categoryId)
        {
            filters.Add(Filter.Eq(x => x.CategoryId, categoryId));
        }

        if (spec.IsShowcased is { } showcased)
        {
            filters.Add(Filter.Eq(x => x.IsShowcased, showcased));
        }

        if (spec.Tags is { Count: > 0 })
        {
            filters.Add(Filter.All(x => x.Tags, spec.Tags));
        }

        if (!string.IsNullOrWhiteSpace(spec.Search))
        {
            filters.Add(Filter.Text(spec.Search));
        }

        var filter = Filter.And(filters);
        var page = Math.Max(spec.Page, 1);
        var pageSize = Math.Clamp(spec.PageSize, 1, 200);

        var total = await Items.CountDocumentsAsync(filter, cancellationToken: ct);

        var items = await Items
            .Find(filter)
            .SortByDescending(x => x.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Item>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken ct)
    {
        var tags = await Items.DistinctAsync<string>("tags", OwnerFilter, cancellationToken: ct);

        return (await tags.ToListAsync(ct)).Order(StringComparer.Ordinal).ToArray();
    }

    public Task InsertAsync(Item item, CancellationToken ct)
    {
        item.OwnerId = userContext.UserId;
        return Items.InsertOneAsync(item, cancellationToken: ct);
    }

    /// <summary>
    /// 刻意用 $set 具名欄位而非 ReplaceOne。
    ///
    /// MongoConventions 註冊了 IgnoreExtraElementsConvention(true)——這是滾動式 schema 演進
    /// 的必要條件，但代價是反序列化會丟掉實體沒宣告的欄位。若用 ReplaceOne 把實體整個寫回去，
    /// 任何一次「欄位改名 → 舊欄位變成 extra element → 使用者編輯該筆」就會永久刪掉舊資料。
    /// $set 只碰列舉出來的欄位，文件裡的其他東西原封不動。
    /// </summary>
    public async Task UpdateAsync(Item item, CancellationToken ct)
    {
        item.OwnerId = userContext.UserId;

        var update = Builders<Item>.Update
            .Set(x => x.CategoryId, item.CategoryId)
            .Set(x => x.Name, item.Name)
            .Set(x => x.Description, item.Description)
            .Set(x => x.Images, item.Images)
            .Set(x => x.Tags, item.Tags)
            .Set(x => x.IsShowcased, item.IsShowcased)
            .Set(x => x.Acquisition, item.Acquisition)
            .Set(x => x.LocationId, item.LocationId)
            .Set(x => x.Attributes, item.Attributes)
            .Set(x => x.UpdatedAt, item.UpdatedAt);

        // OwnerId / Source / ExternalRef / CreatedAt 不在此列：
        // 它們由同步流程與建立流程擁有，使用者更新不得改寫。
        var result = await Items.UpdateOneAsync(
            Filter.And(OwnerFilter, Filter.Eq(x => x.Id, item.Id)),
            update,
            cancellationToken: ct);

        if (result.MatchedCount == 0)
        {
            throw new NotFoundException(nameof(Item), item.Id);
        }
    }

    public async Task DeleteAsync(ObjectId id, CancellationToken ct)
    {
        var result = await Items.DeleteOneAsync(Filter.And(OwnerFilter, Filter.Eq(x => x.Id, id)), ct);

        if (result.DeletedCount == 0)
        {
            throw new NotFoundException(nameof(Item), id);
        }
    }
}
```

- [ ] **Step 5: 註冊 DI**

`src/MyCollection.Infrastructure/DependencyInjection.cs` 追加：

```csharp
        services.AddScoped<IItemRepository, MongoItemRepository>();
        services.AddSingleton<IAttributeValidator, AttributeValidator>();
```

並補 `using MyCollection.Application.Items;`。

- [ ] **Step 6: 跑測試確認通過**

Run: `dotnet test --filter MongoItemRepositoryTests`
Expected: `Passed: 10`

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "feat(items): 新增品項 repository、搜尋與擁有權隔離"
```

---

### Task 7：品項 CRUD Command / Query

**Files:**
- Create: `src/MyCollection.Application/Items/ItemDtos.cs`
- Create: `src/MyCollection.Application/Items/ItemCommands.cs`
- Create: `src/MyCollection.Application/Items/ItemQueries.cs`
- Test: `tests/MyCollection.Tests/Unit/ItemCommandTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/ItemCommandTests.cs`：

```csharp
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Categories;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class ItemCommandTests
{
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    private static readonly ObjectId CategoryId = ObjectId.GenerateNewId();

    private static readonly Category FigureCategory = new()
    {
        Id = CategoryId,
        Name = "公仔",
        Kind = CategoryKind.Physical,
        Fields =
        [
            new CategoryField { Key = "brand", Label = "廠商", Type = FieldType.Text, Required = true },
            new CategoryField { Key = "scale", Label = "比例", Type = FieldType.Text }
        ]
    };

    public ItemCommandTests()
    {
        _categories.Setup(r => r.GetAsync(CategoryId, It.IsAny<CancellationToken>())).ReturnsAsync(FigureCategory);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private CreateItemCommandHandler CreateSut() =>
        new(_items.Object, _categories.Object, new AttributeValidator(), _time);

    private static CreateItemCommand Command(string attributes = """{ "brand": "GSC" }""") => new(
        CategoryId.ToString(),
        "初音ミク 1/8",
        "描述",
        ["GSC"],
        false,
        Json(attributes),
        null);

    [Fact]
    public async Task Creates_item_with_timestamps_and_manual_source()
    {
        Item? saved = null;
        _items.Setup(r => r.InsertAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>()))
            .Callback<Item, CancellationToken>((i, _) => saved = i)
            .Returns(Task.CompletedTask);

        var dto = await CreateSut().Handle(Command(), CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.Source.Should().Be(ItemSource.Manual);
        saved.CategoryId.Should().Be(CategoryId);
        saved.Attributes["brand"].AsString.Should().Be("GSC");
        saved.CreatedAt.Should().Be(new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc));
        saved.UpdatedAt.Should().Be(saved.CreatedAt);

        dto.Attributes["brand"].Should().Be("GSC");
        dto.Tags.Should().BeEquivalentTo("GSC");
    }

    [Fact]
    public async Task Throws_NotFound_for_unknown_category()
    {
        _categories.Setup(r => r.GetAsync(It.IsAny<ObjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var act = () => CreateSut().Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Throws_ValidationException_when_attributes_violate_schema()
    {
        var act = () => CreateSut().Handle(Command("""{ "scale": "1/8" }"""), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be("attributes.brand");
    }

    [Fact]
    public async Task Forces_null_location_for_digital_category()
    {
        var digital = new Category
        {
            Id = CategoryId, Name = "數位遊戲", Kind = CategoryKind.Digital, Fields = []
        };
        _categories.Setup(r => r.GetAsync(CategoryId, It.IsAny<CancellationToken>())).ReturnsAsync(digital);

        Item? saved = null;
        _items.Setup(r => r.InsertAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>()))
            .Callback<Item, CancellationToken>((i, _) => saved = i)
            .Returns(Task.CompletedTask);

        var command = Command("{}") with { LocationId = ObjectId.GenerateNewId().ToString() };
        await CreateSut().Handle(command, CancellationToken.None);

        saved!.LocationId.Should().BeNull("digital 品類的 locationId 恆為 null");
    }

    [Fact]
    public async Task Update_preserves_immutable_fields()
    {
        var existing = new Item
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = ObjectId.GenerateNewId(),
            CategoryId = CategoryId,
            Name = "舊名稱",
            Source = ItemSource.Steam,
            ExternalRef = new ExternalRef { Provider = "steam", ExternalId = "440" },
            Images = [new ItemImage { Id = "img1", Path = "p", CardPath = "c", ThumbPath = "t", IsPrimary = true }],
            Attributes = new BsonDocument("brand", "Valve"),
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        _items.Setup(r => r.GetAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        Item? saved = null;
        _items.Setup(r => r.UpdateAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>()))
            .Callback<Item, CancellationToken>((i, _) => saved = i)
            .Returns(Task.CompletedTask);

        var command = new UpdateItemCommand(
            existing.Id.ToString(), CategoryId.ToString(), "新名稱", null, ["FPS"], true,
            Json("""{ "brand": "Valve" }"""), null);

        await new UpdateItemCommandHandler(_items.Object, _categories.Object, new AttributeValidator(), _time)
            .Handle(command, CancellationToken.None);

        saved!.Name.Should().Be("新名稱");
        saved.IsShowcased.Should().BeTrue();
        saved.Source.Should().Be(ItemSource.Steam, "來源不可由使用者改寫");
        saved.ExternalRef!.ExternalId.Should().Be("440", "外部參照不可由使用者改寫");
        saved.Images.Should().ContainSingle("圖片由 Media 模組管理，不透過品項更新");
        saved.CreatedAt.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        saved.UpdatedAt.Should().Be(new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Search_validator_rejects_out_of_range_page_size()
    {
        var validator = new SearchItemsQueryValidator();

        validator.Validate(new SearchItemsQuery(PageSize: 0)).IsValid.Should().BeFalse();
        validator.Validate(new SearchItemsQuery(PageSize: 500)).IsValid.Should().BeFalse();
        validator.Validate(new SearchItemsQuery(Page: 0)).IsValid.Should().BeFalse();
        validator.Validate(new SearchItemsQuery()).IsValid.Should().BeTrue();
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter ItemCommandTests`
Expected: 編譯失敗，找不到 `CreateItemCommand` 等型別。

- [ ] **Step 3: 實作 DTO 與映射**

`src/MyCollection.Application/Items/ItemDtos.cs`：

```csharp
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Items;

public record ItemImageDto(string Id, string Path, string CardPath, string ThumbPath, bool IsPrimary, int Order);

public record ExternalRefDto(string Provider, string ExternalId, string? Url, DateTime LastSyncedAt);

public record MoneyDto(decimal Amount, string Currency);

public record AcquisitionDto(DateTime? AcquiredAt, MoneyDto? Price, string? Vendor);

public record ItemDto(
    string Id,
    string CategoryId,
    string Name,
    string? Description,
    IReadOnlyList<ItemImageDto> Images,
    IReadOnlyList<string> Tags,
    bool IsShowcased,
    string Source,
    ExternalRefDto? ExternalRef,
    AcquisitionDto? Acquisition,
    string? LocationId,
    IReadOnlyDictionary<string, object?> Attributes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public static class ItemMapper
{
    public static ItemDto ToDto(Item item) => new(
        item.Id.ToString(),
        item.CategoryId.ToString(),
        item.Name,
        item.Description,
        item.Images.Select(i => new ItemImageDto(i.Id, i.Path, i.CardPath, i.ThumbPath, i.IsPrimary, i.Order)).ToArray(),
        item.Tags,
        item.IsShowcased,
        item.Source.ToString(),
        item.ExternalRef is null
            ? null
            : new ExternalRefDto(item.ExternalRef.Provider, item.ExternalRef.ExternalId, item.ExternalRef.Url, item.ExternalRef.LastSyncedAt),
        item.Acquisition is null
            ? null
            : new AcquisitionDto(
                item.Acquisition.AcquiredAt,
                item.Acquisition.Price is null ? null : new MoneyDto(item.Acquisition.Price.Amount, item.Acquisition.Price.Currency),
                item.Acquisition.Vendor),
        item.LocationId?.ToString(),
        BsonJson.ToDictionary(item.Attributes),
        item.CreatedAt,
        item.UpdatedAt);
}
```

- [ ] **Step 4: 實作 Command**

`src/MyCollection.Application/Items/ItemCommands.cs`：

```csharp
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Items;

public record AcquisitionInput(DateTime? AcquiredAt, decimal? Amount, string? Currency, string? Vendor);

public record CreateItemCommand(
    string CategoryId,
    string Name,
    string? Description,
    IReadOnlyList<string> Tags,
    bool IsShowcased,
    JsonElement Attributes,
    AcquisitionInput? Acquisition,
    string? LocationId = null) : IRequest<ItemDto>;

public record UpdateItemCommand(
    string Id,
    string CategoryId,
    string Name,
    string? Description,
    IReadOnlyList<string> Tags,
    bool IsShowcased,
    JsonElement Attributes,
    AcquisitionInput? Acquisition,
    string? LocationId = null) : IRequest<ItemDto>;

public record DeleteItemCommand(string Id) : IRequest;

public sealed class CreateItemCommandValidator : AbstractValidator<CreateItemCommand>
{
    public CreateItemCommandValidator()
    {
        RuleFor(x => x.CategoryId).Must(id => ObjectId.TryParse(id, out _)).WithMessage("Invalid category id.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleForEach(x => x.Tags).NotEmpty().MaximumLength(50);
    }
}

public sealed class UpdateItemCommandValidator : AbstractValidator<UpdateItemCommand>
{
    public UpdateItemCommandValidator()
    {
        RuleFor(x => x.Id).Must(id => ObjectId.TryParse(id, out _)).WithMessage("Invalid item id.");
        RuleFor(x => x.CategoryId).Must(id => ObjectId.TryParse(id, out _)).WithMessage("Invalid category id.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleForEach(x => x.Tags).NotEmpty().MaximumLength(50);
    }
}

/// <summary>Create 與 Update 共用的 schema 驗證與欄位套用邏輯。</summary>
internal static class ItemWriteHelper
{
    public static async Task<(Category Category, BsonDocument Attributes)> ResolveAsync(
        ICategoryRepository categories,
        IAttributeValidator attributeValidator,
        string categoryId,
        JsonElement attributes,
        CancellationToken ct)
    {
        var id = ObjectId.Parse(categoryId);
        var category = await categories.GetAsync(id, ct)
                       ?? throw new NotFoundException(nameof(Category), categoryId);

        var document = attributes.ValueKind == JsonValueKind.Undefined ? [] : BsonJson.ToBson(attributes);

        var failures = attributeValidator.Validate(category, document);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return (category, document);
    }

    public static Acquisition? ToAcquisition(AcquisitionInput? input)
    {
        if (input is null)
        {
            return null;
        }

        return new Acquisition
        {
            // 沒帶 Z 的輸入視為 UTC；資料層的 UtcOnlyDateTimeSerializer 會拒絕非 UTC 值
            AcquiredAt = UtcDate.Normalise(input.AcquiredAt),
            Price = input.Amount is { } amount
                ? new Money(amount, string.IsNullOrWhiteSpace(input.Currency) ? "TWD" : input.Currency)
                : null,
            Vendor = input.Vendor
        };
    }

    public static ObjectId? ToLocationId(Category category, string? locationId) =>
        category.Kind == CategoryKind.Digital || string.IsNullOrWhiteSpace(locationId)
            ? null
            : ObjectId.Parse(locationId);

    public static List<string> NormaliseTags(IReadOnlyList<string> tags) =>
        tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
}

public sealed class CreateItemCommandHandler(
    IItemRepository items,
    ICategoryRepository categories,
    IAttributeValidator attributeValidator,
    TimeProvider timeProvider) : IRequestHandler<CreateItemCommand, ItemDto>
{
    public async Task<ItemDto> Handle(CreateItemCommand request, CancellationToken cancellationToken)
    {
        var (category, attributes) = await ItemWriteHelper.ResolveAsync(
            categories, attributeValidator, request.CategoryId, request.Attributes, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var item = new Item
        {
            Id = ObjectId.GenerateNewId(),
            CategoryId = category.Id,
            Name = request.Name.Trim(),
            Description = request.Description,
            Tags = ItemWriteHelper.NormaliseTags(request.Tags),
            IsShowcased = request.IsShowcased,
            Source = ItemSource.Manual,
            Acquisition = ItemWriteHelper.ToAcquisition(request.Acquisition),
            LocationId = ItemWriteHelper.ToLocationId(category, request.LocationId),
            Attributes = attributes,
            CreatedAt = now,
            UpdatedAt = now
        };

        await items.InsertAsync(item, cancellationToken);

        return ItemMapper.ToDto(item);
    }
}

public sealed class UpdateItemCommandHandler(
    IItemRepository items,
    ICategoryRepository categories,
    IAttributeValidator attributeValidator,
    TimeProvider timeProvider) : IRequestHandler<UpdateItemCommand, ItemDto>
{
    public async Task<ItemDto> Handle(UpdateItemCommand request, CancellationToken cancellationToken)
    {
        var id = ObjectId.Parse(request.Id);
        var existing = await items.GetAsync(id, cancellationToken)
                       ?? throw new NotFoundException(nameof(Item), request.Id);

        var (category, attributes) = await ItemWriteHelper.ResolveAsync(
            categories, attributeValidator, request.CategoryId, request.Attributes, cancellationToken);

        // Source / ExternalRef / Images / CreatedAt 不接受使用者輸入，由同步與 Media 模組管理
        existing.CategoryId = category.Id;
        existing.Name = request.Name.Trim();
        existing.Description = request.Description;
        existing.Tags = ItemWriteHelper.NormaliseTags(request.Tags);
        existing.IsShowcased = request.IsShowcased;
        existing.Acquisition = ItemWriteHelper.ToAcquisition(request.Acquisition);
        existing.LocationId = ItemWriteHelper.ToLocationId(category, request.LocationId);
        existing.Attributes = attributes;
        existing.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await items.UpdateAsync(existing, cancellationToken);

        return ItemMapper.ToDto(existing);
    }
}

public sealed class DeleteItemCommandHandler(IItemRepository items) : IRequestHandler<DeleteItemCommand>
{
    public Task Handle(DeleteItemCommand request, CancellationToken cancellationToken)
    {
        // 同 GetItemQueryHandler：DeleteItemCommand 無 validator，不合法 id 必須回 404 而非 500
        if (!ObjectId.TryParse(request.Id, out var id))
        {
            throw new NotFoundException(nameof(Item), request.Id);
        }

        return items.DeleteAsync(id, cancellationToken);
    }
}
```

- [ ] **Step 5: 實作 Query**

`src/MyCollection.Application/Items/ItemQueries.cs`：

```csharp
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Items;

public record SearchItemsQuery(
    string? Search = null,
    string? CategoryId = null,
    IReadOnlyList<string>? Tags = null,
    bool? IsShowcased = null,
    int Page = 1,
    int PageSize = 24) : IRequest<PagedResult<ItemDto>>;

public record GetItemQuery(string Id) : IRequest<ItemDto>;

public record ListTagsQuery : IRequest<IReadOnlyList<string>>;

public sealed class SearchItemsQueryValidator : AbstractValidator<SearchItemsQuery>
{
    public SearchItemsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.CategoryId)
            .Must(id => ObjectId.TryParse(id, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.CategoryId))
            .WithMessage("Invalid category id.");
    }
}

public sealed class SearchItemsQueryHandler(IItemRepository items)
    : IRequestHandler<SearchItemsQuery, PagedResult<ItemDto>>
{
    public async Task<PagedResult<ItemDto>> Handle(SearchItemsQuery request, CancellationToken cancellationToken)
    {
        var spec = new ItemQuerySpec
        {
            Search = request.Search,
            CategoryId = string.IsNullOrWhiteSpace(request.CategoryId) ? null : ObjectId.Parse(request.CategoryId),
            Tags = request.Tags,
            IsShowcased = request.IsShowcased,
            Page = request.Page,
            PageSize = request.PageSize
        };

        var result = await items.SearchAsync(spec, cancellationToken);

        return new PagedResult<ItemDto>(
            result.Items.Select(ItemMapper.ToDto).ToArray(),
            result.Total,
            result.Page,
            result.PageSize);
    }
}

public sealed class GetItemQueryHandler(IItemRepository items) : IRequestHandler<GetItemQuery, ItemDto>
{
    public async Task<ItemDto> Handle(GetItemQuery request, CancellationToken cancellationToken)
    {
        // GetItemQuery 沒有 validator，直接 Parse 會讓 GET /items/abc 擲 FormatException → 500。
        // 不合法的 id 是「找不到」而非伺服器錯誤。
        if (!ObjectId.TryParse(request.Id, out var id))
        {
            throw new NotFoundException(nameof(Item), request.Id);
        }

        var item = await items.GetAsync(id, cancellationToken)
                   ?? throw new NotFoundException(nameof(Item), request.Id);

        return ItemMapper.ToDto(item);
    }
}

public sealed class ListTagsQueryHandler(IItemRepository items) : IRequestHandler<ListTagsQuery, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> Handle(ListTagsQuery request, CancellationToken cancellationToken) =>
        items.ListTagsAsync(cancellationToken);
}
```

- [ ] **Step 6: 跑測試確認通過**

Run: `dotnet test --filter ItemCommandTests`
Expected: `Passed: 6`

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "feat(items): 新增品項 CRUD command 與搜尋 query"
```

---

### Task 8：Catalog 端點與端到端測試

**Files:**
- Create: `src/MyCollection.Api/Endpoints/CategoryEndpoints.cs`
- Create: `src/MyCollection.Api/Endpoints/ItemEndpoints.cs`
- Modify: `src/MyCollection.Api/Program.cs`
- Create: `tests/MyCollection.Tests/Fixtures/AuthenticatedClient.cs`
- Test: `tests/MyCollection.Tests/Integration/CatalogEndpointsTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Fixtures/AuthenticatedClient.cs`：

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MyCollection.Application.Auth;

namespace MyCollection.Tests.Fixtures;

public static class AuthenticatedClient
{
    /// <summary>註冊一個新帳號並回傳已帶好 Bearer token 的 client。</summary>
    public static async Task<HttpClient> CreateAsync(ApiFactory factory, string email)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/register", new { email, password = "P@ssw0rd!", displayName = "Tester" });
        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        return client;
    }
}
```

`tests/MyCollection.Tests/Integration/CatalogEndpointsTests.cs`：

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
public class CatalogEndpointsTests(MongoFixture mongo) : IAsyncLifetime
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

    private async Task<CategoryDto> CreateFigureCategoryAsync()
    {
        var response = await _client.PostAsJsonAsync("/categories", new
        {
            name = "公仔",
            icon = "figure",
            kind = "Physical",
            fields = new[]
            {
                // 第一個元素必須明確標成 string[]?：匿名型別陣列以第一個元素推論型別，
                // 若這裡是 non-nullable string[]，下一個元素的 (string[]?)null 會觸發 CS8619（→ 建置失敗）
                new { key = "brand", label = "廠商", type = "Select", options = (string[]?)["GSC", "ALTER"], required = true, searchable = true, showOnCard = true },
                new { key = "scale", label = "比例", type = "Text", options = (string[]?)null, required = false, searchable = false, showOnCard = false }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CategoryDto>())!;
    }

    private async Task<HttpResponseMessage> CreateItemAsync(string categoryId, string name, object attributes, string[]? tags = null) =>
        await _client.PostAsJsonAsync("/items", new
        {
            categoryId,
            name,
            description = (string?)null,
            tags = tags ?? [],
            isShowcased = false,
            attributes,
            acquisition = (object?)null
        });

    [Fact]
    public async Task Full_catalog_round_trip()
    {
        var category = await CreateFigureCategoryAsync();

        var created = await CreateItemAsync(category.Id, "初音ミク 1/8", new { brand = "GSC", scale = "1/8" }, ["VOCALOID"]);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var item = (await created.Content.ReadFromJsonAsync<ItemDto>())!;

        var fetched = await _client.GetFromJsonAsync<ItemDto>($"/items/{item.Id}");
        fetched!.Name.Should().Be("初音ミク 1/8");
        ((JsonElement)fetched.Attributes["brand"]!).GetString().Should().Be("GSC");

        var updated = await _client.PutAsJsonAsync($"/items/{item.Id}", new
        {
            categoryId = category.Id,
            name = "初音ミク 1/8（改）",
            description = "已開封",
            tags = new[] { "VOCALOID", "GSC" },
            isShowcased = true,
            attributes = new { brand = "ALTER" },
            acquisition = new { acquiredAt = "2026-01-01T00:00:00Z", amount = 12800, currency = "TWD", vendor = "GSC 官網" }
        });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        var deleted = await _client.DeleteAsync($"/items/{item.Id}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _client.GetAsync($"/items/{item.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Item_violating_schema_returns_400_with_field_errors()
    {
        var category = await CreateFigureCategoryAsync();

        var response = await CreateItemAsync(category.Id, "無廠商", new { scale = "1/8" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").TryGetProperty("attributes.brand", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Item_with_value_outside_select_options_returns_400()
    {
        var category = await CreateFigureCategoryAsync();

        var response = await CreateItemAsync(category.Id, "未知廠商", new { brand = "MegaHouse" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Search_filters_by_tag_and_returns_paged_shape()
    {
        var category = await CreateFigureCategoryAsync();
        await CreateItemAsync(category.Id, "A", new { brand = "GSC" }, ["紅"]);
        await CreateItemAsync(category.Id, "B", new { brand = "GSC" }, ["藍"]);

        var result = await _client.GetFromJsonAsync<PagedItemsResponse>("/items?tags=紅&page=1&pageSize=10");

        result!.Total.Should().Be(1);
        result.Items.Should().ContainSingle().Which.Name.Should().Be("A");
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task Tags_endpoint_returns_distinct_tags()
    {
        var category = await CreateFigureCategoryAsync();
        await CreateItemAsync(category.Id, "A", new { brand = "GSC" }, ["紅", "限定"]);
        await CreateItemAsync(category.Id, "B", new { brand = "GSC" }, ["紅"]);

        var tags = await _client.GetFromJsonAsync<string[]>("/items/tags");

        tags.Should().BeEquivalentTo("紅", "限定");
    }

    [Fact]
    public async Task Another_user_cannot_see_or_modify_items()
    {
        var category = await CreateFigureCategoryAsync();
        var item = (await (await CreateItemAsync(category.Id, "我的公仔", new { brand = "GSC" }))
            .Content.ReadFromJsonAsync<ItemDto>())!;

        using var intruder = await AuthenticatedClient.CreateAsync(_factory, "intruder@example.com");

        (await intruder.GetAsync($"/items/{item.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.DeleteAsync($"/items/{item.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await intruder.GetFromJsonAsync<PagedItemsResponse>("/items");
        list!.Total.Should().Be(0);
    }

    [Fact]
    public async Task Categories_endpoint_requires_authentication()
    {
        using var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/categories")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed record PagedItemsResponse(ItemDto[] Items, long Total, int Page, int PageSize);
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter CatalogEndpointsTests`
Expected: 全部 FAIL（`/categories`、`/items` 回 404）。

- [ ] **Step 3: 實作端點**

`src/MyCollection.Api/Endpoints/CategoryEndpoints.cs`：

```csharp
using MediatR;
using MyCollection.Application.Categories;

namespace MyCollection.Api.Endpoints;

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/categories").WithTags("Categories").RequireAuthorization();

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListCategoriesQuery(), ct)));

        group.MapPost("/", async (CreateCategoryCommand command, ISender sender, CancellationToken ct) =>
        {
            var created = await sender.Send(command, ct);
            return Results.Created($"/categories/{created.Id}", created);
        });

        group.MapPut("/{id}", async (string id, UpdateCategoryCommand body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(body with { Id = id }, ct)));

        group.MapDelete("/{id}", async (string id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteCategoryCommand(id), ct);
            return Results.NoContent();
        });

        return app;
    }
}
```

`src/MyCollection.Api/Endpoints/ItemEndpoints.cs`：

```csharp
using MediatR;
using MyCollection.Application.Items;

namespace MyCollection.Api.Endpoints;

public static class ItemEndpoints
{
    public static IEndpointRouteBuilder MapItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/items").WithTags("Items").RequireAuthorization();

        group.MapGet("/", async (
            string? search,
            string? categoryId,
            string[]? tags,
            bool? isShowcased,
            int? page,
            int? pageSize,
            ISender sender,
            CancellationToken ct) =>
            Results.Ok(await sender.Send(new SearchItemsQuery(
                search, categoryId, tags, isShowcased, page ?? 1, pageSize ?? 24), ct)));

        group.MapGet("/tags", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListTagsQuery(), ct)));

        group.MapGet("/{id}", async (string id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetItemQuery(id), ct)));

        group.MapPost("/", async (CreateItemCommand command, ISender sender, CancellationToken ct) =>
        {
            var created = await sender.Send(command, ct);
            return Results.Created($"/items/{created.Id}", created);
        });

        group.MapPut("/{id}", async (string id, UpdateItemCommand body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(body with { Id = id }, ct)));

        group.MapDelete("/{id}", async (string id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteItemCommand(id), ct);
            return Results.NoContent();
        });

        return app;
    }
}
```

`/items/tags` 必須註冊在 `/items/{id}` **之前**，否則 `tags` 會被當成 id。

`src/MyCollection.Api/Program.cs` 的 `app.MapAuthEndpoints();` 之後追加：

```csharp
app.MapCategoryEndpoints();
app.MapItemEndpoints();
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter CatalogEndpointsTests`
Expected: `Passed: 7`

若 `GetItemQuery` 傳入非法 ObjectId 造成 500，在 `GetItemQueryHandler` 開頭改為：

```csharp
        if (!ObjectId.TryParse(request.Id, out var id))
        {
            throw new NotFoundException(nameof(Item), request.Id);
        }
```

並將後續 `ObjectId.Parse(request.Id)` 換成 `id`。同樣處理 `DeleteItemCommandHandler` 與 `DeleteCategoryCommandHandler`。

- [ ] **Step 5: 跑全部測試**

Run: `dotnet test`
Expected: `Failed: 0`

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(api): 新增品類與品項端點"
```

---

## 驗收

- [ ] `dotnet test` 全綠
- [ ] 建立自訂品類 → 建立品項 → 更新 → 搜尋 → 刪除，全流程 API 可跑通
- [ ] 缺 required 屬性的品項回 400，`errors` 內含 `attributes.<key>`
- [ ] 另一使用者對他人品項一律得到 404，且 `/items` 看不到他人資料
- [ ] 新增一個全新品類（例如「啦啦隊商品」）不需修改任何 C# 程式碼

**下一步：** `docs/superpowers/plans/2026-07-25-03-media-showcase-sharing.md`
