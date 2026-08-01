# IGDB 遊戲中繼資料整合（後端）實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **前置：** master 分支全綠（`dotnet test` 全過）。
>
> **範圍：** 僅後端。前端（IGDB 搜尋 modal、批次補完按鈕）另立計畫，見文末「後續」。
>
> **設計文件：** `docs/superpowers/specs/2026-08-01-igdb-metadata-design.md`

**Goal:** 透過 IGDB 提供遊戲搜尋建檔與 Steam 品項批次補完，並把 `IMetadataProvider` 從單一肥介面拆成三個能力介面。

**Architecture:** IGDB 走 Twitch client credentials（server-to-server，無 redirect、無 HTTPS 需求），憑證以環境變數全站共用，未設定時 provider 不註冊、功能自動隱藏。`IMetadataProvider` 只留 `Key`，能力拆成 `IBulkSyncProvider` / `IUrlLookupProvider` / `ISearchProvider`，`ProviderCapability` 旗標改由介面推導以杜絕漂移。補完只做精準比對（Steam appid 或既有 `igdbId`），寫入時 `$set` 只碰 provider 欄位，不動使用者手動編輯的欄位。

**Tech Stack:** .NET 10 · MediatR · MongoDB 原生驅動 · xUnit + FluentAssertions + Moq + `FakeTimeProvider` + Testcontainers

---

## 檔案結構

| 檔案 | 職責 |
|---|---|
| `src/MyCollection.Application/Ingestion/IMetadataProvider.cs` | **改**：拆為 4 個介面 + `ExternalLookupResult`，移除 `Capabilities` |
| `src/MyCollection.Application/Ingestion/ProviderKeys.cs` | **新**：provider key 常數，消除各層字面值 |
| `src/MyCollection.Application/Ingestion/ProviderCapabilities.cs` | **新**：由介面推導旗標 |
| `src/MyCollection.Application/Ingestion/ProviderRegistry.cs` | **改**：`Require<T>` 取代能力旗標多載 |
| `src/MyCollection.Application/Ingestion/SearchProviderQuery.cs` | **新**：關鍵字搜尋 |
| `src/MyCollection.Application/Ingestion/IItemEnrichWriter.cs` | **新**：只寫 provider 欄位的 bulk update 契約 |
| `src/MyCollection.Application/Ingestion/EnrichCommand.cs` | **新**：批次／單筆補完編排 |
| `src/MyCollection.Infrastructure/Providers/Igdb/IgdbOptions.cs` | **新**：設定與 `IsConfigured` |
| `src/MyCollection.Infrastructure/Providers/Igdb/IgdbFields.cs` | **新**：IGDB 欄位定義的唯一來源 |
| `src/MyCollection.Infrastructure/Providers/Igdb/TwitchTokenProvider.cs` | **新**：token 快取與 single-flight |
| `src/MyCollection.Infrastructure/Providers/Igdb/IgdbRateLimiter.cs` | **新**：程序層級最小請求間隔 |
| `src/MyCollection.Infrastructure/Providers/Igdb/IgdbMapper.cs` | **新**：IGDB JSON → `ExternalItem` |
| `src/MyCollection.Infrastructure/Providers/Igdb/IgdbProvider.cs` | **新**：`ISearchProvider` 實作 |
| `src/MyCollection.Infrastructure/Mongo/MongoItemEnrichWriter.cs` | **新** |
| `src/MyCollection.Infrastructure/Mongo/SystemCategoryDefinitions.cs` | **改**：兩個遊戲品類加 5 個 IGDB 欄位 |
| `src/MyCollection.Infrastructure/Mongo/MongoItemRepository.cs` | **改**：補完候選查詢與依 id 批次載入 |
| `src/MyCollection.Application/Items/IItemRepository.cs` | **改**：上述兩個方法的契約 |
| `src/MyCollection.Infrastructure/Providers/SteamProvider.cs` | **改**：改實作 `IBulkSyncProvider`，刪樁 |
| `src/MyCollection.Infrastructure/Providers/OpenGraphProvider.cs` | **改**：改實作 `IUrlLookupProvider`，刪樁 |
| `src/MyCollection.Infrastructure/DependencyInjection.cs` | **改**：IGDB 選配註冊 |
| `src/MyCollection.Domain/Entities/SyncJob.cs` | **改**：新增 `Skipped` |
| `src/MyCollection.Application/Ingestion/SyncCommand.cs` | **改**：`Require<IBulkSyncProvider>`、DTO 帶 `Skipped` |
| `src/MyCollection.Application/Ingestion/FetchByUrlQuery.cs` | **改**：`Require<IUrlLookupProvider>` |
| `src/MyCollection.Api/Endpoints/IngestionEndpoints.cs` | **改**：`/search`、`/enrich/{provider}` |
| `docker-compose.yml`、`.env.example`、`appsettings.json` | **改**：IGDB 設定 |

Task 1–2 是重構與資料模型，3–7 是 IGDB 客戶端，8 是品類欄位，9–10 是補完的資料層，
11 是搜尋，12 是補完編排，13 是設定與文件，14 為可選。

**設計文件 §4.1 有一項不需要後端改動：** 搜尋建檔產生的品項 `Source = ItemSource.Manual`。
搜尋端點只回傳 `FetchedMetadataDto` 供前端預填表單，實際建檔仍走既有的 `CreateItemCommand`，
其預設就是 `ItemSource.Manual`。**刻意不新增 `ItemSource.Igdb`**——IGDB 沒有「你的收藏」概念，
不會有後續同步覆蓋這些品項，標成 IGDB 來源會讓 `$setOnInsert` 那套保護機制的語意錯亂。
來源資訊靠 `igdbId` attribute 保留即可。

---

### Task 1：拆分 Provider 能力介面

**Files:**
- Modify: `src/MyCollection.Application/Ingestion/IMetadataProvider.cs`
- Create: `src/MyCollection.Application/Ingestion/ProviderKeys.cs`
- Create: `src/MyCollection.Application/Ingestion/ProviderCapabilities.cs`
- Modify: `src/MyCollection.Application/Ingestion/ProviderRegistry.cs`
- Modify: `src/MyCollection.Application/Ingestion/SyncCommand.cs:53`
- Modify: `src/MyCollection.Application/Ingestion/FetchByUrlQuery.cs:40`
- Modify: `src/MyCollection.Application/Ingestion/ExternalAccountCommands.cs`（`LinkExternalAccountCommandHandler` 也用了舊多載）
- Modify: `src/MyCollection.Infrastructure/Providers/SteamProvider.cs`
- Modify: `src/MyCollection.Infrastructure/Providers/OpenGraphProvider.cs`
- Modify: `src/MyCollection.Api/Endpoints/IngestionEndpoints.cs:12-17`
- Test: `tests/MyCollection.Tests/Unit/ProviderRegistryTests.cs`（整份改寫）
- Test: `tests/MyCollection.Tests/Unit/SteamProviderTests.cs`（刪 2 個案例、改 1 個）
- Test: `tests/MyCollection.Tests/Unit/OpenGraphProviderTests.cs`（刪 1 個案例、改 1 個）
- Test: `tests/MyCollection.Tests/Unit/SyncCommandTests.cs`、`ExternalAccountCommandTests.cs`、`FetchByUrlQueryTests.cs`
  （這三個檔案 mock 了 `IMetadataProvider` 並設定 `Capabilities`，改成 mock 對應的能力介面即可。
  `SyncCommandTests` 的 `Provider_without_bulk_sync_is_rejected` 要**保留** `Mock<IMetadataProvider>`——
  那個測試就是在驗證 `Require<T>` 轉型失敗的路徑。）

這一步無法拆成更小的 commit——介面簽章改變會讓整個方案在中途無法編譯。以編譯器找出所有呼叫端，
上面的清單已涵蓋 master 當下的全部。步驟仍逐一列出。

- [ ] **Step 1: 改寫 ProviderRegistryTests**

整份取代 `tests/MyCollection.Tests/Unit/ProviderRegistryTests.cs`：

```csharp
using FluentAssertions;
using Moq;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class ProviderRegistryTests
{
    private static IMetadataProvider BulkSync(string key)
    {
        var mock = new Mock<IBulkSyncProvider>();
        mock.SetupGet(p => p.Key).Returns(key);
        return mock.Object;
    }

    private static IMetadataProvider UrlLookup(string key)
    {
        var mock = new Mock<IUrlLookupProvider>();
        mock.SetupGet(p => p.Key).Returns(key);
        return mock.Object;
    }

    private static IMetadataProvider Search(string key)
    {
        var mock = new Mock<ISearchProvider>();
        mock.SetupGet(p => p.Key).Returns(key);
        return mock.Object;
    }

    private static ProviderRegistry CreateSut() =>
        new([BulkSync("steam"), UrlLookup("opengraph"), Search("igdb")]);

    [Fact]
    public void Resolves_provider_by_key_case_insensitively()
    {
        CreateSut().Require("STEAM").Key.Should().Be("steam");
    }

    [Fact]
    public void Unknown_key_throws_NotFoundException()
    {
        var act = () => CreateSut().Require("psn");

        act.Should().Throw<NotFoundException>();
    }

    [Fact]
    public void Generic_Require_returns_the_provider_when_the_capability_interface_matches()
    {
        CreateSut().Require<ISearchProvider>("igdb").Key.Should().Be("igdb");
    }

    [Fact]
    public void Generic_Require_throws_ProviderException_when_the_interface_does_not_match()
    {
        var act = () => CreateSut().Require<IBulkSyncProvider>("opengraph");

        act.Should().Throw<ProviderException>()
            .Which.ProviderKey.Should().Be("opengraph");
    }

    [Fact]
    public void Generic_Require_still_throws_NotFoundException_for_an_unknown_key()
    {
        var act = () => CreateSut().Require<ISearchProvider>("psn");

        act.Should().Throw<NotFoundException>();
    }

    [Fact]
    public void Lists_all_registered_providers()
    {
        CreateSut().All.Select(p => p.Key).Should().BeEquivalentTo("steam", "opengraph", "igdb");
    }

    [Theory]
    [InlineData("steam", ProviderCapability.BulkSync)]
    [InlineData("opengraph", ProviderCapability.UrlLookup)]
    [InlineData("igdb", ProviderCapability.Search)]
    public void Derives_capabilities_from_the_implemented_interfaces(string key, ProviderCapability expected)
    {
        ProviderCapabilities.Of(CreateSut().Require(key)).Should().Be(expected);
    }

    [Fact]
    public void Derives_combined_capabilities_when_one_provider_implements_two_interfaces()
    {
        var mock = new Mock<IMetadataProvider>();
        mock.SetupGet(p => p.Key).Returns("hybrid");
        mock.As<IBulkSyncProvider>();
        mock.As<IUrlLookupProvider>();

        ProviderCapabilities.Of(mock.Object).Should()
            .Be(ProviderCapability.BulkSync | ProviderCapability.UrlLookup);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter ProviderRegistryTests`
Expected: 編譯失敗，找不到 `IBulkSyncProvider` / `IUrlLookupProvider` / `ISearchProvider` / `ProviderCapabilities`。

- [ ] **Step 3: 建立 ProviderKeys**

`src/MyCollection.Application/Ingestion/ProviderKeys.cs`：

```csharp
namespace MyCollection.Application.Ingestion;

/// <summary>
/// Provider key 是寫進 externalRef.provider 與 API 路由的識別字串。
/// 集中一處，避免 Application 層散落字面值。
/// </summary>
public static class ProviderKeys
{
    public const string Steam = "steam";
    public const string OpenGraph = "opengraph";
    public const string Igdb = "igdb";
}
```

- [ ] **Step 4: 改寫 IMetadataProvider.cs**

整份取代：

```csharp
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Ingestion;

[Flags]
public enum ProviderCapability
{
    None = 0,

    /// <summary>可用已綁定帳號一次拉回全部品項。</summary>
    BulkSync = 1,

    /// <summary>可從單一 URL 擷取品項資料。</summary>
    UrlLookup = 2,

    /// <summary>可依關鍵字搜尋，並以外部識別碼反查。</summary>
    Search = 4
}

/// <summary>Provider 回傳的中性結構，尚未綁定任何品類 schema。</summary>
public record ExternalItem(
    string ExternalId,
    string Name,
    string? Description,
    Uri? ImageUrl,
    IReadOnlyDictionary<string, object?> Attributes)
{
    public Uri? SourceUrl { get; init; }
}

/// <summary>Found 的 key 是傳入的 externalId。三種結果互斥：命中、查無、請求失敗。</summary>
public record ExternalLookupResult(
    IReadOnlyDictionary<string, ExternalItem> Found,
    IReadOnlyList<string> FailedIds);

/// <summary>
/// 所有 provider 的共同基底，只帶識別。能力由下方三個介面表達。
/// 舊版把三種能力塞在同一個介面，逼每個 provider 實作用不到的樁，
/// 且 Capabilities 旗標與實際實作是兩處來源，會漂移。
/// </summary>
public interface IMetadataProvider
{
    /// <summary>見 <see cref="ProviderKeys"/>。全小寫。</summary>
    string Key { get; }
}

public interface IBulkSyncProvider : IMetadataProvider
{
    /// <summary>失敗時擲 <see cref="Domain.Exceptions.ProviderException"/>。</summary>
    Task<IReadOnlyList<ExternalItem>> SyncAsync(ExternalAccount account, CancellationToken ct);
}

public interface IUrlLookupProvider : IMetadataProvider
{
    /// <summary>抓不到可用中繼資料時回傳 null。</summary>
    Task<ExternalItem?> FetchByUrlAsync(Uri url, CancellationToken ct);
}

public interface ISearchProvider : IMetadataProvider
{
    /// <summary>標記「此品項已綁定本 provider」的 attribute key，也是批次補完的篩選依據。</summary>
    string MarkerAttributeKey { get; }

    /// <summary>寫入 attributes 時，目標品類必須宣告的欄位。</summary>
    IReadOnlyList<CategoryField> RequiredFields { get; }

    /// <summary>失敗時擲 <see cref="Domain.Exceptions.ProviderException"/>。</summary>
    Task<IReadOnlyList<ExternalItem>> SearchAsync(string query, int limit, CancellationToken ct);

    /// <summary>
    /// 以 "steam:440" / "igdb:1942" 形式的外部識別碼批次反查，內部自行分塊與節流。
    /// 查無對應者不出現在 Found；請求層級失敗者列入 FailedIds。
    /// </summary>
    Task<ExternalLookupResult> FetchByExternalIdsAsync(
        IReadOnlyList<string> externalIds, CancellationToken ct);
}
```

- [ ] **Step 5: 建立 ProviderCapabilities**

`src/MyCollection.Application/Ingestion/ProviderCapabilities.cs`：

```csharp
namespace MyCollection.Application.Ingestion;

/// <summary>
/// 由實作的介面推導能力旗標。Provider 不再自行宣告 Capabilities——
/// 同一個事實兩處來源，遲早漂移成「旗標說支援、方法沒實作」。
/// </summary>
public static class ProviderCapabilities
{
    public static ProviderCapability Of(IMetadataProvider provider) =>
        (provider is IBulkSyncProvider ? ProviderCapability.BulkSync : ProviderCapability.None)
        | (provider is IUrlLookupProvider ? ProviderCapability.UrlLookup : ProviderCapability.None)
        | (provider is ISearchProvider ? ProviderCapability.Search : ProviderCapability.None);
}
```

- [ ] **Step 6: 改寫 ProviderRegistry 的能力多載**

`src/MyCollection.Application/Ingestion/ProviderRegistry.cs`，把 `Require(string key, ProviderCapability capability)` 整個方法換成：

```csharp
    /// <summary>
    /// 解析並要求特定能力介面。回傳強型別，呼叫端不需再轉型，
    /// 也不可能出現「旗標檢查過了但方法不存在」。
    /// </summary>
    public T Require<T>(string key) where T : class, IMetadataProvider
    {
        var provider = Require(key);

        return provider as T
               ?? throw new ProviderException(
                   provider.Key, $"Provider '{provider.Key}' does not support {typeof(T).Name}.");
    }
```

- [ ] **Step 7: 改 SteamProvider**

