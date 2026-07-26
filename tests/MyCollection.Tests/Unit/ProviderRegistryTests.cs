using FluentAssertions;
using Moq;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class ProviderRegistryTests
{
    private static IMetadataProvider Provider(string key, ProviderCapability capabilities)
    {
        var mock = new Mock<IMetadataProvider>();
        mock.SetupGet(p => p.Key).Returns(key);
        mock.SetupGet(p => p.Capabilities).Returns(capabilities);
        return mock.Object;
    }

    private static ProviderRegistry CreateSut() => new(
    [
        Provider("steam", ProviderCapability.BulkSync),
        Provider("opengraph", ProviderCapability.UrlLookup)
    ]);

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
    public void RequireCapability_throws_when_provider_lacks_it()
    {
        var act = () => CreateSut().Require("opengraph", ProviderCapability.BulkSync);

        act.Should().Throw<ProviderException>()
            .Which.ProviderKey.Should().Be("opengraph");
    }

    [Fact]
    public void RequireCapability_passes_when_supported()
    {
        CreateSut().Require("opengraph", ProviderCapability.UrlLookup).Key.Should().Be("opengraph");
    }

    [Fact]
    public void Lists_all_registered_providers_with_capabilities()
    {
        var all = CreateSut().All;

        all.Select(p => p.Key).Should().BeEquivalentTo("steam", "opengraph");
    }
}
