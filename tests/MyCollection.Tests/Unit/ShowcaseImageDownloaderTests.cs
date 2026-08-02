using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Imaging;

namespace MyCollection.Tests.Unit;

public class ShowcaseImageDownloaderTests
{
    [Fact]
    public void Uses_the_igdb_cover_when_there_is_no_steam_header()
    {
        var item = CreateItem(new BsonDocument
        {
            { "coverUrl", "https://images.igdb.com/a.jpg" },
            { "iconUrl", "https://cdn.steam/icon.jpg" }
        });

        ShowcaseImageDownloader.ResolveSourceUrl(item).Should().Be(new Uri("https://images.igdb.com/a.jpg"));
    }

    [Fact]
    public void Prefers_the_steam_header_over_the_igdb_cover()
    {
        var item = CreateItem(new BsonDocument
        {
            { "coverUrl", "https://images.igdb.com/a.jpg" },
            { "headerUrl", "https://cdn.steam/header.jpg" }
        });

        ShowcaseImageDownloader.ResolveSourceUrl(item).Should().Be(new Uri("https://cdn.steam/header.jpg"));
    }

    [Fact]
    public void Falls_back_to_the_icon_when_it_is_the_only_url()
    {
        var item = CreateItem(new BsonDocument("iconUrl", "https://cdn.steam/icon.jpg"));

        ShowcaseImageDownloader.ResolveSourceUrl(item).Should().Be(new Uri("https://cdn.steam/icon.jpg"));
    }

    [Fact]
    public void Returns_null_when_no_attribute_holds_an_absolute_url()
    {
        var item = CreateItem(new BsonDocument("coverUrl", "不是網址"));

        ShowcaseImageDownloader.ResolveSourceUrl(item).Should().BeNull();
    }

    private static Item CreateItem(BsonDocument attributes) => new()
    {
        Name = "Test item",
        Attributes = attributes
    };
}