`src/MyCollection.Infrastructure/Providers/SteamProvider.cs`：

1. 類別宣告 `: IMetadataProvider` 改為 `: IBulkSyncProvider`
2. 刪除第 21 行 `public ProviderCapability Capabilities => ProviderCapability.BulkSync;`
3. 刪除第 60–62 行的 `FetchByUrlAsync` 樁（含其上方註解）
4. `public const string ProviderKey = "steam";` 改為 `public const string ProviderKey = ProviderKeys.Steam;`

- [ ] **Step 8: 改 OpenGraphProvider**

`src/MyCollection.Infrastructure/Providers/OpenGraphProvider.cs`：

1. 類別宣告改為 `: IUrlLookupProvider`
2. 刪除 `public ProviderCapability Capabilities => ProviderCapability.UrlLookup;`
3. 刪除擲 `ProviderException` 的 `SyncAsync` 樁
4. `public const string ProviderKey = "opengraph";` 改為 `= ProviderKeys.OpenGraph;`
5. 刪除因而未使用的 `using MyCollection.Domain.Entities;`（`ExternalAccount` 已不再出現）

- [ ] **Step 9: 改兩個呼叫端**

`src/MyCollection.Application/Ingestion/SyncCommand.cs:53`：

```csharp
        var provider = registry.Require<IBulkSyncProvider>(request.Provider);
```

`src/MyCollection.Application/Ingestion/FetchByUrlQuery.cs:40`：

```csharp
        var provider = registry.Require<IUrlLookupProvider>(request.Provider);
```

- [ ] **Step 10: 改 /providers 端點**

`src/MyCollection.Api/Endpoints/IngestionEndpoints.cs:12-17`：

```csharp
        group.MapGet("/providers", (ProviderRegistry registry) =>
            Results.Ok(registry.All.Select(p => new
            {
                key = p.Key,
                capabilities = ProviderCapabilities.Of(p).ToString()
            })));
```

- [ ] **Step 11: 清掉兩個 provider 測試裡的「不支援」案例**

`tests/MyCollection.Tests/Unit/SteamProviderTests.cs`：

刪除 `FetchByUrl_is_not_supported` 整個方法，並把 `Declares_bulk_sync_capability_only` 換成：

```csharp
    [Fact]
    public void Declares_bulk_sync_capability_only()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json("{}"));

        sut.Key.Should().Be("steam");
        ProviderCapabilities.Of(sut).Should().Be(ProviderCapability.BulkSync);
    }
```

`tests/MyCollection.Tests/Unit/OpenGraphProviderTests.cs`：

刪除 `Sync_is_not_supported` 整個方法（以及隨之未使用的 `using MongoDB.Bson;`、`using MyCollection.Domain.Entities;`），並把 `Declares_url_lookup_capability_only` 換成：

```csharp
    [Fact]
    public void Declares_url_lookup_capability_only()
    {
        var sut = CreateSut(StubHttpMessageHandler.Html("<html></html>"));

        sut.Key.Should().Be("opengraph");
        ProviderCapabilities.Of(sut).Should().Be(ProviderCapability.UrlLookup);
    }
```

- [ ] **Step 12: 跑全部測試確認通過**

Run: `dotnet test`
Expected: 全綠。`ProviderRegistryTests` 為 `Passed: 10`（7 個 `[Fact]` + `[Theory]` 展開 3 個）、`SteamProviderTests` 為 `Passed: 10`、`OpenGraphProviderTests` 為 `Passed: 7`。

- [ ] **Step 13: Commit**

```bash
git add src tests
git commit -m "refactor(ingestion): split IMetadataProvider into capability interfaces"
```

---

### Task 2：SyncJob 新增 Skipped

**Files:**
- Modify: `src/MyCollection.Domain/Entities/SyncJob.cs`
- Modify: `src/MyCollection.Application/Ingestion/SyncCommand.cs:12-36`
- Test: `tests/MyCollection.Tests/Unit/SyncJobMapperTests.cs`
- Test: `tests/MyCollection.Tests/Integration/MongoSyncJobRepositoryTests.cs`

「查無此遊戲」不是失敗。混進 `Failed` 會讓使用者以為出事，所以另設一格。

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/SyncJobMapperTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class SyncJobMapperTests
{
    [Fact]
    public void Maps_every_counter_including_skipped()
    {
        var startedAt = new DateTime(2026, 8, 1, 3, 0, 0, DateTimeKind.Utc);
        var job = new SyncJob
        {
            Id = ObjectId.Parse("000000000000000000000009"),
            Provider = ProviderKeys.Igdb,
            Status = SyncStatus.Succeeded,
            Created = 1,
            Updated = 2,
            Failed = 3,
            Skipped = 4,
            Error = null,
            StartedAt = startedAt,
            FinishedAt = startedAt.AddSeconds(5)
        };

        var dto = SyncJobMapper.ToDto(job);

        dto.Id.Should().Be("000000000000000000000009");
        dto.Provider.Should().Be("igdb");
        dto.Status.Should().Be("Succeeded");
        dto.Created.Should().Be(1);
        dto.Updated.Should().Be(2);
        dto.Failed.Should().Be(3);
        dto.Skipped.Should().Be(4);
        dto.FinishedAt.Should().Be(startedAt.AddSeconds(5));
    }

    [Fact]
    public void Skipped_defaults_to_zero_when_not_set()
    {
        var job = new SyncJob
        {
            Id = ObjectId.GenerateNewId(),
            Provider = ProviderKeys.Steam,
            StartedAt = DateTime.UtcNow
        };

        SyncJobMapper.ToDto(job).Skipped.Should().Be(0);
    }
}
```

> 這個測試只驗證 C# 欄位預設值，**不要**把它命名成「舊文件」之類暗示 BSON 行為的名字——
> 名字宣稱的範圍大於實際驗證的範圍，比沒有測試更糟。真正的向後相容由下面的整合測試負責。

`tests/MyCollection.Tests/Integration/MongoSyncJobRepositoryTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoSyncJobRepositoryTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();

    private MongoSyncJobRepository _sut = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(c => c.UserId).Returns(Owner);
        userContext.SetupGet(c => c.IsAuthenticated).Returns(true);

        _sut = new MongoSyncJobRepository(fixture.Context, userContext.Object);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// 直接寫入沒有 skipped 欄位的 raw BsonDocument，模擬 Skipped 加入前寫入的舊文件，
    /// 驗證反序列化時會自動補 0，而不是驗證 C# 物件的預設值（那不代表任何 BSON 行為）。
    /// </summary>
    [Fact]
    public async Task Legacy_document_without_skipped_element_deserializes_to_zero()
    {
        var id = ObjectId.GenerateNewId();
        var startedAt = new DateTime(2026, 8, 1, 3, 0, 0, DateTimeKind.Utc);
        var legacyDoc = new BsonDocument
        {
            ["_id"] = id,
            ["ownerId"] = Owner,
            ["provider"] = "steam",
            ["status"] = "Succeeded",
            ["created"] = 1,
            ["updated"] = 2,
            ["failed"] = 3,
            // 刻意省略 "skipped"，模擬欄位新增前就存在的文件
            ["error"] = BsonNull.Value,
            ["startedAt"] = startedAt,
            ["finishedAt"] = startedAt.AddSeconds(5)
        };

        var rawCollection = fixture.Context.SyncJobs.Database.GetCollection<BsonDocument>("syncJobs");
        await rawCollection.InsertOneAsync(legacyDoc);

        var result = await _sut.ListRecentAsync(10, CancellationToken.None);

        var job = result.Should().ContainSingle().Subject;
        job.Id.Should().Be(id);
        job.Skipped.Should().Be(0);
        job.Created.Should().Be(1);
        job.Updated.Should().Be(2);
        job.Failed.Should().Be(3);
    }
}
```

> 元素名稱與型別必須符合驅動程式真正會寫出的格式，否則整份文件反序列化失敗、每個欄位都是預設值，
> `Skipped == 0` 就會**空洞地通過**。`MongoConventions.cs` 註冊了 `CamelCaseElementNameConvention`
> 與 `EnumRepresentationConvention(BsonType.String)`，所以元素名是 camelCase、`status` 是字串。
> 同時斷言 `Created` / `Updated` / `Failed` 為非預設值，就是防這個空洞通過的護欄。

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter SyncJobMapperTests`
Expected: 編譯失敗，`SyncJob` 沒有 `Skipped`、`SyncJobDto` 沒有 `Skipped`。

- [ ] **Step 3: 實作**

`src/MyCollection.Domain/Entities/SyncJob.cs`，在 `public int Failed { get; set; }` 之後加入：

```csharp
    /// <summary>正常但未處理的品項數，例如外部來源查無對應。與 Failed 語意不同。</summary>
    public int Skipped { get; set; }
```

`src/MyCollection.Application/Ingestion/SyncCommand.cs`，`SyncJobDto` 與 `SyncJobMapper.ToDto` 改為：

```csharp
public record SyncJobDto(
    string Id,
    string Provider,
    string Status,
    int Created,
    int Updated,
    int Failed,
    int Skipped,
    string? Error,
    DateTime StartedAt,
    DateTime? FinishedAt);
```

```csharp
public static class SyncJobMapper
{
    public static SyncJobDto ToDto(SyncJob job) => new(
        job.Id.ToString(),
        job.Provider,
        job.Status.ToString(),
        job.Created,
        job.Updated,
        job.Failed,
        job.Skipped,
        job.Error,
        job.StartedAt,
        job.FinishedAt);
}
```

MongoDB 對舊文件缺少的 `skipped` 欄位會反序列化成 `0`，不需要遷移。

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter "SyncJobMapperTests|MongoSyncJobRepositoryTests|SyncCommandTests"`
Expected: `SyncJobMapperTests` `Passed: 2`、`MongoSyncJobRepositoryTests` `Passed: 1`，`SyncCommandTests` 維持全綠。

驗證整合測試不是空洞通過：暫時在 `legacyDoc` 加入 `["skipped"] = 99`，重跑應**失敗**
（`Expected job.Skipped to be 0, but found 99`），確認後還原。

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(ingestion): add Skipped counter to SyncJob"
```

---

### Task 3：IgdbOptions 與 IGDB 欄位定義

**Files:**
- Create: `src/MyCollection.Infrastructure/Providers/Igdb/IgdbOptions.cs`
- Create: `src/MyCollection.Infrastructure/Providers/Igdb/IgdbFields.cs`
- Test: `tests/MyCollection.Tests/Unit/IgdbOptionsTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/IgdbOptionsTests.cs`：

```csharp
using FluentAssertions;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Providers.Igdb;

namespace MyCollection.Tests.Unit;

public class IgdbOptionsTests
{
    [Theory]
    [InlineData("", "", false)]
    [InlineData("client-id", "", false)]
    [InlineData("", "client-secret", false)]
    [InlineData("   ", "   ", false)]
    [InlineData("client-id", "client-secret", true)]
    public void IsConfigured_requires_both_credentials(string clientId, string clientSecret, bool expected)
    {
        new IgdbOptions { ClientId = clientId, ClientSecret = clientSecret }
            .IsConfigured.Should().Be(expected);
    }

    [Fact]
    public void Fields_declare_the_marker_key_first()
    {
        IgdbFields.All[0].Key.Should().Be(IgdbFields.MarkerKey);
        IgdbFields.MarkerKey.Should().Be("igdbId");
    }

    [Fact]
    public void Fields_cover_every_attribute_the_mapper_writes()
    {
        IgdbFields.All.Select(f => f.Key).Should().BeEquivalentTo(
            "igdbId", "developer", "publisher", "releaseDate",
            "genres", "platforms", "igdbRating", "coverUrl");
    }

    [Fact]
    public void Fields_use_types_the_attribute_validator_accepts()
    {
        IgdbFields.All.Single(f => f.Key == "igdbId").Type.Should().Be(FieldType.Number);
        IgdbFields.All.Single(f => f.Key == "releaseDate").Type.Should().Be(FieldType.Date);
        IgdbFields.All.Single(f => f.Key == "coverUrl").Type.Should().Be(FieldType.Url);
        IgdbFields.All.Single(f => f.Key == "igdbRating").Type.Should().Be(FieldType.Number);
        IgdbFields.All.Single(f => f.Key == "genres").Type.Should().Be(FieldType.Text);
    }

    [Fact]
    public void No_field_is_required_because_igdb_data_is_patchy()
    {
        IgdbFields.All.Should().OnlyContain(f => !f.Required);
    }

    [Fact]
    public void Each_call_returns_independent_instances_so_callers_cannot_mutate_the_definition()
    {
        var first = IgdbFields.Create();
        first[0].Label = "tampered";

        IgdbFields.Create()[0].Label.Should().Be("IGDB ID");
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter IgdbOptionsTests`
Expected: 編譯失敗，找不到 `IgdbOptions` / `IgdbFields`。

- [ ] **Step 3: 實作 IgdbOptions**

`src/MyCollection.Infrastructure/Providers/Igdb/IgdbOptions.cs`：

```csharp
using MyCollection.Application.Ingestion;

namespace MyCollection.Infrastructure.Providers.Igdb;

public sealed class IgdbOptions
{
    public const string SectionName = "Igdb";

    /// <summary>與 <see cref="ProviderKeys.Igdb"/> 相同；放在這裡讓 Infrastructure 內部不必互相引用類別。</summary>
    public const string ProviderKey = ProviderKeys.Igdb;

    /// <summary>Twitch 應用程式的 Client ID。空值代表整個 IGDB 功能停用。</summary>
    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string TokenBaseAddress { get; init; } = "https://id.twitch.tv/";
    public string BaseAddress { get; init; } = "https://api.igdb.com/v4/";

    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>單次搜尋回傳上限。</summary>
    public int SearchLimit { get; init; } = 20;

    /// <summary>批次反查時一次帶幾個外部 id。</summary>
    public int LookupBatchSize { get; init; } = 10;

    /// <summary>
    /// 兩次 IGDB 請求之間的最小間隔。IGDB 限制 4 req/sec，
    /// 超標的懲罰是整段時間被擋，代價不對稱，所以自我節流而非撞到 429 才退避。
    /// </summary>
    public int MinRequestIntervalMs { get; init; } = 250;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
```

- [ ] **Step 4: 實作 IgdbFields**

`src/MyCollection.Infrastructure/Providers/Igdb/IgdbFields.cs`：

