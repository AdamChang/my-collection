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
