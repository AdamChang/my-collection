using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Providers;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Unit;

public class SteamProviderTests
{
    private readonly Mock<ISecretProtector> _protector = new();

    public SteamProviderTests() =>
        _protector.Setup(p => p.Unprotect("protected")).Returns("real-api-key");

    private static ExternalAccount Account() => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = ObjectId.GenerateNewId(),
        Provider = "steam",
        ExternalUserId = "76561197960287930",
        ProtectedApiKey = "protected",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private SteamProvider CreateSut(StubHttpMessageHandler handler) =>
        new(handler.CreateClient("https://api.steampowered.com/"),
            _protector.Object,
            NullLogger<SteamProvider>.Instance);

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "steam-getownedgames.json"));

    [Fact]
    public void Declares_bulk_sync_capability_only()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json("{}"));

        sut.Key.Should().Be("steam");
        ProviderCapabilities.Of(sut).Should().Be(ProviderCapability.BulkSync);
    }

    [Fact]
    public async Task Sync_maps_every_game_from_the_recorded_response()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json(Fixture()));

        var items = await sut.SyncAsync(Account(), CancellationToken.None);

        items.Should().HaveCount(3);
        var tf2 = items.Single(i => i.ExternalId == "440");
        tf2.Name.Should().Be("Team Fortress 2");
        tf2.Attributes["playtimeForever"].Should().Be(1234);
        tf2.Attributes["iconUrl"].Should().Be(
            "https://media.steampowered.com/steamcommunity/public/images/apps/440/e3f595a92552da3d664ad00277fad2107345f743.jpg");
        tf2.ImageUrl!.ToString().Should().Be("https://cdn.cloudflare.steamstatic.com/steam/apps/440/header.jpg");
        tf2.SourceUrl!.ToString().Should().Be("https://store.steampowered.com/app/440");
    }

    [Fact]
    public async Task Sync_omits_icon_url_when_steam_returns_none()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json(Fixture()));

        var items = await sut.SyncAsync(Account(), CancellationToken.None);

        items.Single(i => i.ExternalId == "292030").Attributes.Should().NotContainKey("iconUrl");
    }

    [Fact]
    public async Task Sync_sends_the_decrypted_key_and_steam_id()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());

        await CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        var query = handler.Requests.Single().Query;
        query.Should().Contain("key=real-api-key");
        query.Should().Contain("steamid=76561197960287930");
        query.Should().Contain("include_appinfo=1");
    }

    [Fact]
    public async Task Sync_returns_empty_when_profile_is_private()
    {
        // Steam 對隱私設定為私人的帳號回傳空的 response 物件
        var sut = CreateSut(StubHttpMessageHandler.Json("""{ "response": {} }"""));

        (await sut.SyncAsync(Account(), CancellationToken.None)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Sync_wraps_http_failures_in_ProviderException(HttpStatusCode status)
    {
        var sut = CreateSut(StubHttpMessageHandler.Status(status));

        var act = () => sut.SyncAsync(Account(), CancellationToken.None);

        (await act.Should().ThrowAsync<ProviderException>()).Which.ProviderKey.Should().Be("steam");
    }

    [Fact]
    public async Task Sync_wraps_malformed_json_in_ProviderException()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json("not json"));

        var act = () => sut.SyncAsync(Account(), CancellationToken.None);

        await act.Should().ThrowAsync<ProviderException>();
    }
}
