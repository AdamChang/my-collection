using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoItemSyncWriterTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId GameCategory = ObjectId.GenerateNewId();
    private static readonly DateTime SyncedAt = new(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc);

    private MongoItemSyncWriter _sut = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _sut = new MongoItemSyncWriter(fixture.Context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static IReadOnlyList<ExternalItem> SteamPayload(string tf2Name = "Team Fortress 2") =>
    [
        new ExternalItem("440", tf2Name, null, new Uri("https://cdn/440.jpg"),
            new Dictionary<string, object?> { ["playtimeForever"] = 1234, ["headerUrl"] = "https://cdn/440.jpg" })
        {
            SourceUrl = new Uri("https://store.steampowered.com/app/440")
        },
        new ExternalItem("620", "Portal 2", null, new Uri("https://cdn/620.jpg"),
            new Dictionary<string, object?> { ["playtimeForever"] = 0, ["headerUrl"] = "https://cdn/620.jpg" })
    ];

    private Task<SyncOutcome> SyncAsync(IReadOnlyList<ExternalItem> payload, DateTime? at = null) =>
        _sut.UpsertAsync(Owner, GameCategory, ItemSource.Steam, "steam", payload, at ?? SyncedAt, CancellationToken.None);

    private Task<Item> LoadAsync(string externalId) =>
        fixture.Context.Items
            .Find(Builders<Item>.Filter.Eq("externalRef.externalId", externalId))
            .FirstAsync();

    [Fact]
    public async Task First_run_creates_every_item()
    {
        var outcome = await SyncAsync(SteamPayload());

        outcome.Created.Should().Be(2);
        outcome.Updated.Should().Be(0);
        outcome.Failed.Should().Be(0);

        var tf2 = await LoadAsync("440");
        tf2.OwnerId.Should().Be(Owner);
        tf2.CategoryId.Should().Be(GameCategory);
        tf2.Source.Should().Be(ItemSource.Steam);
        tf2.IsShowcased.Should().BeFalse("同步進來的品項一律不是精選");
        tf2.Tags.Should().BeEmpty();
        tf2.Acquisition.Should().BeNull();
        tf2.Images.Should().BeEmpty("圖片延遲下載，同步時不落地");
        tf2.Attributes["playtimeForever"].AsInt32.Should().Be(1234);
        tf2.ExternalRef!.Provider.Should().Be("steam");
        tf2.ExternalRef.LastSyncedAt.Should().Be(SyncedAt);
        tf2.CreatedAt.Should().Be(SyncedAt);
    }

    [Fact]
    public async Task Second_run_of_the_same_payload_creates_nothing()
    {
        await SyncAsync(SteamPayload());

        var outcome = await SyncAsync(SteamPayload());

        outcome.Created.Should().Be(0);
        outcome.Updated.Should().Be(2);
        (await fixture.Context.Items.CountDocumentsAsync(FilterDefinition<Item>.Empty)).Should().Be(2);
    }

    [Fact]
    public async Task Second_run_never_overwrites_manual_edits()
    {
        await SyncAsync(SteamPayload());

        // 使用者手動設為精選、加標籤、填購入資訊
        var tf2 = await LoadAsync("440");
        await fixture.Context.Items.UpdateOneAsync(
            Builders<Item>.Filter.Eq(x => x.Id, tf2.Id),
            Builders<Item>.Update
                .Set(x => x.IsShowcased, true)
                .Set(x => x.Tags, ["最愛", "FPS"])
                .Set(x => x.Acquisition, new Acquisition { Vendor = "Steam 特賣", Price = new Money(99m, "TWD") }));

        await SyncAsync(SteamPayload(tf2Name: "Team Fortress 2 (Updated)"),
            at: SyncedAt.AddDays(1));

        var reloaded = await LoadAsync("440");
        reloaded.IsShowcased.Should().BeTrue("$setOnInsert 保護使用者設定");
        reloaded.Tags.Should().BeEquivalentTo("最愛", "FPS");
        reloaded.Acquisition!.Vendor.Should().Be("Steam 特賣");
        reloaded.CreatedAt.Should().Be(SyncedAt, "createdAt 只在建立時寫入");

        reloaded.Attributes["playtimeForever"].ToInt32().Should().Be(1234, "provider 擁有的欄位仍會更新");
        reloaded.ExternalRef!.LastSyncedAt.Should().Be(SyncedAt.AddDays(1));
    }

    /// <summary>
    /// 這是繁體中文品名唯一的護欄。name 若被改回 $set，
    /// 商店補完寫好的「艾爾登法環」會在下一次同步被默默改回 "ELDEN RING"——
    /// 沒有錯誤訊息、沒有失敗的作業，只有資料悄悄退回英文。
    /// </summary>
    [Fact]
    public async Task Sync_never_overwrites_an_existing_name()
    {
        await SyncAsync(SteamPayload());

        var tf2 = await LoadAsync("440");
        await fixture.Context.Items.UpdateOneAsync(
            Builders<Item>.Filter.Eq(x => x.Id, tf2.Id),
            Builders<Item>.Update.Set(x => x.Name, "絕地要塞 2"));

        await SyncAsync(SteamPayload(tf2Name: "Team Fortress 2"), at: SyncedAt.AddDays(1));

        (await LoadAsync("440")).Name.Should().Be(
            "絕地要塞 2", "name 的擁有者是補完，同步只在建立品項時寫入");
    }

    [Fact]
    public async Task Sync_never_touches_another_owners_item_with_the_same_external_id()
    {
        var otherOwner = ObjectId.GenerateNewId();
        await _sut.UpsertAsync(otherOwner, GameCategory, ItemSource.Steam, "steam", SteamPayload("別人的 TF2"), SyncedAt, CancellationToken.None);

        await SyncAsync(SteamPayload());

        var all = await fixture.Context.Items
            .Find(Builders<Item>.Filter.Eq("externalRef.externalId", "440")).ToListAsync();

        all.Should().HaveCount(2, "唯一索引是 (ownerId, provider, externalId) 複合鍵");
        all.Should().Contain(i => i.OwnerId == otherOwner && i.Name == "別人的 TF2");
    }

    [Fact]
    public async Task Empty_payload_is_a_no_op()
    {
        var outcome = await SyncAsync([]);

        outcome.Created.Should().Be(0);
        outcome.Updated.Should().Be(0);
    }

    [Fact]
    public async Task Duplicate_external_ids_in_one_payload_are_deduplicated()
    {
        var payload = SteamPayload().Concat(SteamPayload()).ToArray();

        var outcome = await SyncAsync(payload);

        outcome.Created.Should().Be(2);
        outcome.Failed.Should().Be(0, "同一批次內重複的 externalId 會被去重，不算失敗");
        (await fixture.Context.Items.CountDocumentsAsync(FilterDefinition<Item>.Empty)).Should().Be(2);
    }
}
