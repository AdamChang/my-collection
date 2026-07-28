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
