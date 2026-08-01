using FluentAssertions;
using Moq;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class FetchByUrlQueryTests
{
    private readonly Mock<IUrlLookupProvider> _opengraph = new();

    public FetchByUrlQueryTests()
    {
        _opengraph.SetupGet(p => p.Key).Returns("opengraph");
    }

    private FetchByUrlQueryHandler CreateSut() => new(new ProviderRegistry([_opengraph.Object]));

    [Fact]
    public async Task Returns_mapped_metadata()
    {
        _opengraph.Setup(p => p.FetchByUrlAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalItem(
                "https://x/1", "初音ミク", "描述", new Uri("https://cdn/a.jpg"),
                new Dictionary<string, object?> { ["siteName"] = "GSC" }));

        var result = await CreateSut().Handle(
            new FetchByUrlQuery("https://x/1", "opengraph"), CancellationToken.None);

        result.Name.Should().Be("初音ミク");
        result.Description.Should().Be("描述");
        result.ImageUrl.Should().Be("https://cdn/a.jpg");
        result.Attributes["siteName"].Should().Be("GSC");
    }

    [Fact]
    public async Task Throws_NotFound_when_provider_finds_nothing()
    {
        _opengraph.Setup(p => p.FetchByUrlAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalItem?)null);

        var act = () => CreateSut().Handle(new FetchByUrlQuery("https://x/1", "opengraph"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void Validator_rejects_non_http_urls()
    {
        var validator = new FetchByUrlQueryValidator();

        validator.Validate(new FetchByUrlQuery("not a url", "opengraph")).IsValid.Should().BeFalse();
        validator.Validate(new FetchByUrlQuery("file:///etc/passwd", "opengraph")).IsValid.Should().BeFalse();
        validator.Validate(new FetchByUrlQuery("ftp://x/y", "opengraph")).IsValid.Should().BeFalse();
        validator.Validate(new FetchByUrlQuery("https://x/1", "opengraph")).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Defaults_to_the_opengraph_provider()
    {
        _opengraph.Setup(p => p.FetchByUrlAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalItem("x", "X", null, null, new Dictionary<string, object?>()));

        await CreateSut().Handle(new FetchByUrlQuery("https://x/1"), CancellationToken.None);

        _opengraph.Verify(p => p.FetchByUrlAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
