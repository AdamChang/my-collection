using System.Net;
using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Providers;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Unit;

public class OpenGraphProviderTests
{
    private static readonly Uri ProductUrl = new("https://www.goodsmile.com/product/12345");

    private static OpenGraphProvider CreateSut(StubHttpMessageHandler handler) => new(new HttpClient(handler));

    [Fact]
    public void Declares_url_lookup_capability_only()
    {
        var sut = CreateSut(StubHttpMessageHandler.Html("<html></html>"));

        sut.Key.Should().Be("opengraph");
        sut.Capabilities.Should().Be(ProviderCapability.UrlLookup);
    }

    [Fact]
    public async Task Extracts_open_graph_tags()
    {
        var sut = CreateSut(StubHttpMessageHandler.Html("""
            <html><head>
              <meta property="og:title" content="初音ミク 1/8 スケール" />
              <meta property="og:description" content="グッドスマイルカンパニー製" />
              <meta property="og:image" content="https://cdn.goodsmile.com/12345.jpg" />
              <meta property="og:site_name" content="GOOD SMILE COMPANY" />
            </head><body></body></html>
            """));

        var item = await sut.FetchByUrlAsync(ProductUrl, CancellationToken.None);

        item.Should().NotBeNull();
        item!.Name.Should().Be("初音ミク 1/8 スケール");
        item.Description.Should().Be("グッドスマイルカンパニー製");
        item.ImageUrl!.ToString().Should().Be("https://cdn.goodsmile.com/12345.jpg");
        item.Attributes["siteName"].Should().Be("GOOD SMILE COMPANY");
        item.SourceUrl.Should().Be(ProductUrl);
        item.ExternalId.Should().Be(ProductUrl.ToString());
    }

    [Fact]
    public async Task Falls_back_to_title_tag_when_og_title_missing()
    {
        var sut = CreateSut(StubHttpMessageHandler.Html(
            "<html><head><title>備用標題</title></head><body></body></html>"));

        var item = await sut.FetchByUrlAsync(ProductUrl, CancellationToken.None);

        item!.Name.Should().Be("備用標題");
        item.ImageUrl.Should().BeNull();
    }

    [Fact]
    public async Task Resolves_relative_image_url_against_the_page()
    {
        var sut = CreateSut(StubHttpMessageHandler.Html("""
            <html><head>
              <meta property="og:title" content="X" />
              <meta property="og:image" content="/img/a.jpg" />
            </head></html>
            """));

        var item = await sut.FetchByUrlAsync(ProductUrl, CancellationToken.None);

        item!.ImageUrl!.ToString().Should().Be("https://www.goodsmile.com/img/a.jpg");
    }

    [Fact]
    public async Task Returns_null_when_no_usable_title()
    {
        var sut = CreateSut(StubHttpMessageHandler.Html("<html><head></head><body>hi</body></html>"));

        (await sut.FetchByUrlAsync(ProductUrl, CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Wraps_http_failures_in_ProviderException(HttpStatusCode status)
    {
        var sut = CreateSut(StubHttpMessageHandler.Status(status));

        var act = () => sut.FetchByUrlAsync(ProductUrl, CancellationToken.None);

        (await act.Should().ThrowAsync<ProviderException>()).Which.ProviderKey.Should().Be("opengraph");
    }

    [Fact]
    public async Task Sync_is_not_supported()
    {
        var sut = CreateSut(StubHttpMessageHandler.Html("<html></html>"));
        var account = new ExternalAccount
        {
            Id = ObjectId.GenerateNewId(), OwnerId = ObjectId.GenerateNewId(),
            Provider = "opengraph", ExternalUserId = "x", ProtectedApiKey = "x"
        };

        var act = () => sut.SyncAsync(account, CancellationToken.None);

        await act.Should().ThrowAsync<ProviderException>();
    }
}
