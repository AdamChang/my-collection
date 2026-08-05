using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

/// <summary>
/// 同步改用 aggregation pipeline 更新之後，payload 常數會與欄位路徑共用同一個語法空間。
/// 這裡守的是 pipeline 專屬的風險，不是同步語意——語意在 MongoItemSyncWriterTests。
/// </summary>
[Collection(MongoCollection.Name)]
public class MongoItemSyncWriterPipelineTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId GameCategory = ObjectId.GenerateNewId();
    private static readonly DateTime SyncedAt = new(2026, 8, 5, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LastPlayedAt = new(2026, 3, 14, 15, 9, 26, DateTimeKind.Utc);

    private MongoItemSyncWriter _sut = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _sut = new MongoItemSyncWriter(fixture.Context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<SyncOutcome> SyncAsync(IReadOnlyList<ExternalItem> payload, DateTime? at = null) =>
        _sut.UpsertAsync(Owner, GameCategory, ItemSource.Psn, "psn", payload, at ?? SyncedAt, CancellationToken.None);

    private Task<Item> LoadAsync(string externalId) =>
        fixture.Context.Items
            .Find(Builders<Item>.Filter.Eq("externalRef.externalId", externalId))
            .FirstAsync();

    /// <summary>
    /// Pipeline 會把 '$' 開頭的字串解讀為欄位路徑。少了 $literal，這些值不會拋錯，
    /// 而是被解析成不存在的欄位並靜默寫成別的東西——所以要用真實的 Mongo 驗。
    /// </summary>
    [Fact]
    public async Task Sync_writes_dollar_prefixed_payload_as_literal_text()
    {
        await SyncAsync(
        [
            new ExternalItem(
                "NPWR00001_00",
                "$1 Ride",
                "$notAFieldPath",
                new Uri("https://cdn/icon.png"),
                new Dictionary<string, object?>
                {
                    ["iconUrl"] = "$alsoNotAFieldPath",
                    ["platform"] = "$PS5"
                })
            {
                FillOnlyIfAbsent = new HashSet<string>(["platform"], StringComparer.Ordinal)
            }
        ]);

        var item = await LoadAsync("NPWR00001_00");

        item.Name.Should().Be("$1 Ride");
        item.Description.Should().Be("$notAFieldPath");
        item.Attributes["iconUrl"].AsString.Should().Be("$alsoNotAFieldPath");
        item.Attributes["platform"].AsString.Should().Be("$PS5", "軟寫入分支的 payload 同樣要包成 literal");
    }

    /// <summary>PSN 的 psnLastPlayedAt 是唯一走這條 pipeline 的日期型別 payload。</summary>
    [Fact]
    public async Task Sync_round_trips_a_date_attribute()
    {
        await SyncAsync(
        [
            new ExternalItem(
                "NPWR00002_00",
                "Bloodborne",
                null,
                new Uri("https://cdn/icon.png"),
                new Dictionary<string, object?>
                {
                    ["psnProgress"] = 87,
                    ["psnLastPlayedAt"] = LastPlayedAt
                })
        ]);

        var item = await LoadAsync("NPWR00002_00");

        item.Attributes["psnLastPlayedAt"].BsonType.Should().Be(BsonType.DateTime);
        item.Attributes["psnLastPlayedAt"].ToUniversalTime().Should().Be(LastPlayedAt);
        item.Attributes["psnProgress"].AsInt32.Should().Be(87);
    }

    /// <summary>
    /// $ifNull 取代 $setOnInsert 之後，使用者欄位的保留條件從「文件是否為新建」
    /// 變成「欄位是否為 null」。true 與非空陣列必須原樣留存。
    /// </summary>
    [Fact]
    public async Task Second_sync_preserves_existing_user_owned_values()
    {
        IReadOnlyList<ExternalItem> payload =
        [
            new ExternalItem(
                "NPWR00003_00",
                "Returnal",
                null,
                new Uri("https://cdn/icon.png"),
                new Dictionary<string, object?> { ["psnProgress"] = 12 })
        ];

        await SyncAsync(payload);

        var created = await LoadAsync("NPWR00003_00");
        await fixture.Context.Items.UpdateOneAsync(
            Builders<Item>.Filter.Eq(x => x.Id, created.Id),
            Builders<Item>.Update
                .Set(x => x.IsShowcased, true)
                .Set(x => x.Tags, ["已破台"]));

        await SyncAsync(payload, at: SyncedAt.AddDays(1));

        var item = await LoadAsync("NPWR00003_00");
        item.IsShowcased.Should().BeTrue();
        item.Tags.Should().Equal("已破台");
        item.CreatedAt.Should().Be(SyncedAt, "createdAt 不得被第二次同步改寫");
    }
}
