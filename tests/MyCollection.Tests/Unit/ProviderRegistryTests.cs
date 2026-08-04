using FluentAssertions;
using Moq;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class ProviderRegistryTests
{
    private static IMetadataProvider BulkSync(string key)
    {
        var mock = new Mock<IBulkSyncProvider>();
        mock.SetupGet(p => p.Key).Returns(key);
        return mock.Object;
    }

    private static IMetadataProvider UrlLookup(string key)
    {
        var mock = new Mock<IUrlLookupProvider>();
        mock.SetupGet(p => p.Key).Returns(key);
        return mock.Object;
    }

    private static IMetadataProvider Search(string key)
    {
        var mock = new Mock<ISearchProvider>();
        mock.SetupGet(p => p.Key).Returns(key);
        return mock.Object;
    }

    private static ProviderRegistry CreateSut() =>
        new([BulkSync("steam"), UrlLookup("opengraph"), Search("igdb")]);

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
    public void Generic_Require_returns_the_provider_when_the_capability_interface_matches()
    {
        CreateSut().Require<ISearchProvider>("igdb").Key.Should().Be("igdb");
    }

    [Fact]
    public void Generic_Require_throws_ProviderException_when_the_interface_does_not_match()
    {
        var act = () => CreateSut().Require<IBulkSyncProvider>("opengraph");

        act.Should().Throw<ProviderException>()
            .Which.ProviderKey.Should().Be("opengraph");
    }

    [Fact]
    public void Generic_Require_still_throws_NotFoundException_for_an_unknown_key()
    {
        var act = () => CreateSut().Require<ISearchProvider>("psn");

        act.Should().Throw<NotFoundException>();
    }

    [Fact]
    public void Lists_all_registered_providers()
    {
        CreateSut().All.Select(p => p.Key).Should().BeEquivalentTo("steam", "opengraph", "igdb");
    }

    [Theory]
    [InlineData("steam", ProviderCapability.BulkSync)]
    [InlineData("opengraph", ProviderCapability.UrlLookup)]
    // 可搜尋的 provider 必然也可依識別碼反查——ISearchProvider 繼承 IExternalIdLookupProvider
    [InlineData("igdb", ProviderCapability.Search | ProviderCapability.Enrich)]
    public void Derives_capabilities_from_the_implemented_interfaces(string key, ProviderCapability expected)
    {
        ProviderCapabilities.Of(CreateSut().Require(key)).Should().Be(expected);
    }

    [Fact]
    public void Derives_combined_capabilities_when_one_provider_implements_two_interfaces()
    {
        var mock = new Mock<IMetadataProvider>();
        mock.SetupGet(p => p.Key).Returns("hybrid");
        mock.As<IBulkSyncProvider>();
        mock.As<IUrlLookupProvider>();

        ProviderCapabilities.Of(mock.Object).Should()
            .Be(ProviderCapability.BulkSync | ProviderCapability.UrlLookup);
    }
}
