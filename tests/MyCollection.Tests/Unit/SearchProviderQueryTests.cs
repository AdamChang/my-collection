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