```csharp
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Providers.Igdb;

/// <summary>
/// IGDB 寫入的 attribute 欄位定義，唯一來源。
/// SystemCategoryDefinitions 與 IgdbProvider.RequiredFields 都由此取得，避免兩處各寫一份而漂移。
///
/// developer / publisher / releaseDate 三個 key 兩個系統遊戲品類本來就有，
/// 標籤沿用既有的（「發售日期」而非「發行日」），不另立同義欄位。
/// 沒有任何欄位設 Required：IGDB 資料缺漏很常見，設了會讓使用者之後每次更新都失敗。
/// </summary>
public static class IgdbFields
{
    public const string MarkerKey = "igdbId";

    /// <summary>唯讀快照，供只需要讀 Key/Type 的呼叫端。</summary>
    public static IReadOnlyList<CategoryField> All { get; } = Create();

    /// <summary>
    /// 回傳可安全交給呼叫端持有的新實例。CategoryField 是可變類別，
    /// 直接共用 All 會讓 SystemCategoryDefinitions 寫進資料庫的物件被別處改到。
    /// </summary>
    public static List<CategoryField> Create() =>
    [
        new() { Key = MarkerKey, Label = "IGDB ID", Type = FieldType.Number },
        new() { Key = "developer", Label = "開發商", Type = FieldType.Text, Searchable = true },
        new() { Key = "publisher", Label = "發行商", Type = FieldType.Text, Searchable = true },
        new() { Key = "releaseDate", Label = "發售日期", Type = FieldType.Date },
        new() { Key = "genres", Label = "類型", Type = FieldType.Text, Searchable = true },
        new() { Key = "platforms", Label = "發行平台", Type = FieldType.Text, Searchable = true },
        new() { Key = "igdbRating", Label = "IGDB 評分", Type = FieldType.Number },
        new() { Key = "coverUrl", Label = "IGDB 封面網址", Type = FieldType.Url }
    ];
}
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test --filter IgdbOptionsTests`
Expected: `Passed: 10`（5 個 `[Fact]` + `[Theory]` 展開 5 個）

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(igdb): add options and field definitions"
```

---

### Task 4：TwitchTokenProvider

**Files:**
- Create: `src/MyCollection.Infrastructure/Providers/Igdb/TwitchTokenProvider.cs`
- Create: `tests/MyCollection.Tests/Fixtures/twitch-token.json`
- Test: `tests/MyCollection.Tests/Unit/TwitchTokenProviderTests.cs`

必須是 **singleton**，否則快取毫無意義。因此不用 `AddHttpClient<T>`（那是 transient），改注入 `IHttpClientFactory`——與 `ShowcaseImageDownloader.HttpClientName` 同一套作法。

- [ ] **Step 1: 建立 fixture**

`tests/MyCollection.Tests/Fixtures/twitch-token.json`：

```json
{
  "access_token": "abcdefghijklmnopqrstuvwxyz1234",
  "expires_in": 5184000,
  "token_type": "bearer"
}
```

- [ ] **Step 2: 寫失敗測試**

`tests/MyCollection.Tests/Unit/TwitchTokenProviderTests.cs`：

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Providers.Igdb;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Unit;

public class TwitchTokenProviderTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 1, 3, 0, 0, TimeSpan.Zero));

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "twitch-token.json"));

    private TwitchTokenProvider CreateSut(StubHttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(TwitchTokenProvider.HttpClientName))
            .Returns(() => handler.CreateClient("https://id.twitch.tv/"));

        return new TwitchTokenProvider(
            factory.Object,
            Options.Create(new IgdbOptions { ClientId = "cid", ClientSecret = "csecret" }),
            _time);
    }

    [Fact]
    public async Task Fetches_the_token_on_the_first_call()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());

        var token = await CreateSut(handler).GetAsync(CancellationToken.None);

        token.Should().Be("abcdefghijklmnopqrstuvwxyz1234");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Sends_the_client_credentials_grant()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());

        await CreateSut(handler).GetAsync(CancellationToken.None);

        var query = handler.Requests.Single().Query;
        query.Should().Contain("client_id=cid");
        query.Should().Contain("client_secret=csecret");
        query.Should().Contain("grant_type=client_credentials");
    }

    [Fact]
    public async Task Reuses_the_cached_token_without_a_second_request()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());
        var sut = CreateSut(handler);

        await sut.GetAsync(CancellationToken.None);
        await sut.GetAsync(CancellationToken.None);

        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Renews_the_token_five_minutes_before_it_expires()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());
        var sut = CreateSut(handler);

        await sut.GetAsync(CancellationToken.None);

        // 60 天有效期，推進到剩 4 分 59 秒
        _time.Advance(TimeSpan.FromSeconds(5184000 - 299));
        await sut.GetAsync(CancellationToken.None);

        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Keeps_the_cached_token_while_more_than_five_minutes_remain()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());
        var sut = CreateSut(handler);

        await sut.GetAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(5184000 - 601));
        await sut.GetAsync(CancellationToken.None);

        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Invalidate_forces_the_next_call_to_refetch()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());
        var sut = CreateSut(handler);

        await sut.GetAsync(CancellationToken.None);
        sut.Invalidate();
        await sut.GetAsync(CancellationToken.None);

        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Concurrent_callers_trigger_only_one_token_request()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());
        var sut = CreateSut(handler);

        await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => sut.GetAsync(CancellationToken.None)));

        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Wraps_http_failures_in_ProviderException(HttpStatusCode status)
    {
        var sut = CreateSut(StubHttpMessageHandler.Status(status));

        var act = () => sut.GetAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<ProviderException>())
            .Which.ProviderKey.Should().Be("igdb");
    }

    [Fact]
    public async Task Wraps_malformed_json_in_ProviderException()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json("not json"));

        var act = () => sut.GetAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ProviderException>();
    }
}
```

- [ ] **Step 3: 跑測試確認失敗**

Run: `dotnet test --filter TwitchTokenProviderTests`
Expected: 編譯失敗，找不到 `TwitchTokenProvider`。

- [ ] **Step 4: 實作**

`src/MyCollection.Infrastructure/Providers/Igdb/TwitchTokenProvider.cs`：

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Providers.Igdb;

public interface ITwitchTokenProvider
{
    Task<string> GetAsync(CancellationToken ct);

    /// <summary>IGDB 回 401 時呼叫，強制下一次重新取得。</summary>
    void Invalidate();
}

/// <summary>
/// Twitch client credentials 的 app access token（約 60 天）。
/// 存記憶體即可：重啟成本是一次額外請求，換來零狀態管理。
/// 必須註冊為 singleton，否則每次解析都是新的空快取。
/// </summary>
public sealed class TwitchTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<IgdbOptions> options,
    TimeProvider timeProvider) : ITwitchTokenProvider, IDisposable
{
    public const string HttpClientName = "twitch-oauth";

    /// <summary>提前這麼久換新，避免請求還在路上時 token 剛好到期。</summary>
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetAsync(CancellationToken ct)
    {
        if (IsFresh())
        {
            return _token!;
        }

        await _gate.WaitAsync(ct);
        try
        {
            // 等鎖期間可能已有人換好了，再確認一次才不會打出多餘請求
            if (IsFresh())
            {
                return _token!;
            }

            var response = await FetchAsync(ct);

            _token = response.AccessToken;
            _expiresAt = timeProvider.GetUtcNow().AddSeconds(response.ExpiresIn);

            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        _token = null;
        _expiresAt = default;
    }

    public void Dispose() => _gate.Dispose();

    private bool IsFresh() =>
        _token is not null && timeProvider.GetUtcNow() + RenewalMargin < _expiresAt;

    private async Task<TokenResponse> FetchAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var requestUri =
            $"oauth2/token?client_id={Uri.EscapeDataString(settings.ClientId)}" +
            $"&client_secret={Uri.EscapeDataString(settings.ClientSecret)}" +
            "&grant_type=client_credentials";

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            var response = await client.PostAsync(requestUri, content: null, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException(
                    IgdbOptions.ProviderKey,
                    $"Twitch returned HTTP {(int)response.StatusCode} for the token request.");
            }

            return await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                   ?? throw new ProviderException(
                       IgdbOptions.ProviderKey, "Twitch returned an empty token response.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new ProviderException(
                IgdbOptions.ProviderKey, $"Twitch token request failed: {ex.Message}", ex);
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] long ExpiresIn);
}
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test --filter TwitchTokenProviderTests`
Expected: `Passed: 11`（8 個 `[Fact]` + `[Theory]` 展開 3 個）

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(igdb): add Twitch client-credentials token provider"
```

---

### Task 5：IgdbRateLimiter

**Files:**
- Create: `src/MyCollection.Infrastructure/Providers/Igdb/IgdbRateLimiter.cs`
- Test: `tests/MyCollection.Tests/Unit/IgdbRateLimiterTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/IgdbRateLimiterTests.cs`：

```csharp
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MyCollection.Infrastructure.Providers.Igdb;

namespace MyCollection.Tests.Unit;

public class IgdbRateLimiterTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 1, 3, 0, 0, TimeSpan.Zero));

    private IgdbRateLimiter CreateSut(int intervalMs = 250) =>
        new(Options.Create(new IgdbOptions { MinRequestIntervalMs = intervalMs }), _time);

    [Fact]
    public async Task First_call_passes_immediately()
    {
        using var sut = CreateSut();

        var wait = sut.WaitAsync(CancellationToken.None);

        wait.IsCompleted.Should().BeTrue();
        await wait;
    }

    [Fact]
    public async Task Second_call_blocks_until_the_interval_has_elapsed()
    {
        using var sut = CreateSut();
        await sut.WaitAsync(CancellationToken.None);

        var second = sut.WaitAsync(CancellationToken.None);
        second.IsCompleted.Should().BeFalse("未達最小間隔前不應放行");

        _time.Advance(TimeSpan.FromMilliseconds(250));

        await second;
        second.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task Second_call_passes_immediately_when_enough_time_already_passed()
    {
        using var sut = CreateSut();
        await sut.WaitAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(1));

        var second = sut.WaitAsync(CancellationToken.None);
        second.IsCompleted.Should().BeTrue();
        await second;
    }

    [Fact]
    public async Task A_zero_interval_disables_throttling()
    {
        using var sut = CreateSut(intervalMs: 0);

        await sut.WaitAsync(CancellationToken.None);
        var second = sut.WaitAsync(CancellationToken.None);

        second.IsCompleted.Should().BeTrue();
        await second;
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter IgdbRateLimiterTests`
Expected: 編譯失敗，找不到 `IgdbRateLimiter`。

- [ ] **Step 3: 實作**

`src/MyCollection.Infrastructure/Providers/Igdb/IgdbRateLimiter.cs`：

```csharp
using Microsoft.Extensions.Options;

namespace MyCollection.Infrastructure.Providers.Igdb;

/// <summary>
/// IGDB 限制 4 req/sec。撞上去的懲罰是整段時間被擋，代價不對稱，
/// 所以在程序層級自我節流，而不是等到 429 才退避。
/// 註冊為 singleton——每個請求各自一份節流器等於沒有節流。
/// </summary>
public sealed class IgdbRateLimiter(
    IOptions<IgdbOptions> options,
    TimeProvider timeProvider) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DateTimeOffset _nextAllowedAt = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(options.Value.MinRequestIntervalMs);

        await _gate.WaitAsync(ct);
        try
        {
            var now = timeProvider.GetUtcNow();
            var remaining = _nextAllowedAt - now;

            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, timeProvider, ct);
                now = timeProvider.GetUtcNow();
            }

            _nextAllowedAt = now + interval;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter IgdbRateLimiterTests`
Expected: `Passed: 4`

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(igdb): add process-wide request rate limiter"
```

---

### Task 6：IgdbMapper

**Files:**
- Create: `src/MyCollection.Infrastructure/Providers/Igdb/IgdbMapper.cs`
- Create: `tests/MyCollection.Tests/Fixtures/igdb-search-witcher.json`
- Test: `tests/MyCollection.Tests/Unit/IgdbMapperTests.cs`

- [ ] **Step 1: 建立 fixture**

`tests/MyCollection.Tests/Fixtures/igdb-search-witcher.json`。第二筆刻意缺 `summary`、`total_rating`、`genres`、`involved_companies`，用來驗證缺席欄位一律省略 key 而非寫 null：

```json
[
  {
    "id": 1942,
    "name": "The Witcher 3: Wild Hunt",
    "summary": "A story-driven, open world adventure set in a visually stunning fantasy universe.",
    "url": "https://www.igdb.com/games/the-witcher-3-wild-hunt",
    "first_release_date": 1431993600,
    "total_rating": 93.47,
    "cover": { "id": 89386, "image_id": "co1wyy" },
    "genres": [
      { "id": 12, "name": "Role-playing (RPG)" },
      { "id": 31, "name": "Adventure" }
    ],
    "platforms": [
      { "id": 6, "abbreviation": "PC" },
      { "id": 48, "abbreviation": "PS4" }
    ],
    "involved_companies": [
      { "id": 1, "company": { "id": 908, "name": "CD Projekt RED" }, "developer": true, "publisher": false },
      { "id": 2, "company": { "id": 909, "name": "CD Projekt" }, "developer": false, "publisher": true }
    ]
  },
  {
    "id": 11156,
    "name": "The Witcher 3: Wild Hunt - Hearts of Stone",
    "url": "https://www.igdb.com/games/the-witcher-3-wild-hunt-hearts-of-stone",
    "first_release_date": 1444694400,
    "platforms": [
      { "id": 6, "abbreviation": "PC" }
    ]
  }
]
```

- [ ] **Step 2: 寫失敗測試**

`tests/MyCollection.Tests/Unit/IgdbMapperTests.cs`：

```csharp
using System.Text.Json;
using FluentAssertions;
using MyCollection.Infrastructure.Providers.Igdb;

namespace MyCollection.Tests.Unit;

public class IgdbMapperTests
{
    private static JsonElement Games() =>
        JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "igdb-search-witcher.json"))).RootElement;

    private static JsonElement Witcher3() => Games()[0];

    private static JsonElement HeartsOfStone() => Games()[1];

    [Fact]
    public void Maps_the_identity_fields()
    {
        var item = IgdbMapper.ToExternalItem(Witcher3());

        item.ExternalId.Should().Be("1942");
        item.Name.Should().Be("The Witcher 3: Wild Hunt");
        item.Description.Should().StartWith("A story-driven, open world adventure");
        item.SourceUrl!.ToString().Should().Be("https://www.igdb.com/games/the-witcher-3-wild-hunt");
    }

    [Fact]
    public void Builds_the_cover_url_from_the_image_id()
    {
        var item = IgdbMapper.ToExternalItem(Witcher3());

        const string expected = "https://images.igdb.com/igdb/image/upload/t_cover_big/co1wyy.jpg";
        item.ImageUrl!.ToString().Should().Be(expected);
        item.Attributes["coverUrl"].Should().Be(expected);
    }

    [Fact]
    public void Maps_the_marker_id_as_a_number()
    {
        IgdbMapper.ToExternalItem(Witcher3()).Attributes[IgdbFields.MarkerKey].Should().Be(1942L);
    }

    [Fact]
    public void Converts_the_unix_release_date_to_utc()
    {
        IgdbMapper.ToExternalItem(Witcher3()).Attributes["releaseDate"]
            .Should().Be(new DateTime(2015, 5, 19, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Picks_the_developer_and_publisher_from_involved_companies()
    {
        var attributes = IgdbMapper.ToExternalItem(Witcher3()).Attributes;

        attributes["developer"].Should().Be("CD Projekt RED");
        attributes["publisher"].Should().Be("CD Projekt");
    }

    [Fact]
    public void Joins_genres_and_platforms_with_commas()
    {
        var attributes = IgdbMapper.ToExternalItem(Witcher3()).Attributes;

        attributes["genres"].Should().Be("Role-playing (RPG), Adventure");
        attributes["platforms"].Should().Be("PC, PS4");
    }

    [Fact]
    public void Rounds_the_rating_to_one_decimal()
    {
        IgdbMapper.ToExternalItem(Witcher3()).Attributes["igdbRating"].Should().Be(93.5d);
    }

    [Fact]
    public void Omits_absent_attributes_instead_of_writing_nulls()
    {
        var item = IgdbMapper.ToExternalItem(HeartsOfStone());

        item.Description.Should().BeNull();
        item.ImageUrl.Should().BeNull();
        item.Attributes.Should().NotContainKeys("summary", "igdbRating", "genres", "developer", "publisher", "coverUrl");
        item.Attributes.Should().ContainKey("platforms");
    }

    [Fact]
    public void Never_writes_a_key_outside_the_declared_field_set()
    {
        var declared = IgdbFields.All.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var game in Games().EnumerateArray())
        {
            IgdbMapper.ToExternalItem(game).Attributes.Keys.Should().BeSubsetOf(declared);
        }
    }
}
```

- [ ] **Step 3: 跑測試確認失敗**

Run: `dotnet test --filter IgdbMapperTests`
Expected: 編譯失敗，找不到 `IgdbMapper`。

- [ ] **Step 4: 實作**

`src/MyCollection.Infrastructure/Providers/Igdb/IgdbMapper.cs`：

```csharp
using System.Globalization;
using System.Text.Json;
using MyCollection.Application.Ingestion;

namespace MyCollection.Infrastructure.Providers.Igdb;

/// <summary>
/// IGDB JSON → ExternalItem。
/// 缺席的欄位一律省略 key，不寫 null：AttributeValidator 把 null 視同未提供，
/// 但寫進 Mongo 的 null 會在文件裡留下噪音，且無法與「使用者清空」區分。
/// </summary>
public static class IgdbMapper
{
    private const string CoverUrlTemplate = "https://images.igdb.com/igdb/image/upload/t_cover_big/{0}.jpg";

    public static ExternalItem ToExternalItem(JsonElement game)
    {
        var id = game.GetProperty("id").GetInt64();
        var coverUrl = CoverUrl(game);

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [IgdbFields.MarkerKey] = id
        };

        Add(attributes, "developer", Company(game, wantDeveloper: true));
        Add(attributes, "publisher", Company(game, wantDeveloper: false));
        Add(attributes, "genres", Join(game, "genres", "name"));
        Add(attributes, "platforms", Join(game, "platforms", "abbreviation"));
        Add(attributes, "coverUrl", coverUrl?.ToString());

        if (Property(game, "first_release_date") is { } released)
        {
            attributes["releaseDate"] = DateTimeOffset.FromUnixTimeSeconds(released.GetInt64()).UtcDateTime;
        }

        if (Property(game, "total_rating") is { } rating)
        {
            attributes["igdbRating"] = Math.Round(rating.GetDouble(), 1);
        }

        return new ExternalItem(
            ExternalId: id.ToString(CultureInfo.InvariantCulture),
            Name: game.GetProperty("name").GetString()!,
            Description: Text(game, "summary"),
            ImageUrl: coverUrl,
            Attributes: attributes)
        {
            SourceUrl = Text(game, "url") is { } url ? new Uri(url) : null
        };
    }

    private static void Add(Dictionary<string, object?> attributes, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            attributes[key] = value;
        }
    }

    /// <summary>缺席與 JSON null 都當作沒有。</summary>
    private static JsonElement? Property(JsonElement element, string name) =>
        element.ValueKind is JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind is not JsonValueKind.Null
            ? value
            : null;

    private static string? Text(JsonElement element, string name) =>
        Property(element, name)?.GetString();

    private static Uri? CoverUrl(JsonElement game)
    {
        if (Property(game, "cover") is not { } cover || Text(cover, "image_id") is not { } imageId)
        {
            return null;
        }

        return new Uri(string.Format(CultureInfo.InvariantCulture, CoverUrlTemplate, imageId));
    }

    private static string? Join(JsonElement game, string arrayName, string property)
    {
        if (Property(game, arrayName) is not { ValueKind: JsonValueKind.Array } array)
        {
            return null;
        }

        var values = array.EnumerateArray()
            .Select(element => Text(element, property))
            .Where(value => !string.IsNullOrWhiteSpace(value));

        var joined = string.Join(", ", values);

        return joined.Length == 0 ? null : joined;
    }

    private static string? Company(JsonElement game, bool wantDeveloper)
    {
        if (Property(game, "involved_companies") is not { ValueKind: JsonValueKind.Array } array)
        {
            return null;
        }

        var role = wantDeveloper ? "developer" : "publisher";

        return array.EnumerateArray()
            .Where(entry => Property(entry, role)?.GetBoolean() == true)
            .Select(entry => Property(entry, "company") is { } company ? Text(company, "name") : null)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
    }
}
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test --filter IgdbMapperTests`
Expected: `Passed: 9`

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(igdb): map IGDB game payloads to ExternalItem"
```

