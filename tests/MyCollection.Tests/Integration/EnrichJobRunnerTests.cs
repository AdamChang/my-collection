using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Infrastructure.Providers;
using MyCollection.Infrastructure.Providers.Igdb;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

/// <summary>
/// 本地化補完的主接縫：真 Mongo + 真 SteamProvider，只把商店的 HTTP 換成樁。
/// 這是「工作仍為同步、結果可直接觀察」的最高點，一個接縫涵蓋定址、
/// 欄位擁有權、繁中映射與完成標記。
/// </summary>
[Collection(MongoCollection.Name)]
public class EnrichJobRunnerTests(MongoFixture fixture) : IAsyncLifetime
{
    private const long EldenRing = 1245620;
    private const long Cyberpunk = 1091500;

    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId CategoryId = ObjectId.GenerateNewId();
    private static readonly DateTime CreatedAt = new(2026, 7, 1, 3, 0, 0, DateTimeKind.Utc);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 4, 3, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        await fixture.Context.Categories.InsertOneAsync(new Category
        {
            Id = CategoryId,
            OwnerId = Owner,
            Name = "數位遊戲",
            Kind = CategoryKind.Digital,
            Fields = SteamFields.Create(),
            CreatedAt = CreatedAt,
            UpdatedAt = CreatedAt
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---- 繁體中文映射 ----

    [Fact]
    public async Task Replaces_the_english_name_description_and_genres_with_traditional_chinese()
    {
        var itemId = await InsertSteamItemAsync(EldenRing, "ELDEN RING");

        await RunAsync(StoreStub());

        var item = await LoadAsync(itemId);
        item.Name.Should().Be("艾爾登法環");
        item.Description.Should().NotBeNullOrWhiteSpace();
        item.Attributes["genres"].AsString.Should().Be("動作, 角色扮演");
    }

    /// <summary>
    /// 沒有官方繁中版時 Steam 自己回傳原文——退回邏輯在 Valve 那端，我們不寫也不判斷。
    /// 這一案是「不需要自寫 fallback」這個決定的證據；它若開始失敗，代表商店行為變了。
    /// </summary>
    [Fact]
    public async Task Keeps_the_original_name_when_the_store_has_no_localized_title()
    {
        var itemId = await InsertSteamItemAsync(Cyberpunk, "Cyberpunk 2077");

        var job = await RunAsync(StoreStub());

        var item = await LoadAsync(itemId);
        item.Name.Should().Be("Cyberpunk 2077");
        item.Attributes["genres"].AsString.Should().Be("角色扮演", "類型一律本地化，即使品名沒有");
        job.Status.Should().Be(SyncStatus.Succeeded, "拿到原文不是失敗");
        job.Failed.Should().Be(0);
    }

    [Fact]
    public async Task Stamps_the_completion_marker_and_the_app_id()
    {
        var itemId = await InsertSteamItemAsync(EldenRing, "ELDEN RING");

        await RunAsync(StoreStub());

        var item = await LoadAsync(itemId);
        item.Attributes[SteamFields.AppIdKey].ToInt64().Should().Be(EldenRing);
        item.Attributes[SteamFields.StoreUpdatedAtKey].ToUniversalTime()
            .Should().Be(_time.GetUtcNow().UtcDateTime);
    }

    // ---- 欄位擁有權 ----

    [Fact]
    public async Task Overwrites_a_description_that_the_user_already_had()
    {
        var itemId = await InsertSteamItemAsync(EldenRing, "ELDEN RING", "An action RPG.");

        await RunAsync(StoreStub());

        (await LoadAsync(itemId)).Description.Should().NotBe(
            "An action RPG.", "本地化補完擁有它寫的每一個欄位");
    }

    // ---- 定址 ----

    /// <summary>
    /// 手動建檔的實體遊戲沒有 externalRef，靠 IGDB 反查寫進來的 steamAppId 定址。
    /// 完成標記是另一個欄位，所以它仍會出現在批次候選裡。
    /// </summary>
    [Fact]
    public async Task Addresses_an_item_that_has_only_an_app_id_and_no_external_ref()
    {
        var itemId = ObjectId.GenerateNewId();
        await fixture.Context.Items.InsertOneAsync(new Item
        {
            Id = itemId,
            OwnerId = Owner,
            CategoryId = CategoryId,
            Name = "ELDEN RING",
            Source = ItemSource.Manual,
            ExternalRef = new ExternalRef
            {
                Provider = ProviderKeys.Igdb, ExternalId = "119133", LastSyncedAt = CreatedAt
            },
            Attributes = new BsonDocument { { SteamFields.AppIdKey, EldenRing } },
            CreatedAt = CreatedAt,
            UpdatedAt = CreatedAt
        });

        await RunAsync(StoreStub());

        (await LoadAsync(itemId)).Name.Should().Be("艾爾登法環");
    }

    [Fact]
    public async Task Leaves_items_that_already_carry_the_completion_marker_out_of_the_batch()
    {
        var itemId = await InsertSteamItemAsync(
            EldenRing, "ELDEN RING",
            attributes: new BsonDocument { { SteamFields.StoreUpdatedAtKey, CreatedAt } });

        var job = await RunAsync(StoreStub());

        (await LoadAsync(itemId)).Name.Should().Be("ELDEN RING", "補過的品項不該被重抓");
        job.Updated.Should().Be(0);
    }

    // ---- 失敗與查無 ----

    [Fact]
    public async Task Counts_an_unlisted_app_as_skipped_not_failed()
    {
        await InsertSteamItemAsync(999999, "下架了");

        var job = await RunAsync(StoreStub());

        job.Skipped.Should().Be(1);
        job.Failed.Should().Be(0);
        job.Status.Should().Be(SyncStatus.Succeeded);
    }

    [Fact]
    public async Task Skips_a_psn_trophy_title_when_igdb_cannot_resolve_its_external_id()
    {
        var itemId = ObjectId.GenerateNewId();
        await fixture.Context.Items.InsertOneAsync(new Item
        {
            Id = itemId,
            OwnerId = Owner,
            CategoryId = CategoryId,
            Name = "PSN trophy title",
            Source = ItemSource.Psn,
            ExternalRef = new ExternalRef
            {
                Provider = ProviderKeys.Psn,
                ExternalId = "NPWR12345_00",
                LastSyncedAt = CreatedAt
            },
            CreatedAt = CreatedAt,
            UpdatedAt = CreatedAt
        });

        var job = await RunAsync(CreateIgdbProvider());

        var item = await LoadAsync(itemId);
        item.Source.Should().Be(ItemSource.Psn);
        item.ExternalRef!.Provider.Should().Be("psn");
        job.Skipped.Should().Be(1);
        job.Failed.Should().Be(0);
        job.Status.Should().Be(SyncStatus.Succeeded);
    }

    [Fact]
    public async Task Records_a_store_failure_per_item_and_still_finishes_the_rest()
    {
        await InsertSteamItemAsync(EldenRing, "ELDEN RING");
        var brokenId = await InsertSteamItemAsync(Cyberpunk, "Cyberpunk 2077");

        var job = await RunAsync(StoreStub(failFor: Cyberpunk));

        job.Failed.Should().Be(1);
        job.Updated.Should().Be(1);
        job.Status.Should().Be(SyncStatus.Succeeded, "單筆失敗不該讓整批作業失敗");
        (await LoadAsync(brokenId)).Name.Should().Be("Cyberpunk 2077");
    }

    // ---- helpers ----

    private Task<SyncJob> RunAsync(StubHttpMessageHandler storeHandler) =>
        RunAsync(CreateProvider(storeHandler));

    private async Task<SyncJob> RunAsync(IExternalIdLookupProvider provider)
    {
        var userContext = new FixedUserContext(Owner);
        var jobs = new MongoSyncJobRepository(fixture.Context, userContext);

        var job = new SyncJob
        {
            Id = ObjectId.GenerateNewId(),
            Provider = provider.Key,
            Status = SyncStatus.Running,
            StartedAt = _time.GetUtcNow().UtcDateTime
        };
        await jobs.InsertAsync(job, CancellationToken.None);

        var runner = new EnrichJobRunner(
            new MongoItemRepository(fixture.Context, userContext),
            new MongoCategoryRepository(fixture.Context, userContext),
            jobs,
            new MongoItemEnrichWriter(fixture.Context),
            userContext,
            _time);

        return await runner.RunAsync(job, provider, null, 50, CancellationToken.None);
    }

    private SteamProvider CreateProvider(StubHttpMessageHandler storeHandler)
    {
        // 節流間隔 0：測試不該真的等 1.5 秒
        var options = Options.Create(new SteamOptions { StoreMinRequestIntervalMs = 0 });

        return new SteamProvider(
            StubHttpMessageHandler.Json("{}").CreateClient("https://api.steampowered.com/"),
            new SteamStoreClient(
                storeHandler.CreateClient("https://store.steampowered.com/"),
                new SteamStoreRateLimiter(options, TimeProvider.System),
                options),
            Mock.Of<ISecretProtector>(),
            _time,
            NullLogger<SteamProvider>.Instance);
    }

    private IgdbProvider CreateIgdbProvider()
    {
        var options = Options.Create(new IgdbOptions
        {
            ClientId = "cid",
            ClientSecret = "csecret",
            MinRequestIntervalMs = 0,
            LookupBatchSize = 10
        });

        return new IgdbProvider(
            StubHttpMessageHandler.Json("[]").CreateClient("https://api.igdb.com/v4/"),
            Mock.Of<ITwitchTokenProvider>(),
            new IgdbRateLimiter(options, _time),
            options,
            NullLogger<IgdbProvider>.Instance);
    }

    /// <summary>依 appid 回放錄下來的商店回應；未錄的 appid 回 success:false。</summary>
    private static StubHttpMessageHandler StoreStub(long? failFor = null) =>
        new(request =>
        {
            var appId = request.RequestUri!.Query
                .Split('&')
                .First(part => part.Contains("appids="))
                .Split('=')[1];

            if (failFor is { } broken && appId == broken.ToString())
            {
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            }

            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", $"steam-appdetails-{appId}.json");
            var body = File.Exists(path)
                ? File.ReadAllText(path)
                : $"{{\"{appId}\":{{\"success\":false}}}}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
        });

    private async Task<ObjectId> InsertSteamItemAsync(
        long appId, string name, string? description = null, BsonDocument? attributes = null)
    {
        var id = ObjectId.GenerateNewId();

        await fixture.Context.Items.InsertOneAsync(new Item
        {
            Id = id,
            OwnerId = Owner,
            CategoryId = CategoryId,
            Name = name,
            Description = description,
            Source = ItemSource.Steam,
            ExternalRef = new ExternalRef
            {
                Provider = ProviderKeys.Steam,
                ExternalId = appId.ToString(),
                LastSyncedAt = CreatedAt
            },
            Attributes = attributes ?? [],
            CreatedAt = CreatedAt,
            UpdatedAt = CreatedAt
        });

        return id;
    }

    private Task<Item> LoadAsync(ObjectId id) =>
        fixture.Context.Items.Find(Builders<Item>.Filter.Eq(x => x.Id, id)).FirstAsync();
}
