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

        AssertCanonicalCategories(await GetSystemCategoriesAsync());
    }

    [Fact]
    public async Task SeedAsync_is_idempotent_and_preserves_created_at()
    {
        await SystemCategorySeeder.SeedAsync(fixture.Context, _time, CancellationToken.None);
        var first = await GetSystemCategoriesAsync();

        await fixture.Context.Categories.UpdateOneAsync(
            Builders<Category>.Filter.Eq(x => x.Id, SystemCategoryDefinitions.PhysicalGameId),
            Builders<Category>.Update
                .Set(x => x.Name, "已損壞")
                .Set(x => x.Icon, "broken")
                .Set(x => x.Kind, CategoryKind.Digital)
                .Set(x => x.Fields,
                [
                    new CategoryField
                    {
                        Key = "corrupted",
                        Label = "已損壞",
                        Type = FieldType.Bool,
                        Required = true,
                        Searchable = true,
                        ShowOnCard = true
                    }
                ]));

        _time.Advance(TimeSpan.FromHours(1));
        await SystemCategorySeeder.SeedAsync(fixture.Context, _time, CancellationToken.None);
        var second = await GetSystemCategoriesAsync();

        second.Should().HaveCount(4);
        AssertCanonicalCategories(second);
        second.Select(x => x.CreatedAt).Should().Equal(first.Select(x => x.CreatedAt));
        second.Select(x => x.UpdatedAt).Should()
            .OnlyContain(x => x == new DateTime(2026, 7, 28, 7, 0, 0, DateTimeKind.Utc));
    }

    private async Task<List<Category>> GetSystemCategoriesAsync() =>
        await fixture.Context.Categories
            .Find(Builders<Category>.Filter.Eq(x => x.OwnerId, null))
            .SortBy(x => x.Id)
            .ToListAsync();

    private static void AssertCanonicalCategories(IReadOnlyList<Category> actual)
    {
        actual.Should().HaveCount(4);
        AssertCategory(actual[0], "000000000000000000000001", "實體遊戲", "gamepad-2", CategoryKind.Physical,
        [
            Field("platform", "平台", FieldType.Text, searchable: true, showOnCard: true),
            Field("edition", "版本", FieldType.Text, searchable: true, showOnCard: true),
            Field("region", "區域", FieldType.Text, searchable: true),
            Field("mediaFormat", "媒體格式", FieldType.Select, ["光碟", "卡匣", "記憶卡", "其他"], true, true),
            Field("developer", "開發商", FieldType.Text, searchable: true),
            Field("publisher", "發行商", FieldType.Text, searchable: true),
            Field("releaseDate", "發售日期", FieldType.Date),
            Field("productCode", "產品編號", FieldType.Text, searchable: true),
            Field("barcode", "條碼", FieldType.Text, searchable: true),
            Field("condition", "保存狀況", FieldType.Select, ["全新", "近全新", "良好", "普通", "需修復"], true),
            Field("igdbId", "IGDB ID", FieldType.Number),
            Field("genres", "類型", FieldType.Text, searchable: true),
            Field("platforms", "發行平台", FieldType.Text, searchable: true),
            Field("igdbRating", "IGDB 評分", FieldType.Number),
            Field("coverUrl", "IGDB 封面網址", FieldType.Url),
            Field("steamAppId", "Steam App ID", FieldType.Number),
            Field("steamStoreUpdatedAt", "Steam 資料更新時間", FieldType.Date)
        ]);
        AssertCategory(actual[1], "000000000000000000000002", "數位遊戲", "gamepad-2", CategoryKind.Digital,
        [
            Field("platform", "平台／商店", FieldType.Text, searchable: true, showOnCard: true),
            Field("developer", "開發商", FieldType.Text, searchable: true),
            Field("publisher", "發行商", FieldType.Text, searchable: true, showOnCard: true),
            Field("releaseDate", "發售日期", FieldType.Date),
            Field("productCode", "產品編號", FieldType.Text, searchable: true),
            Field("playtimeForever", "遊玩時數（分鐘）", FieldType.Number, showOnCard: true),
            Field("headerUrl", "封面圖網址", FieldType.Url),
            Field("iconUrl", "圖示網址", FieldType.Url),
            Field("igdbId", "IGDB ID", FieldType.Number),
            Field("genres", "類型", FieldType.Text, searchable: true),
            Field("platforms", "發行平台", FieldType.Text, searchable: true),
            Field("igdbRating", "IGDB 評分", FieldType.Number),
            Field("coverUrl", "IGDB 封面網址", FieldType.Url),
            Field("steamAppId", "Steam App ID", FieldType.Number),
            Field("steamStoreUpdatedAt", "Steam 資料更新時間", FieldType.Date)
        ]);
        AssertCategory(actual[2], "000000000000000000000003", "音樂專輯", "disc-3", CategoryKind.Physical,
        [
            Field("artist", "演出者", FieldType.Text, searchable: true, showOnCard: true),
            Field("mediaFormat", "媒體格式", FieldType.Select, ["CD", "黑膠唱片", "卡帶", "SACD", "其他"], true, true),
            Field("albumType", "專輯類型", FieldType.Select, ["專輯", "單曲", "EP", "精選輯", "原聲帶", "其他"], true),
            Field("label", "唱片公司", FieldType.Text, searchable: true, showOnCard: true),
            Field("catalogNumber", "目錄編號", FieldType.Text, searchable: true),
            Field("country", "國家／地區", FieldType.Text, searchable: true),
            Field("releaseDate", "發行日期", FieldType.Date),
            Field("genre", "曲風", FieldType.Text, searchable: true),
            Field("style", "風格", FieldType.Text, searchable: true),
            Field("barcode", "條碼", FieldType.Text, searchable: true)
        ]);
        AssertCategory(actual[3], "000000000000000000000004", "電影光碟", "film", CategoryKind.Physical,
        [
            Field("discFormat", "光碟格式", FieldType.Select, ["Blu-ray", "4K UHD", "DVD", "VCD", "其他"], true, true),
            Field("edition", "版本", FieldType.Text, searchable: true, showOnCard: true),
            Field("director", "導演", FieldType.Text, searchable: true, showOnCard: true),
            Field("studio", "片商", FieldType.Text, searchable: true),
            Field("regionCode", "區碼", FieldType.Text, searchable: true),
            Field("country", "國家／地區", FieldType.Text, searchable: true),
            Field("releaseDate", "發行日期", FieldType.Date),
            Field("genre", "類型", FieldType.Text, searchable: true),
            Field("barcode", "條碼", FieldType.Text, searchable: true)
        ]);
    }

    private static void AssertCategory(
        Category actual,
        string id,
        string name,
        string icon,
        CategoryKind kind,
        IReadOnlyList<ExpectedField> fields)
    {
        actual.Id.ToString().Should().Be(id);
        actual.Name.Should().Be(name);
        actual.Icon.Should().Be(icon);
        actual.Kind.Should().Be(kind);
        actual.Fields.Should().HaveCount(fields.Count);
        for (var index = 0; index < fields.Count; index++)
        {
            var expected = fields[index];
            var field = actual.Fields[index];
            field.Key.Should().Be(expected.Key);
            field.Label.Should().Be(expected.Label);
            field.Type.Should().Be(expected.Type);
            field.Options.Should().Equal(expected.Options);
            field.Required.Should().Be(expected.Required);
            field.Searchable.Should().Be(expected.Searchable);
            field.ShowOnCard.Should().Be(expected.ShowOnCard);
        }
    }

    private static ExpectedField Field(
        string key,
        string label,
        FieldType type,
        IReadOnlyList<string>? options = null,
        bool searchable = false,
        bool showOnCard = false) =>
        new(key, label, type, options, Required: false, searchable, showOnCard);

    private sealed record ExpectedField(
        string Key,
        string Label,
        FieldType Type,
        IReadOnlyList<string>? Options,
        bool Required,
        bool Searchable,
        bool ShowOnCard);
}