---

### Task 7：IgdbProvider

**Files:**
- Create: `src/MyCollection.Infrastructure/Providers/Igdb/IgdbProvider.cs`
- Create: `tests/MyCollection.Tests/Fixtures/igdb-external-steam.json`
- Test: `tests/MyCollection.Tests/Unit/IgdbProviderTests.cs`

> **實作前必做：** Steam appid → IGDB id 的查法是整份設計唯一無法從文件確定的部分（spec §7）。
> 先用真實憑證各打一次以下兩種查詢，確認哪一種可用，再據以填 `SteamLookupQuery`，並把真實回應錄成 fixture：
>
> ```bash
> # 候選 A：external_games 端點（穩定多年，但 category 欄位傳出正被 game_type 取代）
> curl -X POST 'https://api.igdb.com/v4/external_games' \
>   -H "Client-ID: $IGDB_CLIENT_ID" -H "Authorization: Bearer $TOKEN" \
>   -d 'fields game,uid; where category = 1 & uid = ("440","620"); limit 500;'
>
> # 候選 B：games 端點的 external 欄位
> curl -X POST 'https://api.igdb.com/v4/games' \
>   -H "Client-ID: $IGDB_CLIENT_ID" -H "Authorization: Bearer $TOKEN" \
>   -d 'fields name,external.steam; where external.steam = ("440","620"); limit 500;'
> ```
>
> 下方以候選 A 撰寫。若實測為候選 B 可用，只需改 `SteamLookupQuery` 與 `ResolveSteamAsync` 兩處，
> 以及 fixture 內容；其餘測試與程式碼不受影響。

- [ ] **Step 1: 建立 fixture**

`tests/MyCollection.Tests/Fixtures/igdb-external-steam.json`（`external_games` 的回應，只有 440 有對應，620 刻意查無）：

```json
[
  { "id": 5001, "game": 1942, "uid": "440" }
]
```

`tests/MyCollection.Tests/Fixtures/igdb-game-1942.json`（反查第二段 `games` 查詢的回應，單筆）：

```json
[
  {
    "id": 1942,
    "name": "The Witcher 3: Wild Hunt",
    "summary": "A story-driven, open world adventure set in a visually stunning fantasy universe.",
    "url": "https://www.igdb.com/games/the-witcher-3-wild-hunt",
    "first_release_date": 1431993600,
    "total_rating": 93.47,
    "cover": { "id": 89386, "image_id": "co1wyy" },
    "genres": [
      { "id": 12, "name": "Role-playing (RPG)" },
      { "id": 31, "name": "Adventure" }
    ],
    "platforms": [
      { "id": 6, "abbreviation": "PC" },
      { "id": 48, "abbreviation": "PS4" }
    ],
    "involved_companies": [
      { "id": 1, "company": { "id": 908, "name": "CD Projekt RED" }, "developer": true, "publisher": false },
      { "id": 2, "company": { "id": 909, "name": "CD Projekt" }, "developer": false, "publisher": true }
    ]
  }
]
```

- [ ] **Step 2: 寫失敗測試**

`tests/MyCollection.Tests/Unit/IgdbProviderTests.cs`：

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Providers.Igdb;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Unit;

public class IgdbProviderTests
{
    private readonly Mock<ITwitchTokenProvider> _token = new();

    public IgdbProviderTests() =>
        _token.Setup(t => t.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync("token-1");

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static IgdbOptions Options() => new()
    {
        ClientId = "cid",
        ClientSecret = "csecret",
        MinRequestIntervalMs = 0,
        LookupBatchSize = 10
    };

    private IgdbProvider CreateSut(StubHttpMessageHandler handler)
    {
        var options = Microsoft.Extensions.Options.Options.Create(Options());

        return new IgdbProvider(
            handler.CreateClient("https://api.igdb.com/v4/"),
            _token.Object,
            new IgdbRateLimiter(options, new FakeTimeProvider()),
            options,
            NullLogger<IgdbProvider>.Instance);
    }

    /// <summary>依請求路徑回不同 fixture：反查先打 external_games 取得 game id，再打 games 取詳情。</summary>
    private static StubHttpMessageHandler LookupHandler() =>
        new(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                request.RequestUri!.AbsolutePath.EndsWith("external_games", StringComparison.Ordinal)
                    ? Fixture("igdb-external-steam.json")
                    : Fixture("igdb-game-1942.json"),
                System.Text.Encoding.UTF8,
                "application/json")
        });

    [Fact]
    public void Declares_search_capability_only()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json("[]"));

        sut.Key.Should().Be("igdb");
        ProviderCapabilities.Of(sut).Should().Be(ProviderCapability.Search);
        sut.MarkerAttributeKey.Should().Be("igdbId");
        sut.RequiredFields.Select(f => f.Key).Should().Contain("igdbId");
    }

    [Fact]
    public async Task Search_maps_every_result()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json(Fixture("igdb-search-witcher.json")));

        var items = await sut.SearchAsync("witcher 3", 20, CancellationToken.None);

        items.Should().HaveCount(2);
        items[0].ExternalId.Should().Be("1942");
        items[0].Name.Should().Be("The Witcher 3: Wild Hunt");
    }

    [Fact]
    public async Task Search_sends_the_credentials_as_headers_and_the_query_as_the_body()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
        });
        var sut = CreateSut(handler);

        await sut.SearchAsync("witcher 3", 5, CancellationToken.None);

        handler.Requests.Single().AbsolutePath.Should().EndWith("/games");
        handler.LastRequestBody.Should().Contain("search \"witcher 3\";");
        handler.LastRequestBody.Should().Contain("limit 5;");
        handler.LastRequestHeaders!.GetValues("Client-ID").Should().ContainSingle("cid");
        handler.LastRequestHeaders.GetValues("Authorization").Should().ContainSingle("Bearer token-1");
    }

    [Theory]
    [InlineData("wit\"cher; where id = 1", "witcher where id = 1")]
    [InlineData("witcher\n3", "witcher 3")]
    public async Task Search_strips_apicalypse_control_characters_from_user_input(string input, string expected)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
        });

        await CreateSut(handler).SearchAsync(input, 5, CancellationToken.None);

        handler.LastRequestBody.Should().Contain($"search \"{expected}\";");
    }

    [Fact]
    public async Task Search_returns_empty_when_igdb_has_no_match()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json("[]"));

        (await sut.SearchAsync("zzzz", 20, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Lookup_resolves_a_steam_appid_through_external_games()
    {
        var sut = CreateSut(LookupHandler());

        var result = await sut.FetchByExternalIdsAsync(["steam:440"], CancellationToken.None);

        result.Found.Should().ContainKey("steam:440");
        result.Found["steam:440"].ExternalId.Should().Be("1942");
        result.FailedIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Lookup_omits_ids_igdb_has_no_match_for_without_marking_them_failed()
    {
        var sut = CreateSut(LookupHandler());

        var result = await sut.FetchByExternalIdsAsync(["steam:440", "steam:620"], CancellationToken.None);

        result.Found.Keys.Should().BeEquivalentTo("steam:440");
        result.FailedIds.Should().BeEmpty("查無對應不是失敗");
    }

    [Fact]
    public async Task Lookup_of_an_igdb_id_skips_the_external_games_round_trip()
    {
        var handler = StubHttpMessageHandler.Json(Fixture("igdb-search-witcher.json"));
        var sut = CreateSut(handler);

        var result = await sut.FetchByExternalIdsAsync(["igdb:1942"], CancellationToken.None);

        result.Found.Should().ContainKey("igdb:1942");
        handler.Requests.Should().ContainSingle(uri => uri.AbsolutePath.EndsWith("/games"));
    }

    [Fact]
    public async Task Lookup_marks_an_unknown_prefix_as_failed()
    {
        var sut = CreateSut(LookupHandler());

        var result = await sut.FetchByExternalIdsAsync(["psn:CUSA123"], CancellationToken.None);

        result.Found.Should().BeEmpty();
        result.FailedIds.Should().BeEquivalentTo("psn:CUSA123");
    }

    [Fact]
    public async Task Lookup_records_request_failures_as_failed_ids_rather_than_throwing()
    {
        var sut = CreateSut(StubHttpMessageHandler.Status(HttpStatusCode.InternalServerError));

        var result = await sut.FetchByExternalIdsAsync(["steam:440", "steam:620"], CancellationToken.None);

        result.Found.Should().BeEmpty();
        result.FailedIds.Should().BeEquivalentTo("steam:440", "steam:620");
    }

    [Fact]
    public async Task Retries_once_after_a_401_with_a_refreshed_token()
    {
        var responses = new Queue<HttpStatusCode>([HttpStatusCode.Unauthorized, HttpStatusCode.OK]);
        var handler = new StubHttpMessageHandler(_ =>
        {
            var status = responses.Dequeue();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(
                    status is HttpStatusCode.OK ? Fixture("igdb-search-witcher.json") : "",
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        var items = await CreateSut(handler).SearchAsync("witcher 3", 20, CancellationToken.None);

        items.Should().HaveCount(2);
        handler.Requests.Should().HaveCount(2);
        _token.Verify(t => t.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task Gives_up_after_a_second_401()
    {
        var sut = CreateSut(StubHttpMessageHandler.Status(HttpStatusCode.Unauthorized));

        var act = () => sut.SearchAsync("witcher 3", 20, CancellationToken.None);

        (await act.Should().ThrowAsync<ProviderException>()).Which.ProviderKey.Should().Be("igdb");
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Search_wraps_http_failures_in_ProviderException(HttpStatusCode status)
    {
        var sut = CreateSut(StubHttpMessageHandler.Status(status));

        var act = () => sut.SearchAsync("witcher 3", 20, CancellationToken.None);

        (await act.Should().ThrowAsync<ProviderException>()).Which.ProviderKey.Should().Be("igdb");
    }

    [Fact]
    public async Task Search_wraps_malformed_json_in_ProviderException()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json("not json"));

        var act = () => sut.SearchAsync("witcher 3", 20, CancellationToken.None);

        await act.Should().ThrowAsync<ProviderException>();
    }
}
```

- [ ] **Step 3: 擴充 StubHttpMessageHandler 以記錄 body 與 headers**

`tests/MyCollection.Tests/Fixtures/StubHttpMessageHandler.cs`，在 `Requests` 屬性之後加入兩個屬性，並改寫 `SendAsync`：

```csharp
    /// <summary>最後一次請求的 body。IGDB 用 POST + APIcalypse 純文字查詢，斷言查詢內容需要它。</summary>
    public string? LastRequestBody { get; private set; }

    public System.Net.Http.Headers.HttpRequestHeaders? LastRequestHeaders { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        LastRequestHeaders = request.Headers;
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        return responder(request);
    }
```

（原本的同步 `SendAsync` 整個取代掉。）

- [ ] **Step 4: 跑測試確認失敗**

Run: `dotnet test --filter IgdbProviderTests`
Expected: 編譯失敗，找不到 `IgdbProvider`。

- [ ] **Step 5: 實作**

`src/MyCollection.Infrastructure/Providers/Igdb/IgdbProvider.cs`：

```csharp
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Providers.Igdb;

/// <summary>
/// IGDB 只提供公開遊戲資料，不綁使用者身分，因此憑證是全站共用的環境變數，
/// 不走 ExternalAccount 那套每人一把的加密儲存。
/// </summary>
public sealed class IgdbProvider(
    HttpClient httpClient,
    ITwitchTokenProvider tokenProvider,
    IgdbRateLimiter rateLimiter,
    IOptions<IgdbOptions> options,
    ILogger<IgdbProvider> logger) : ISearchProvider
{
    public const string ProviderKey = IgdbOptions.ProviderKey;

    private const string SteamPrefix = "steam:";
    private const string IgdbPrefix = "igdb:";

    /// <summary>Steam 在 external_games.category 的代碼。</summary>
    private const int SteamExternalCategory = 1;

    private const string GameFields =
        "fields name,summary,url,first_release_date,total_rating,cover.image_id," +
        "genres.name,platforms.abbreviation,involved_companies.company.name," +
        "involved_companies.developer,involved_companies.publisher;";

    public string Key => ProviderKey;

    public string MarkerAttributeKey => IgdbFields.MarkerKey;

    public IReadOnlyList<CategoryField> RequiredFields { get; } = IgdbFields.All;

    public async Task<IReadOnlyList<ExternalItem>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var effectiveLimit = Math.Clamp(limit, 1, options.Value.SearchLimit);

        // version_parent 非 null 的是地區重製／再版，搜尋結果會被同名項目灌爆
        var body =
            $"search \"{Sanitize(query)}\";\n" +
            $"{GameFields}\n" +
            "where version_parent = null;\n" +
            $"limit {effectiveLimit.ToString(CultureInfo.InvariantCulture)};";

        var games = await QueryAsync("games", body, ct);

        return games.EnumerateArray().Select(IgdbMapper.ToExternalItem).ToArray();
    }

    public async Task<ExternalLookupResult> FetchByExternalIdsAsync(
        IReadOnlyList<string> externalIds, CancellationToken ct)
    {
        var found = new Dictionary<string, ExternalItem>(StringComparer.Ordinal);
        var failed = new List<string>();

        var recognised = new List<string>();

        foreach (var externalId in externalIds.Distinct(StringComparer.Ordinal))
        {
            if (externalId.StartsWith(SteamPrefix, StringComparison.Ordinal)
                || externalId.StartsWith(IgdbPrefix, StringComparison.Ordinal))
            {
                recognised.Add(externalId);
            }
            else
            {
                logger.LogWarning("Unsupported external id prefix: {ExternalId}", externalId);
                failed.Add(externalId);
            }
        }

        foreach (var chunk in recognised.Chunk(Math.Max(1, options.Value.LookupBatchSize)))
        {
            try
            {
                await ResolveChunkAsync(chunk, found, ct);
            }
            catch (ProviderException ex)
            {
                // 逐批容錯：一批失敗不該讓其餘 40 筆一起陪葬
                logger.LogWarning(ex, "IGDB lookup failed for a chunk of {Count} ids.", chunk.Length);
                failed.AddRange(chunk);
            }
        }

        return new ExternalLookupResult(found, failed);
    }

    private async Task ResolveChunkAsync(
        string[] chunk, Dictionary<string, ExternalItem> found, CancellationToken ct)
    {
        // igdbId → 原始 externalId。同一個 IGDB 遊戲可能同時被 steam: 與 igdb: 指到
        var byGameId = new Dictionary<long, List<string>>();

        var steamIds = chunk
            .Where(id => id.StartsWith(SteamPrefix, StringComparison.Ordinal))
            .ToArray();

        foreach (var externalId in chunk.Where(id => id.StartsWith(IgdbPrefix, StringComparison.Ordinal)))
        {
            if (long.TryParse(externalId[IgdbPrefix.Length..], CultureInfo.InvariantCulture, out var gameId))
            {
                Track(byGameId, gameId, externalId);
            }
        }

        if (steamIds.Length > 0)
        {
            foreach (var (gameId, externalId) in await ResolveSteamAsync(steamIds, ct))
            {
                Track(byGameId, gameId, externalId);
            }
        }

        if (byGameId.Count == 0)
        {
            return;
        }

        var idList = string.Join(",", byGameId.Keys.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        var games = await QueryAsync(
            "games",
            $"{GameFields}\nwhere id = ({idList});\nlimit 500;",
            ct);

        foreach (var game in games.EnumerateArray())
        {
            var item = IgdbMapper.ToExternalItem(game);

            if (!long.TryParse(item.ExternalId, CultureInfo.InvariantCulture, out var gameId)
                || !byGameId.TryGetValue(gameId, out var externalIds))
            {
                continue;
            }

            foreach (var externalId in externalIds)
            {
                found[externalId] = item;
            }
        }
    }

    /// <summary>
    /// Steam appid → IGDB game id。
    /// 這是整個整合裡唯一無法從 IGDB 文件確定的查法（見設計文件 §7），
    /// 刻意隔離在這一個方法內：查法改變時其餘程式碼不受影響。
    /// </summary>
    private async Task<IReadOnlyList<(long GameId, string ExternalId)>> ResolveSteamAsync(
        string[] steamExternalIds, CancellationToken ct)
    {
        var uidToExternalId = steamExternalIds.ToDictionary(
            id => id[SteamPrefix.Length..], id => id, StringComparer.Ordinal);

        var uidList = string.Join(",", uidToExternalId.Keys.Select(uid => $"\"{uid}\""));

        var rows = await QueryAsync(
            "external_games",
            $"fields game,uid;\nwhere category = {SteamExternalCategory} & uid = ({uidList});\nlimit 500;",
            ct);

        var resolved = new List<(long, string)>();

        foreach (var row in rows.EnumerateArray())
        {
            if (row.TryGetProperty("uid", out var uid)
                && uid.GetString() is { } uidValue
                && uidToExternalId.TryGetValue(uidValue, out var externalId)
                && row.TryGetProperty("game", out var game))
            {
                resolved.Add((game.GetInt64(), externalId));
            }
        }

        return resolved;
    }

    private static void Track(Dictionary<long, List<string>> byGameId, long gameId, string externalId)
    {
        if (!byGameId.TryGetValue(gameId, out var list))
        {
            byGameId[gameId] = list = [];
        }

        list.Add(externalId);
    }

    /// <summary>
    /// 送出 APIcalypse 查詢。401 時換一次 token 重試——Twitch 可能提前撤銷 token，
    /// 這是靠到期時間算不出來的。只重試一次，憑證真的錯誤時才不會無限迴圈。
    /// </summary>
    private async Task<JsonElement> QueryAsync(string endpoint, string body, CancellationToken ct)
    {
        var response = await SendAsync(endpoint, body, ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            tokenProvider.Invalidate();
            response = await SendAsync(endpoint, body, ct);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException(
                    ProviderKey, $"IGDB returned HTTP {(int)response.StatusCode} for {endpoint}.");
            }

            try
            {
                var payload = await response.Content.ReadAsStringAsync(ct);

                return JsonDocument.Parse(payload).RootElement.Clone();
            }
            catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
            {
                throw new ProviderException(ProviderKey, $"IGDB {endpoint} response was unreadable: {ex.Message}", ex);
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string endpoint, string body, CancellationToken ct)
    {
        await rateLimiter.WaitAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        request.Headers.Add("Client-ID", options.Value.ClientId);
        request.Headers.Add("Authorization", $"Bearer {await tokenProvider.GetAsync(ct)}");

        try
        {
            return await httpClient.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ProviderException(ProviderKey, $"IGDB request to {endpoint} failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// APIcalypse 的字串以雙引號界定、以分號斷句，使用者輸入必須把兩者拿掉，
    /// 否則可以改寫整段查詢。換行同理。
    /// </summary>
    private static string Sanitize(string query) =>
        new string(query.Where(c => c is not ('"' or ';' or '\n' or '\r')).ToArray()).Trim();
}
```

- [ ] **Step 6: 跑測試確認通過**

Run: `dotnet test --filter IgdbProviderTests`
Expected: `Passed: 17`（12 個 `[Fact]` + 兩個 `[Theory]` 展開 2 與 3 個）

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "feat(igdb): add search provider with steam id resolution"
```

---

### Task 8：系統品類加入 IGDB 欄位

**Files:**
- Modify: `src/MyCollection.Infrastructure/Mongo/SystemCategoryDefinitions.cs:15-38`
- Test: `tests/MyCollection.Tests/Unit/SystemCategoryDefinitionsTests.cs`

`SystemCategorySeeder` 每次啟動以 `$set` 覆寫整份 `Fields`，所以既有部署重啟即自動補齊，不需要遷移腳本。

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/SystemCategoryDefinitionsTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Infrastructure.Providers.Igdb;

namespace MyCollection.Tests.Unit;

public class SystemCategoryDefinitionsTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 3, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<string> KeysOf(ObjectId categoryId) =>
        SystemCategoryDefinitions.Create(Now)
            .Single(c => c.Id == categoryId)
            .Fields.Select(f => f.Key)
            .ToArray();

    [Theory]
    [MemberData(nameof(GameCategoryIds))]
    public void Game_categories_declare_every_igdb_field(ObjectId categoryId)
    {
        KeysOf(categoryId).Should().Contain(IgdbFields.All.Select(f => f.Key));
    }

    [Theory]
    [MemberData(nameof(GameCategoryIds))]
    public void Game_categories_do_not_declare_a_key_twice(ObjectId categoryId)
    {
        KeysOf(categoryId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Reuses_the_existing_labels_for_keys_the_categories_already_had()
    {
        var digital = SystemCategoryDefinitions.Create(Now)
            .Single(c => c.Id == SystemCategoryDefinitions.DigitalGameId);

        digital.Fields.Single(f => f.Key == "releaseDate").Label.Should().Be("發售日期");
        digital.Fields.Single(f => f.Key == "platform").Label.Should().Be("平台／商店");
    }

    [Fact]
    public void Keeps_igdb_platforms_separate_from_the_owned_platform_field()
    {
        var keys = KeysOf(SystemCategoryDefinitions.PhysicalGameId);

        keys.Should().Contain("platform", "使用者這一份收藏在哪個平台");
        keys.Should().Contain("platforms", "IGDB 說這款遊戲發行於哪些平台");
    }

    [Fact]
    public void Non_game_categories_are_untouched_by_igdb()
    {
        KeysOf(SystemCategoryDefinitions.MusicAlbumId).Should().NotContain(IgdbFields.MarkerKey);
        KeysOf(SystemCategoryDefinitions.MovieDiscId).Should().NotContain(IgdbFields.MarkerKey);
    }

    public static TheoryData<ObjectId> GameCategoryIds() =>
    [
        SystemCategoryDefinitions.PhysicalGameId,
        SystemCategoryDefinitions.DigitalGameId
    ];
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter SystemCategoryDefinitionsTests`
Expected: `Game_categories_declare_every_igdb_field` 失敗，缺 `igdbId`、`genres`、`platforms`、`igdbRating`、`coverUrl`。

- [ ] **Step 3: 實作**

`src/MyCollection.Infrastructure/Mongo/SystemCategoryDefinitions.cs`：

檔頭加上 `using MyCollection.Infrastructure.Providers.Igdb;`。

「實體遊戲」的欄位清單（第 16–27 行）末尾追加 5 個欄位：

```csharp
            Select("condition", "保存狀況", ["全新", "近全新", "良好", "普通", "需修復"], true),
            // 以下為 IGDB 補完寫入的欄位。developer / publisher / releaseDate 上方已宣告。
            // 定義來源是 IgdbFields，這裡只列出上方尚未出現的 key。
            Number(IgdbFields.MarkerKey, "IGDB ID"),
            Text("genres", "類型", searchable: true),
            Text("platforms", "發行平台", searchable: true),
            Number("igdbRating", "IGDB 評分"),
            Url("coverUrl", "IGDB 封面網址")
        ]),
```

「數位遊戲」的欄位清單（第 29–38 行）末尾同樣追加：

```csharp
            Url("iconUrl", "圖示網址"),
            // 同上：IGDB 補完欄位
            Number(IgdbFields.MarkerKey, "IGDB ID"),
            Text("genres", "類型", searchable: true),
            Text("platforms", "發行平台", searchable: true),
            Number("igdbRating", "IGDB 評分"),
            Url("coverUrl", "IGDB 封面網址")
        ]),
```

> 若 `Number` / `Text` / `Url` 私有輔助方法的參數名稱與此處不符，以檔案內既有呼叫為準調整具名引數，不要改動輔助方法本身。

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test --filter "SystemCategoryDefinitionsTests|SystemCategorySeederTests"`
Expected: `SystemCategoryDefinitionsTests` `Passed: 7`（3 個 `[Fact]` + 兩個 `[Theory]` 各展開 2 個），`SystemCategorySeederTests` 維持全綠。

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(categories): declare igdb fields on the system game categories"
```

---

### Task 9：補完寫入器

**Files:**
- Create: `src/MyCollection.Application/Ingestion/IItemEnrichWriter.cs`
- Create: `src/MyCollection.Infrastructure/Mongo/MongoItemEnrichWriter.cs`
- Test: `tests/MyCollection.Tests/Integration/MongoItemEnrichWriterTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Integration/MongoItemEnrichWriterTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoItemEnrichWriterTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId Category = ObjectId.GenerateNewId();
    private static readonly DateTime CreatedAt = new(2026, 7, 1, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EnrichedAt = new(2026, 8, 1, 3, 0, 0, DateTimeKind.Utc);

    private MongoItemEnrichWriter _sut = null!;
    private ObjectId _itemId;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _sut = new MongoItemEnrichWriter(fixture.Context);
        _itemId = await InsertSteamItemAsync(Owner);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<ObjectId> InsertSteamItemAsync(ObjectId ownerId)
    {
        var item = new Item
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = ownerId,
            CategoryId = Category,
            Name = "Team Fortress 2",
            Description = null,
            Source = ItemSource.Steam,
            IsShowcased = true,
            Tags = ["最愛", "FPS"],
            Acquisition = new Acquisition { Vendor = "Steam 特賣" },
            Images = [new ItemImage { Id = "img1", Path = "a", CardPath = "b", ThumbPath = "c" }],
            ExternalRef = new ExternalRef
            {
                Provider = ProviderKeys.Steam, ExternalId = "440", LastSyncedAt = CreatedAt
            },
            Attributes = new BsonDocument { { "playtimeForever", 1234 } },
            CreatedAt = CreatedAt,
            UpdatedAt = CreatedAt
        };

        await fixture.Context.Items.InsertOneAsync(item);

        return item.Id;
    }

    private Task<Item> LoadAsync(ObjectId id) =>
        fixture.Context.Items.Find(Builders<Item>.Filter.Eq(x => x.Id, id)).FirstAsync();

    private static ItemEnrichment Enrichment(ObjectId itemId, string? description = null) =>
        new(itemId, description, new Dictionary<string, object?>
        {
            ["igdbId"] = 1942L,
            ["developer"] = "Valve",
            ["igdbRating"] = 93.5d
        });

    [Fact]
    public async Task Writes_the_provider_attributes()
    {
        var matched = await _sut.ApplyAsync(
            Owner, [Enrichment(_itemId)], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None);

        matched.Should().Be(1);

        var item = await LoadAsync(_itemId);
        item.Attributes["igdbId"].AsInt64.Should().Be(1942);
        item.Attributes["developer"].AsString.Should().Be("Valve");
        item.Attributes["igdbRating"].AsDouble.Should().Be(93.5);
        item.UpdatedAt.Should().Be(EnrichedAt);
    }

    [Fact]
    public async Task Preserves_attributes_written_by_other_providers()
    {
        await _sut.ApplyAsync(Owner, [Enrichment(_itemId)], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None);

        (await LoadAsync(_itemId)).Attributes["playtimeForever"].AsInt32.Should().Be(1234);
    }

    [Fact]
    public async Task Never_touches_fields_the_user_owns()
    {
        await _sut.ApplyAsync(Owner, [Enrichment(_itemId)], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None);

        var item = await LoadAsync(_itemId);
        item.Name.Should().Be("Team Fortress 2", "Steam 的名稱是使用者在庫裡認得的那個");
        item.IsShowcased.Should().BeTrue();
        item.Tags.Should().BeEquivalentTo("最愛", "FPS");
        item.Acquisition!.Vendor.Should().Be("Steam 特賣");
        item.Images.Should().ContainSingle();
        item.CreatedAt.Should().Be(CreatedAt);
        item.Source.Should().Be(ItemSource.Steam);
    }

    [Fact]
    public async Task Writes_the_description_when_one_is_supplied()
    {
        await _sut.ApplyAsync(
            Owner, [Enrichment(_itemId, "A team-based shooter.")], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None);

        (await LoadAsync(_itemId)).Description.Should().Be("A team-based shooter.");
    }

    [Fact]
    public async Task Leaves_the_description_alone_when_none_is_supplied()
    {
        await fixture.Context.Items.UpdateOneAsync(
            Builders<Item>.Filter.Eq(x => x.Id, _itemId),
            Builders<Item>.Update.Set(x => x.Description, "我自己寫的心得"));

        await _sut.ApplyAsync(Owner, [Enrichment(_itemId)], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None);

        (await LoadAsync(_itemId)).Description.Should().Be("我自己寫的心得");
    }

    [Fact]
    public async Task Never_touches_another_owners_item()
    {
        var otherOwner = ObjectId.GenerateNewId();
        var otherItemId = await InsertSteamItemAsync(otherOwner);

        var matched = await _sut.ApplyAsync(
            Owner, [Enrichment(otherItemId)], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None);

        matched.Should().Be(0);
        (await LoadAsync(otherItemId)).Attributes.Should().NotContain(e => e.Name == "igdbId");
    }

    [Fact]
    public async Task Never_creates_an_item_for_an_id_that_does_not_exist()
    {
        var matched = await _sut.ApplyAsync(
            Owner, [Enrichment(ObjectId.GenerateNewId())], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None);

        matched.Should().Be(0);
        (await fixture.Context.Items.CountDocumentsAsync(FilterDefinition<Item>.Empty)).Should().Be(1);
    }

    [Fact]
    public async Task Empty_input_is_a_no_op()
    {
        (await _sut.ApplyAsync(Owner, [], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None))
            .Should().Be(0);
    }

    [Fact]
    public async Task An_enrichment_with_nothing_to_write_is_skipped_entirely()
    {
        var empty = new ItemEnrichment(_itemId, null, new Dictionary<string, object?>());

        var matched = await _sut.ApplyAsync(Owner, [empty], EnrichedAt, ProviderKeys.Igdb, CancellationToken.None);

        matched.Should().Be(0);
        (await LoadAsync(_itemId)).UpdatedAt.Should().Be(CreatedAt, "沒有東西要寫就不該動 updatedAt");
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter MongoItemEnrichWriterTests`
Expected: 編譯失敗，找不到 `MongoItemEnrichWriter` / `ItemEnrichment`。

- [ ] **Step 3: 實作契約**

`src/MyCollection.Application/Ingestion/IItemEnrichWriter.cs`：

```csharp
using MongoDB.Bson;

namespace MyCollection.Application.Ingestion;

/// <summary>
/// 單一品項要套用的 provider 欄位。Description 為 null 代表不動——
/// 「僅在目前為空時寫入」的判斷由 handler 做完，寫入器不讀取現有文件。
/// </summary>
public record ItemEnrichment(
    ObjectId ItemId,
    string? Description,
    IReadOnlyDictionary<string, object?> Attributes);

public interface IItemEnrichWriter
{
    /// <summary>
    /// 對既有品項套用 provider 欄位，回傳實際命中的筆數。
    /// 只 $set 傳入的 attributes 與非 null 的 description；
    /// name / tags / isShowcased / acquisition / images / createdAt / source 一律不碰。
    /// 絕不 upsert：補完只更新，不建立。
    /// </summary>
    Task<int> ApplyAsync(
        ObjectId ownerId,
        IReadOnlyList<ItemEnrichment> enrichments,
        DateTime enrichedAt,
        string providerKey,
        CancellationToken ct);
}
```

- [ ] **Step 4: 實作寫入器**

`src/MyCollection.Infrastructure/Mongo/MongoItemEnrichWriter.cs`：

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoItemEnrichWriter(MongoContext context) : IItemEnrichWriter
{
    public async Task<int> ApplyAsync(
        ObjectId ownerId,
        IReadOnlyList<ItemEnrichment> enrichments,
        DateTime enrichedAt,
        string providerKey,
        CancellationToken ct)
    {
        var models = enrichments
            .Where(e => e.Attributes.Count > 0 || e.Description is not null)
            .Select(e => BuildModel(ownerId, e, enrichedAt))
            .ToArray();

        if (models.Length == 0)
        {
            return 0;
        }

        try
        {
            var result = await context.Items.BulkWriteAsync(
                models, new BulkWriteOptions { IsOrdered = false }, ct);

            // MatchedCount 而非 ModifiedCount：重跑相同內容不會產生欄位變更，
            // ModifiedCount 會是 0，報告就會謊稱什麼都沒處理。
            return (int)result.MatchedCount;
        }
        catch (MongoBulkWriteException<Item> ex)
        {
            // 部分成功如實記錄，不做全有全無
            return (int)ex.Result.MatchedCount;
        }
        catch (MongoException ex)
        {
            throw new ProviderException(providerKey, $"Bulk write failed: {ex.Message}", ex);
        }
    }

    private static UpdateOneModel<Item> BuildModel(
        ObjectId ownerId, ItemEnrichment enrichment, DateTime enrichedAt)
    {
        // 授權寫在倉儲層：ownerId 條件擺在 filter 開頭。
        // 漏寫的後果是「查無資料」而不是「別人的資料被改」。
        var filter = Builders<Item>.Filter.And(
            Builders<Item>.Filter.Eq(x => x.OwnerId, ownerId),
            Builders<Item>.Filter.Eq(x => x.Id, enrichment.ItemId));

        var set = new BsonDocument { { "updatedAt", enrichedAt } };

        if (enrichment.Description is not null)
        {
            set["description"] = enrichment.Description;
        }

        foreach (var (key, value) in enrichment.Attributes)
        {
            set[$"attributes.{key}"] = ToBson(value);
        }

        return new UpdateOneModel<Item>(
            filter,
            new BsonDocumentUpdateDefinition<Item>(new BsonDocument { { "$set", set } }))
        {
            // 補完只更新既有品項。IsUpsert 會讓查無此品項時憑空生出一筆殘缺文件。
            IsUpsert = false
        };
    }

    /// <summary>
    /// null 一律映成 BSON null。不可寫成三元運算子直接混 BsonNull 與 string（CS0173），
    /// 也不可呼叫 BsonValue.Create(null)（擲 ArgumentNullException）。
    /// </summary>
    private static BsonValue ToBson(object? value) =>
        value is null ? BsonNull.Value : BsonValue.Create(value);
}
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test --filter MongoItemEnrichWriterTests`
Expected: `Passed: 9`

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(ingestion): add item enrichment writer"
```

---

### Task 10：補完候選查詢

**Files:**
- Modify: `src/MyCollection.Application/Items/IItemRepository.cs`
- Modify: `src/MyCollection.Infrastructure/Mongo/MongoItemRepository.cs`
- Test: `tests/MyCollection.Tests/Integration/MongoItemRepositoryEnrichmentTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Integration/MongoItemRepositoryEnrichmentTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoItemRepositoryEnrichmentTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId Other = ObjectId.GenerateNewId();
    private static readonly ObjectId Category = ObjectId.GenerateNewId();

    private MongoItemRepository _sut = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(c => c.UserId).Returns(Owner);

        _sut = new MongoItemRepository(fixture.Context, userContext.Object);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<ObjectId> InsertAsync(
        ObjectId ownerId, string name, string? steamAppId, long? igdbId)
    {
        var attributes = new BsonDocument();
        if (igdbId is not null)
        {
            attributes["igdbId"] = igdbId.Value;
        }

        var item = new Item
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = ownerId,
            CategoryId = Category,
            Name = name,
            Source = steamAppId is null ? ItemSource.Manual : ItemSource.Steam,
            ExternalRef = steamAppId is null
                ? null
                : new ExternalRef
                {
                    Provider = ProviderKeys.Steam,
                    ExternalId = steamAppId,
                    LastSyncedAt = DateTime.UtcNow
                },
            Attributes = attributes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await fixture.Context.Items.InsertOneAsync(item);

        return item.Id;
    }

    [Fact]
    public async Task Lists_synced_items_that_lack_the_marker()
    {
        await InsertAsync(Owner, "TF2", "440", igdbId: null);
        await InsertAsync(Owner, "Portal 2", "620", igdbId: 1234);
        await InsertAsync(Owner, "手辦", null, igdbId: null);

        var candidates = await _sut.ListEnrichmentCandidatesAsync("igdbId", 50, CancellationToken.None);

        candidates.Select(i => i.Name).Should().BeEquivalentTo("TF2");
    }

    [Fact]
    public async Task Never_lists_another_owners_items()
    {
        await InsertAsync(Other, "別人的 TF2", "440", igdbId: null);

        var candidates = await _sut.ListEnrichmentCandidatesAsync("igdbId", 50, CancellationToken.None);

        candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task Honours_the_limit()
    {
        await InsertAsync(Owner, "A", "1", igdbId: null);
        await InsertAsync(Owner, "B", "2", igdbId: null);
        await InsertAsync(Owner, "C", "3", igdbId: null);

        var candidates = await _sut.ListEnrichmentCandidatesAsync("igdbId", 2, CancellationToken.None);

        candidates.Should().HaveCount(2);
    }

    [Fact]
    public async Task Loads_items_by_id()
    {
        var first = await InsertAsync(Owner, "A", "1", igdbId: 11);
        await InsertAsync(Owner, "B", "2", igdbId: 22);

        var items = await _sut.ListByIdsAsync([first], CancellationToken.None);

        items.Select(i => i.Name).Should().BeEquivalentTo("A");
    }

    [Fact]
    public async Task Loading_by_id_never_crosses_owners()
    {
        var otherItem = await InsertAsync(Other, "別人的", "1", igdbId: null);

        var items = await _sut.ListByIdsAsync([otherItem], CancellationToken.None);

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task Loading_an_empty_id_list_returns_nothing()
    {
        await InsertAsync(Owner, "A", "1", igdbId: null);

        (await _sut.ListByIdsAsync([], CancellationToken.None)).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter MongoItemRepositoryEnrichmentTests`
Expected: 編譯失敗，`IItemRepository` 沒有 `ListEnrichmentCandidatesAsync` / `ListByIdsAsync`。

- [ ] **Step 3: 加入契約**

`src/MyCollection.Application/Items/IItemRepository.cs`，在 `ListTagsAsync` 之後加入：

```csharp
    /// <summary>
    /// 補完候選：有外部來源綁定（externalRef 非 null）、但 attributes 尚未帶 markerKey 的品項。
    /// 手動建檔且未綁定過的品項不在其中——補完不猜，那些應走搜尋建檔。
    /// </summary>
    Task<IReadOnlyList<Item>> ListEnrichmentCandidatesAsync(
        string markerKey, int limit, CancellationToken ct);

    /// <summary>依 id 批次載入自己的品項。不存在或不屬於自己的 id 直接不出現在結果中。</summary>
    Task<IReadOnlyList<Item>> ListByIdsAsync(IReadOnlyList<ObjectId> ids, CancellationToken ct);
```

- [ ] **Step 4: 實作**

`src/MyCollection.Infrastructure/Mongo/MongoItemRepository.cs`，在類別末尾加入：

```csharp
    public async Task<IReadOnlyList<Item>> ListEnrichmentCandidatesAsync(
        string markerKey, int limit, CancellationToken ct)
    {
        var filter = Filter.And(
            OwnerFilter,
            Filter.Ne(x => x.ExternalRef, null),
            Filter.Exists($"attributes.{markerKey}", false));

        return await context.Items
            .Find(filter)
            .SortBy(x => x.Id)
            .Limit(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Item>> ListByIdsAsync(IReadOnlyList<ObjectId> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await context.Items
            .Find(Filter.And(OwnerFilter, Filter.In(x => x.Id, ids)))
            .ToListAsync(ct);
    }
```

> `Filter` 與 `OwnerFilter` 是該類別既有的私有成員（見檔案第 19 行）。若建構子參數名稱不是 `context`，以檔案內既有寫法為準。

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test --filter MongoItemRepositoryEnrichmentTests`
Expected: `Passed: 6`

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(items): add enrichment candidate and batch-by-id queries"
```

---

### Task 11：搜尋查詢與端點

**Files:**
- Create: `src/MyCollection.Application/Ingestion/SearchProviderQuery.cs`
- Modify: `src/MyCollection.Api/Endpoints/IngestionEndpoints.cs`
- Test: `tests/MyCollection.Tests/Unit/SearchProviderQueryTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/SearchProviderQueryTests.cs`：

```csharp
using FluentAssertions;
using Moq;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class SearchProviderQueryTests
{
    private readonly Mock<ISearchProvider> _provider = new();

    public SearchProviderQueryTests() =>
        _provider.SetupGet(p => p.Key).Returns(ProviderKeys.Igdb);

    private ProviderRegistry Registry() => new([_provider.Object]);

    private static ExternalItem Item() => new(
        "1942",
        "The Witcher 3: Wild Hunt",
        "A story-driven adventure.",
        new Uri("https://images.igdb.com/igdb/image/upload/t_cover_big/co1wyy.jpg"),
        new Dictionary<string, object?> { ["igdbId"] = 1942L, ["developer"] = "CD Projekt RED" });

    [Fact]
    public async Task Maps_provider_results_to_dtos()
    {
        _provider.Setup(p => p.SearchAsync("witcher", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Item()]);

        var result = await new SearchProviderQueryHandler(Registry())
            .Handle(new SearchProviderQuery(ProviderKeys.Igdb, "witcher"), CancellationToken.None);

        var dto = result.Should().ContainSingle().Subject;
        dto.Provider.Should().Be("igdb");
        dto.ExternalId.Should().Be("1942");
        dto.Name.Should().Be("The Witcher 3: Wild Hunt");
        dto.ImageUrl.Should().Be("https://images.igdb.com/igdb/image/upload/t_cover_big/co1wyy.jpg");
        dto.Attributes.Should().ContainKey("developer");
    }

    [Fact]
    public async Task Returns_an_empty_list_rather_than_throwing_when_nothing_matches()
    {
        _provider.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await new SearchProviderQueryHandler(Registry())
            .Handle(new SearchProviderQuery(ProviderKeys.Igdb, "zzzz"), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_provider_throws_NotFoundException()
    {
        var act = () => new SearchProviderQueryHandler(Registry())
            .Handle(new SearchProviderQuery("discogs", "witcher"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData("", "witcher", 20, false)]
    [InlineData("igdb", "", 20, false)]
    [InlineData("igdb", "a", 20, false)]
    [InlineData("igdb", "ab", 20, true)]
    [InlineData("igdb", "witcher", 0, false)]
    [InlineData("igdb", "witcher", 51, false)]
    [InlineData("igdb", "witcher", 50, true)]
    public void Validates_the_request(string provider, string query, int limit, bool expected)
    {
        new SearchProviderQueryValidator()
            .Validate(new SearchProviderQuery(provider, query, limit))
            .IsValid.Should().Be(expected);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter SearchProviderQueryTests`
Expected: 編譯失敗，找不到 `SearchProviderQuery`。

- [ ] **Step 3: 實作**

`src/MyCollection.Application/Ingestion/SearchProviderQuery.cs`：

```csharp
using FluentValidation;
using MediatR;

namespace MyCollection.Application.Ingestion;

/// <summary>
/// 關鍵字搜尋外部來源，供前端預填表單，不建立品項。
/// 回傳型別沿用 FetchByUrlQuery 的 FetchedMetadataDto——兩者對前端是同一件事。
/// </summary>
public record SearchProviderQuery(string Provider, string Query, int Limit = 20)
    : IRequest<IReadOnlyList<FetchedMetadataDto>>;

public sealed class SearchProviderQueryValidator : AbstractValidator<SearchProviderQuery>
{
    public SearchProviderQueryValidator()
    {
        RuleFor(x => x.Provider).NotEmpty();

        // 單字元查詢對 IGDB 沒有意義，只會回一堆雜訊
        RuleFor(x => x.Query).NotEmpty().MinimumLength(2);

        RuleFor(x => x.Limit).InclusiveBetween(1, 50);
    }
}

public sealed class SearchProviderQueryHandler(ProviderRegistry registry)
    : IRequestHandler<SearchProviderQuery, IReadOnlyList<FetchedMetadataDto>>
{
    public async Task<IReadOnlyList<FetchedMetadataDto>> Handle(
        SearchProviderQuery request, CancellationToken cancellationToken)
    {
        var provider = registry.Require<ISearchProvider>(request.Provider);

        var items = await provider.SearchAsync(request.Query, request.Limit, cancellationToken);

        return items.Select(item => new FetchedMetadataDto(
            provider.Key,
            item.ExternalId,
            item.Name,
            item.Description,
            item.ImageUrl?.ToString(),
            item.Attributes)).ToArray();
    }
}
```

- [ ] **Step 4: 加上端點**

`src/MyCollection.Api/Endpoints/IngestionEndpoints.cs`，在 `/fetch` 之後加入：

```csharp
        group.MapGet("/search", async (
            string provider, string q, int? limit, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new SearchProviderQuery(provider, q, limit ?? 20), ct)));
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test --filter SearchProviderQueryTests`
Expected: `Passed: 10`（3 個 `[Fact]` + `[Theory]` 展開 7 個）

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(ingestion): add provider keyword search"
```

---

### Task 12：補完命令與端點

**Files:**
- Create: `src/MyCollection.Application/Ingestion/EnrichCommand.cs`
- Modify: `src/MyCollection.Api/Endpoints/IngestionEndpoints.cs`
- Test: `tests/MyCollection.Tests/Unit/EnrichCommandTests.cs`

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/EnrichCommandTests.cs`：

```csharp
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class EnrichCommandTests
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId CategoryId = ObjectId.GenerateNewId();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 1, 3, 0, 0, TimeSpan.Zero));
    private readonly Mock<ISearchProvider> _provider = new();
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<ISyncJobRepository> _jobs = new();
    private readonly Mock<IItemEnrichWriter> _writer = new();
    private readonly Mock<IUserContext> _userContext = new();

    private readonly List<ItemEnrichment> _written = [];

    public EnrichCommandTests()
    {
        _provider.SetupGet(p => p.Key).Returns(ProviderKeys.Igdb);
        _provider.SetupGet(p => p.MarkerAttributeKey).Returns("igdbId");
        _userContext.SetupGet(c => c.UserId).Returns(Owner);

        _categories.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Category(["igdbId", "developer", "genres"])]);

        _writer.Setup(w => w.ApplyAsync(
                It.IsAny<ObjectId>(), It.IsAny<IReadOnlyList<ItemEnrichment>>(),
                It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<ObjectId, IReadOnlyList<ItemEnrichment>, DateTime, string, CancellationToken>(
                (_, e, _, _, _) => _written.AddRange(e))
            .ReturnsAsync((ObjectId _, IReadOnlyList<ItemEnrichment> e, DateTime _, string _, CancellationToken _)
                => e.Count);
    }

    private static Category Category(string[] fieldKeys) => new()
    {
        Id = CategoryId,
        Name = "數位遊戲",
        Fields = fieldKeys.Select(k => new CategoryField
        {
            Key = k, Label = k, Type = k is "igdbId" ? FieldType.Number : FieldType.Text
        }).ToList()
    };

    private static Item SteamItem(string appId, string name = "TF2", string? description = null) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = Owner,
        CategoryId = CategoryId,
        Name = name,
        Description = description,
        Source = ItemSource.Steam,
        ExternalRef = new ExternalRef
        {
            Provider = ProviderKeys.Steam, ExternalId = appId, LastSyncedAt = DateTime.UtcNow
        },
        Attributes = []
    };

    private static Item BoundItem(long igdbId) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = Owner,
        CategoryId = CategoryId,
        Name = "已綁定",
        Attributes = new BsonDocument { { "igdbId", igdbId } }
    };

    private static ExternalItem Found(string externalId) => new(
        externalId,
        "The Witcher 3",
        "An adventure.",
        null,
        new Dictionary<string, object?>
        {
            ["igdbId"] = 1942L,
            ["developer"] = "CD Projekt RED",
            ["genres"] = "RPG",
            ["igdbRating"] = 93.5d
        });

    private EnrichCommandHandler CreateSut() => new(
        new ProviderRegistry([_provider.Object]),
        _items.Object,
        _categories.Object,
        _jobs.Object,
        _writer.Object,
        _userContext.Object,
        _time);

    private void SetupLookup(IReadOnlyDictionary<string, ExternalItem> found, params string[] failed) =>
        _provider.Setup(p => p.FetchByExternalIdsAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalLookupResult(found, failed));

    [Fact]
    public async Task Batch_mode_enriches_candidates_that_lack_the_marker()
    {
        _items.Setup(r => r.ListEnrichmentCandidatesAsync("igdbId", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([SteamItem("440")]);
        SetupLookup(new Dictionary<string, ExternalItem> { ["steam:440"] = Found("1942") });

        var job = await CreateSut().Handle(new EnrichCommand(ProviderKeys.Igdb), CancellationToken.None);

        job.Provider.Should().Be("igdb");
        job.Status.Should().Be("Succeeded");
        job.Updated.Should().Be(1);
        job.Skipped.Should().Be(0);
        job.Failed.Should().Be(0);
        job.Created.Should().Be(0, "補完永遠不建立品項");
    }

    [Fact]
    public async Task Uses_the_existing_marker_instead_of_the_steam_id_when_present()
    {
        var item = BoundItem(1942);
        _items.Setup(r => r.ListByIdsAsync(It.IsAny<IReadOnlyList<ObjectId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);
        SetupLookup(new Dictionary<string, ExternalItem> { ["igdb:1942"] = Found("1942") });

        await CreateSut().Handle(
            new EnrichCommand(ProviderKeys.Igdb, [item.Id.ToString()]), CancellationToken.None);

        _provider.Verify(p => p.FetchByExternalIdsAsync(
            It.Is<IReadOnlyList<string>>(ids => ids.Single() == "igdb:1942"),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Skips_items_with_neither_a_marker_nor_an_external_ref()
    {
        var orphan = new Item
        {
            Id = ObjectId.GenerateNewId(), OwnerId = Owner, CategoryId = CategoryId,
            Name = "手辦", Attributes = []
        };
        _items.Setup(r => r.ListByIdsAsync(It.IsAny<IReadOnlyList<ObjectId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([orphan]);
        SetupLookup(new Dictionary<string, ExternalItem>());

        var job = await CreateSut().Handle(
            new EnrichCommand(ProviderKeys.Igdb, [orphan.Id.ToString()]), CancellationToken.None);

        job.Skipped.Should().Be(1);
        job.Failed.Should().Be(0);
        job.Updated.Should().Be(0);
    }

    [Fact]
    public async Task Counts_a_lookup_miss_as_skipped_not_failed()
    {
        _items.Setup(r => r.ListEnrichmentCandidatesAsync("igdbId", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([SteamItem("440"), SteamItem("620", "Portal 2")]);
        SetupLookup(new Dictionary<string, ExternalItem> { ["steam:440"] = Found("1942") });

        var job = await CreateSut().Handle(new EnrichCommand(ProviderKeys.Igdb), CancellationToken.None);

        job.Updated.Should().Be(1);
        job.Skipped.Should().Be(1);
        job.Failed.Should().Be(0);
    }

    [Fact]
    public async Task Counts_a_request_level_failure_as_failed()
    {
        _items.Setup(r => r.ListEnrichmentCandidatesAsync("igdbId", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([SteamItem("440")]);
        SetupLookup(new Dictionary<string, ExternalItem>(), "steam:440");

        var job = await CreateSut().Handle(new EnrichCommand(ProviderKeys.Igdb), CancellationToken.None);

        job.Failed.Should().Be(1);
        job.Skipped.Should().Be(0);
    }

    [Fact]
    public async Task Drops_attributes_the_target_category_has_not_declared()
    {
        _items.Setup(r => r.ListEnrichmentCandidatesAsync("igdbId", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([SteamItem("440")]);
        SetupLookup(new Dictionary<string, ExternalItem> { ["steam:440"] = Found("1942") });

        await CreateSut().Handle(new EnrichCommand(ProviderKeys.Igdb), CancellationToken.None);

        _written.Single().Attributes.Keys.Should().BeEquivalentTo("igdbId", "developer", "genres");
        _written.Single().Attributes.Should().NotContainKey(
            "igdbRating", "品類沒宣告的 key 會被 AttributeValidator 擋掉");
    }

    [Fact]
    public async Task Writes_the_summary_only_when_the_item_has_no_description()
    {
        _items.Setup(r => r.ListEnrichmentCandidatesAsync("igdbId", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([SteamItem("440"), SteamItem("620", "Portal 2", "我自己寫的心得")]);
        SetupLookup(new Dictionary<string, ExternalItem>
        {
            ["steam:440"] = Found("1942"),
            ["steam:620"] = Found("1943")
        });

        await CreateSut().Handle(new EnrichCommand(ProviderKeys.Igdb), CancellationToken.None);

        _written.Should().HaveCount(2);
        _written.Should().ContainSingle(e => e.Description == "An adventure.");
        _written.Should().ContainSingle(e => e.Description == null);
    }

    [Fact]
    public async Task Records_a_failed_job_and_rethrows_when_the_provider_blows_up()
    {
        _items.Setup(r => r.ListEnrichmentCandidatesAsync("igdbId", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([SteamItem("440")]);
        _provider.Setup(p => p.FetchByExternalIdsAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException(ProviderKeys.Igdb, "boom"));

        var act = () => CreateSut().Handle(new EnrichCommand(ProviderKeys.Igdb), CancellationToken.None);

        await act.Should().ThrowAsync<ProviderException>();
        _jobs.Verify(j => j.UpdateAsync(
            It.Is<SyncJob>(job => job.Status == SyncStatus.Failed && job.Error == "boom"),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Requires_a_search_capable_provider()
    {
        var bulkOnly = new Mock<IBulkSyncProvider>();
        bulkOnly.SetupGet(p => p.Key).Returns(ProviderKeys.Steam);

        var sut = new EnrichCommandHandler(
            new ProviderRegistry([bulkOnly.Object]), _items.Object, _categories.Object,
            _jobs.Object, _writer.Object, _userContext.Object, _time);

        var act = () => sut.Handle(new EnrichCommand(ProviderKeys.Steam), CancellationToken.None);

        await act.Should().ThrowAsync<ProviderException>();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(500, 200)]
    [InlineData(50, 50)]
    public async Task Clamps_the_batch_limit(int requested, int expected)
    {
        _items.Setup(r => r.ListEnrichmentCandidatesAsync("igdbId", expected, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        SetupLookup(new Dictionary<string, ExternalItem>());

        await CreateSut().Handle(new EnrichCommand(ProviderKeys.Igdb, null, requested), CancellationToken.None);

        _items.Verify(r => r.ListEnrichmentCandidatesAsync("igdbId", expected, It.IsAny<CancellationToken>()));
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter EnrichCommandTests`
Expected: 編譯失敗，找不到 `EnrichCommand` / `EnrichCommandHandler`。

- [ ] **Step 3: 實作**

`src/MyCollection.Application/Ingestion/EnrichCommand.cs`：

```csharp
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Ingestion;

/// <summary>
/// 補完既有品項的 provider 欄位。給 ItemIds 是單筆／重抓，不給是批次補完尚未綁定的品項。
/// </summary>
public record EnrichCommand(
    string Provider,
    IReadOnlyList<string>? ItemIds = null,
    int Limit = 50) : IRequest<SyncJobDto>;

public sealed class EnrichCommandValidator : AbstractValidator<EnrichCommand>
{
    public EnrichCommandValidator()
    {
        RuleFor(x => x.Provider).NotEmpty();

        RuleForEach(x => x.ItemIds)
            .Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("ItemIds must contain valid object ids.");
    }
}

public sealed class EnrichCommandHandler(
    ProviderRegistry registry,
    IItemRepository items,
    ICategoryRepository categories,
    ISyncJobRepository jobs,
    IItemEnrichWriter writer,
    IUserContext userContext,
    TimeProvider timeProvider) : IRequestHandler<EnrichCommand, SyncJobDto>
{
    public async Task<SyncJobDto> Handle(EnrichCommand request, CancellationToken cancellationToken)
    {
        var provider = registry.Require<ISearchProvider>(request.Provider);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var job = new SyncJob
        {
            Id = ObjectId.GenerateNewId(),
            Provider = provider.Key,
            Status = SyncStatus.Running,
            StartedAt = now
        };
        await jobs.InsertAsync(job, cancellationToken);

        try
        {
            var targets = await LoadTargetsAsync(request, provider, cancellationToken);

            // 沒有可用外部識別碼的品項不猜，直接記為 skipped
            var addressable = targets.Where(t => t.ExternalId is not null).ToArray();
            job.Skipped = targets.Length - addressable.Length;

            if (addressable.Length > 0)
            {
                var lookup = await provider.FetchByExternalIdsAsync(
                    addressable.Select(t => t.ExternalId!).Distinct(StringComparer.Ordinal).ToArray(),
                    cancellationToken);

                var failedIds = lookup.FailedIds.ToHashSet(StringComparer.Ordinal);
                var allowedKeys = await AllowedKeysByCategoryAsync(addressable, cancellationToken);

                var enrichments = new List<ItemEnrichment>();

                foreach (var target in addressable)
                {
                    if (failedIds.Contains(target.ExternalId!))
                    {
                        job.Failed++;
                    }
                    else if (lookup.Found.TryGetValue(target.ExternalId!, out var source))
                    {
                        enrichments.Add(ToEnrichment(target.Item, source, allowedKeys[target.Item.CategoryId]));
                    }
                    else
                    {
                        // 查無對應不是失敗
                        job.Skipped++;
                    }
                }

                job.Updated = await writer.ApplyAsync(
                    userContext.UserId, enrichments, now, provider.Key, cancellationToken);
            }

            job.Status = SyncStatus.Succeeded;
        }
        catch (Exception ex)
        {
            job.Status = SyncStatus.Failed;
            job.Error = ex.Message;
            job.FinishedAt = timeProvider.GetUtcNow().UtcDateTime;
            await jobs.UpdateAsync(job, cancellationToken);
            throw;
        }

        job.FinishedAt = timeProvider.GetUtcNow().UtcDateTime;
        await jobs.UpdateAsync(job, cancellationToken);

        return SyncJobMapper.ToDto(job);
    }

    private async Task<EnrichTarget[]> LoadTargetsAsync(
        EnrichCommand request, ISearchProvider provider, CancellationToken ct)
    {
        var loaded = request.ItemIds is { Count: > 0 }
            ? await items.ListByIdsAsync(
                request.ItemIds.Select(ObjectId.Parse).ToArray(), ct)
            : await items.ListEnrichmentCandidatesAsync(
                provider.MarkerAttributeKey, Math.Clamp(request.Limit, 1, 200), ct);

        return loaded.Select(item => new EnrichTarget(item, ExternalIdFor(item, provider))).ToArray();
    }

    /// <summary>
    /// 已綁定過就直接用 marker，不必再繞 Steam 反查一次；
    /// 否則退回外部來源的識別碼。兩者皆無代表這是手動建檔且未綁定的品項——不猜。
    /// </summary>
    private static string? ExternalIdFor(Item item, ISearchProvider provider)
    {
        if (item.Attributes.TryGetValue(provider.MarkerAttributeKey, out var marker)
            && !marker.IsBsonNull)
        {
            return $"{provider.Key}:{marker.ToInt64()}";
        }

        return item.ExternalRef is { } reference
            ? $"{reference.Provider}:{reference.ExternalId}"
            : null;
    }

    private async Task<Dictionary<ObjectId, HashSet<string>>> AllowedKeysByCategoryAsync(
        IReadOnlyList<EnrichTarget> targets, CancellationToken ct)
    {
        var all = await categories.ListAsync(ct);
        var byId = all.ToDictionary(c => c.Id);

        return targets
            .Select(t => t.Item.CategoryId)
            .Distinct()
            .ToDictionary(
                id => id,
                id => byId.TryGetValue(id, out var category)
                    ? category.Fields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal)
                    : []);
    }

    /// <summary>
    /// 品類沒宣告的 key 會被 AttributeValidator 擋掉，讓使用者之後任何一次更新都失敗，
    /// 所以在這裡先濾掉——功能降級，不是中斷。
    /// </summary>
    private static ItemEnrichment ToEnrichment(Item item, ExternalItem source, HashSet<string> allowedKeys)
    {
        var attributes = source.Attributes
            .Where(pair => allowedKeys.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        // 使用者寫過的心得不該被英文簡介蓋掉
        var description = string.IsNullOrWhiteSpace(item.Description) ? source.Description : null;

        return new ItemEnrichment(item.Id, description, attributes);
    }

    private sealed record EnrichTarget(Item Item, string? ExternalId);
}
```

- [ ] **Step 4: 加上端點**

`src/MyCollection.Api/Endpoints/IngestionEndpoints.cs`，在 `/sync/{provider}` 之後加入：

```csharp
        group.MapPost("/enrich/{provider}", async (
            string provider, EnrichRequest? body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new EnrichCommand(provider, body?.ItemIds, body?.Limit ?? 50), ct)));
```

並在同一個檔案末尾（`MapIngestionEndpoints` 方法之外、類別之內）加入：

```csharp
    /// <summary>兩個欄位都可省略：不給 ItemIds 就是批次補完。</summary>
    public record EnrichRequest(IReadOnlyList<string>? ItemIds, int? Limit);
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test --filter EnrichCommandTests`
Expected: `Passed: 12`（9 個 `[Fact]` + `[Theory]` 展開 3 個）

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(ingestion): add provider enrichment command"
```

---

### Task 13：DI 註冊與設定

**Files:**
- Modify: `src/MyCollection.Infrastructure/DependencyInjection.cs`
- Modify: `src/MyCollection.Api/appsettings.json`
- Modify: `docker-compose.yml`
- Modify: `.env.example`
- Modify: `README.md`

IGDB 未設定時 **provider 完全不註冊**：`/ingest/providers` 不會列出 `igdb`，前端據此隱藏入口，且 `Require<ISearchProvider>("igdb")` 擲 `NotFoundException` → 404。功能是否啟用在啟動時就決定完畢。

- [ ] **Step 1: 改 DependencyInjection**

`src/MyCollection.Infrastructure/DependencyInjection.cs`：

檔頭加上 `using MyCollection.Infrastructure.Providers.Igdb;`。

在 `services.Configure<SteamOptions>(...)` 之後加入：

```csharp
        services.Configure<IgdbOptions>(configuration.GetSection(IgdbOptions.SectionName));
```

在 `services.AddScoped<IItemSyncWriter, MongoItemSyncWriter>();` 之後加入：

```csharp
        services.AddScoped<IItemEnrichWriter, MongoItemEnrichWriter>();
```

在 `services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<OpenGraphProvider>());` 之後、
`services.AddScoped<ProviderRegistry>();` 之前加入：

```csharp
        // IGDB 是選配功能：沒有憑證就整組不註冊，/ingest/providers 自然不會列出它，
        // 前端據此隱藏入口。比「註冊了但呼叫時才炸」乾淨。
        var igdb = configuration.GetSection(IgdbOptions.SectionName).Get<IgdbOptions>() ?? new IgdbOptions();

        if (igdb.IsConfigured)
        {
            // token 快取與速率限制都必須是 singleton——每個請求各自一份等於沒有
            services.AddSingleton<ITwitchTokenProvider, TwitchTokenProvider>();
            services.AddSingleton<IgdbRateLimiter>();

            services.AddHttpClient(TwitchTokenProvider.HttpClientName, client =>
                {
                    client.BaseAddress = new Uri(igdb.TokenBaseAddress);
                    client.Timeout = TimeSpan.FromSeconds(igdb.TimeoutSeconds);
                })
                .AddStandardResilienceHandler();

            services.AddHttpClient<IgdbProvider>(client =>
                {
                    client.BaseAddress = new Uri(igdb.BaseAddress);
                    client.Timeout = TimeSpan.FromSeconds(igdb.TimeoutSeconds);
                })
                .AddStandardResilienceHandler(options =>
                {
                    // 401 要由 IgdbProvider 自己換 token 重試，不能被韌性層吃掉重打
                    options.Retry.MaxRetryAttempts = 2;
                    options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(igdb.TimeoutSeconds);
                    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(igdb.TimeoutSeconds * 4);
                    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(igdb.TimeoutSeconds * 4);
                });

            services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<IgdbProvider>());
        }
```

- [ ] **Step 2: 改 appsettings.json**

`src/MyCollection.Api/appsettings.json`，加入頂層區段（正式環境以環境變數覆蓋）：

```json
  "Igdb": {
    "ClientId": "",
    "ClientSecret": ""
  }
```

- [ ] **Step 3: 改 docker-compose.yml**

`docker-compose.yml` 的 `api` service，在 `Storage__BackupRoot` 之後加入：

```yaml
      # IGDB 為選配：留空即停用整組功能，前端會隱藏搜尋入口。
      # 走 Twitch client credentials（server-to-server），不需要 HTTPS 或重新導向網址。
      Igdb__ClientId: ${IGDB_CLIENT_ID:-}
      Igdb__ClientSecret: ${IGDB_CLIENT_SECRET:-}
```

注意用 `:-`（可空）而非 `:?`（必填）——用 `:?` 會讓沒設定 IGDB 的部署直接起不來。

- [ ] **Step 4: 改 .env.example**

`.env.example` 末尾加入：

```bash
# IGDB 遊戲中繼資料（選配，留空即停用）
#
# 申請步驟：
#   1. 到 https://dev.twitch.tv/console/apps 註冊應用程式（需先啟用 Twitch 帳號的兩階段驗證）
#   2. OAuth Redirect URL 填 http://localhost —— 這個欄位只是註冊表單的必填項，
#      client credentials 流程從頭到尾不會用到它，因此不需要 HTTPS 或公開網域
#   3. Category 選 Application Integration
#   4. 把 Client ID 與 Client Secret 填在下面
IGDB_CLIENT_ID=
IGDB_CLIENT_SECRET=
```

- [ ] **Step 5: 改 README**

`README.md` 的技術棧或快速開始段落附近，加入一段說明：

```markdown
### IGDB 遊戲中繼資料（選配）

設定 `IGDB_CLIENT_ID` 與 `IGDB_CLIENT_SECRET` 後，遊戲品類可以從 IGDB 搜尋建檔，
也可以對 Steam 同步進來的品項批次補上開發商、發行商、發售日期、類型、平台與評分。

走 Twitch 的 client credentials 流程（server-to-server），**不需要 HTTPS、不需要重新導向網址、
不需要公開網域**。Twitch 註冊表單的 OAuth Redirect URL 欄位填 `http://localhost` 即可，
該流程不會使用它。

不設定就整組停用：provider 不註冊，前端不顯示相關入口。
```

- [ ] **Step 6: 確認整個方案編譯且全綠**

Run: `dotnet build && dotnet test`
Expected: 建置成功，所有測試通過。

- [ ] **Step 7: 手動驗證（需真實憑證）**

```bash
export IGDB_CLIENT_ID=... IGDB_CLIENT_SECRET=...
docker compose up --build -d
TOKEN=$(curl -s -X POST http://localhost:8080/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"<你的帳號>","password":"<你的密碼>"}' | jq -r .accessToken)

# 應列出 igdb 且 capabilities 為 Search
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/ingest/providers | jq

# 應回傳多筆結果
curl -s -H "Authorization: Bearer $TOKEN" \
  'http://localhost:8080/api/ingest/search?provider=igdb&q=the%20witcher%203' | jq '.[0]'

# 應回傳一筆 syncJob，Updated + Skipped + Failed 等於處理的品項數
curl -s -X POST -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"limit":10}' http://localhost:8080/api/ingest/enrich/igdb | jq
```

確認 `enrich` 之後某個 Steam 品項的 `attributes` 多了 `igdbId`、`developer` 等欄位，
且 `tags`、`isShowcased`、`name` 未被改動。

- [ ] **Step 8: Commit**

```bash
git add src docker-compose.yml .env.example README.md
git commit -m "chore(igdb): wire optional igdb provider into configuration"
```

---

### Task 14（可選）：自訂品類的欄位補齊

**Files:**
- Create: `src/MyCollection.Application/Categories/ProviderFieldsCommands.cs`
- Modify: `src/MyCollection.Api/Endpoints/CategoryEndpoints.cs`
- Test: `tests/MyCollection.Tests/Unit/ProviderFieldsCommandTests.cs`

> **這個 Task 是可選的。** 系統的「實體遊戲」與「數位遊戲」在 Task 8 已內建 IGDB 欄位，
> 兩條主要流程都不需要這組端點。它只服務「使用者自訂品類 + 想用 IGDB」這個尚未出現的情境，
> 而該情境已有優雅降級（Task 12 的 `ToEnrichment` 會濾掉未宣告的 key）。
> 沒有這個需求就跳過整個 Task，不影響任何其他部分。

- [ ] **Step 1: 寫失敗測試**

`tests/MyCollection.Tests/Unit/ProviderFieldsCommandTests.cs`：

```csharp
using FluentAssertions;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Categories;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class ProviderFieldsCommandTests
{
    private static readonly ObjectId CategoryId = ObjectId.Parse("00000000000000000000000a");

    private readonly Mock<ISearchProvider> _provider = new();
    private readonly Mock<ICategoryRepository> _categories = new();

    public ProviderFieldsCommandTests()
    {
        _provider.SetupGet(p => p.Key).Returns(ProviderKeys.Igdb);
        _provider.SetupGet(p => p.RequiredFields).Returns(
        [
            new CategoryField { Key = "igdbId", Label = "IGDB ID", Type = FieldType.Number },
            new CategoryField { Key = "developer", Label = "開發商", Type = FieldType.Text }
        ]);
    }

    private ProviderRegistry Registry() => new([_provider.Object]);

    private void SetupCategory(params CategoryField[] fields) =>
        _categories.Setup(c => c.GetAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Category
            {
                Id = CategoryId,
                OwnerId = ObjectId.GenerateNewId(),
                Name = "Switch 卡帶",
                Fields = fields.ToList()
            });

    [Fact]
    public async Task Reports_every_field_the_category_lacks()
    {
        SetupCategory(new CategoryField { Key = "developer", Label = "我改過的標籤", Type = FieldType.Text });

        var result = await new MissingProviderFieldsQueryHandler(Registry(), _categories.Object)
            .Handle(new MissingProviderFieldsQuery(CategoryId.ToString(), ProviderKeys.Igdb), CancellationToken.None);

        result.Select(f => f.Key).Should().BeEquivalentTo("igdbId");
    }

    [Fact]
    public async Task Reports_nothing_when_the_category_already_declares_everything()
    {
        SetupCategory(
            new CategoryField { Key = "igdbId", Label = "IGDB ID", Type = FieldType.Number },
            new CategoryField { Key = "developer", Label = "開發商", Type = FieldType.Text });

        var result = await new MissingProviderFieldsQueryHandler(Registry(), _categories.Object)
            .Handle(new MissingProviderFieldsQuery(CategoryId.ToString(), ProviderKeys.Igdb), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_category_throws_NotFoundException()
    {
        _categories.Setup(c => c.GetAsync(It.IsAny<ObjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var act = () => new MissingProviderFieldsQueryHandler(Registry(), _categories.Object)
            .Handle(new MissingProviderFieldsQuery(CategoryId.ToString(), ProviderKeys.Igdb), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Appends_only_the_missing_fields_and_keeps_user_edited_labels()
    {
        SetupCategory(new CategoryField { Key = "developer", Label = "我改過的標籤", Type = FieldType.Text });

        Category? saved = null;
        _categories.Setup(c => c.UpdateAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()))
            .Callback<Category, CancellationToken>((c, _) => saved = c)
            .Returns(Task.CompletedTask);

        await new EnsureProviderFieldsCommandHandler(Registry(), _categories.Object)
            .Handle(new EnsureProviderFieldsCommand(CategoryId.ToString(), ProviderKeys.Igdb), CancellationToken.None);

        saved!.Fields.Select(f => f.Key).Should().BeEquivalentTo("developer", "igdbId");
        saved.Fields.Single(f => f.Key == "developer").Label.Should().Be("我改過的標籤");
    }

    [Fact]
    public async Task System_categories_are_rejected_by_the_repository_guard()
    {
        SetupCategory();
        _categories.Setup(c => c.UpdateAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("System categories cannot be modified."));

        var act = () => new EnsureProviderFieldsCommandHandler(Registry(), _categories.Object)
            .Handle(new EnsureProviderFieldsCommand(CategoryId.ToString(), ProviderKeys.Igdb), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter ProviderFieldsCommandTests`
Expected: 編譯失敗，找不到 `MissingProviderFieldsQuery` / `EnsureProviderFieldsCommand`。

- [ ] **Step 3: 實作**

`src/MyCollection.Application/Categories/ProviderFieldsCommands.cs`：

```csharp
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Categories;

/// <summary>
/// 系統的遊戲品類已在 SystemCategoryDefinitions 內建 provider 欄位，不需要這組端點。
/// 這裡服務的是使用者自訂品類想接上 provider 的情境。
/// </summary>
public record MissingProviderFieldsQuery(string CategoryId, string Provider)
    : IRequest<IReadOnlyList<CategoryFieldDto>>;

public record EnsureProviderFieldsCommand(string CategoryId, string Provider) : IRequest<CategoryDto>;

internal static class ProviderFields
{
    public static async Task<(Category Category, IReadOnlyList<CategoryField> Missing)> ResolveAsync(
        ProviderRegistry registry, ICategoryRepository categories,
        string categoryId, string providerKey, CancellationToken ct)
    {
        var provider = registry.Require<ISearchProvider>(providerKey);

        if (!ObjectId.TryParse(categoryId, out var id))
        {
            throw new NotFoundException("Category", categoryId);
        }

        var category = await categories.GetAsync(id, ct)
                       ?? throw new NotFoundException("Category", categoryId);

        var declared = category.Fields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var missing = provider.RequiredFields.Where(f => !declared.Contains(f.Key)).ToArray();

        return (category, missing);
    }
}

public sealed class MissingProviderFieldsQueryHandler(
    ProviderRegistry registry,
    ICategoryRepository categories) : IRequestHandler<MissingProviderFieldsQuery, IReadOnlyList<CategoryFieldDto>>
{
    public async Task<IReadOnlyList<CategoryFieldDto>> Handle(
        MissingProviderFieldsQuery request, CancellationToken cancellationToken)
    {
        var (_, missing) = await ProviderFields.ResolveAsync(
            registry, categories, request.CategoryId, request.Provider, cancellationToken);

        return missing.Select(CategoryMapper.ToDto).ToArray();
    }
}

public sealed class EnsureProviderFieldsCommandHandler(
    ProviderRegistry registry,
    ICategoryRepository categories) : IRequestHandler<EnsureProviderFieldsCommand, CategoryDto>
{
    public async Task<CategoryDto> Handle(
        EnsureProviderFieldsCommand request, CancellationToken cancellationToken)
    {
        var (category, missing) = await ProviderFields.ResolveAsync(
            registry, categories, request.CategoryId, request.Provider, cancellationToken);

        if (missing.Count > 0)
        {
            // 只追加缺的。已存在的 key 原封不動——使用者可能改過 Label。
            // 複製新實例，避免把 provider 持有的定義物件交給資料庫層。
            category.Fields.AddRange(missing.Select(f => new CategoryField
            {
                Key = f.Key,
                Label = f.Label,
                Type = f.Type,
                Options = f.Options?.ToList(),
                Required = f.Required,
                Searchable = f.Searchable,
                ShowOnCard = f.ShowOnCard
            }));

            // 系統品類在這裡被 ForbiddenException 擋下，這是正確行為
            await categories.UpdateAsync(category, cancellationToken);
        }

        return CategoryMapper.ToDto(category);
    }
}
```

- [ ] **Step 4: 加上端點**

`src/MyCollection.Api/Endpoints/CategoryEndpoints.cs`，在 `MapDelete` 之後加入：

```csharp
        group.MapGet("/{id}/missing-fields", async (
            string id, string provider, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new MissingProviderFieldsQuery(id, provider), ct)));

        group.MapPost("/{id}/ensure-fields", async (
            string id, EnsureFieldsRequest body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new EnsureProviderFieldsCommand(id, body.Provider), ct)));
```

並在類別末尾加入：

```csharp
    public record EnsureFieldsRequest(string Provider);
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test --filter ProviderFieldsCommandTests`
Expected: `Passed: 5`

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(categories): let custom categories adopt provider fields"
```

---

## 完成後的驗證

- [ ] `dotnet build` 無警告新增
- [ ] `dotnet test` 全綠
- [ ] 未設定 `IGDB_CLIENT_ID` 時 `docker compose up` 正常啟動，`/api/ingest/providers` 不含 `igdb`
- [ ] 設定憑證後上述 Task 13 Step 7 的手動驗證全部通過
- [ ] 補完後檢查任一 Steam 品項：`tags`、`isShowcased`、`acquisition`、`images`、`createdAt`、`name` 皆未變動

## 後續

前端尚未規劃，需另立計畫：

- 新增／編輯品項頁的「從 IGDB 帶入」搜尋 modal（呼叫 `GET /api/ingest/search`）
- 設定頁的批次補完按鈕與結果顯示（呼叫 `POST /api/ingest/enrich/igdb`，沿用既有同步歷程列表）
- 同步歷程列表顯示新的 `skipped` 計數
- 品項詳情頁的單筆重抓按鈕

撰寫前端計畫需要先讀 `web/src/app/features/`、`web/src/app/core/` 下的相關元件與 API 服務，
建議另開 session 以免上下文超載。
