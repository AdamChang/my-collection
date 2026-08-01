using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Providers.Igdb;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Unit;

public class TwitchTokenProviderTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 1, 3, 0, 0, TimeSpan.Zero));

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "twitch-token.json"));

    private TwitchTokenProvider CreateSut(StubHttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(TwitchTokenProvider.HttpClientName))
            .Returns(() => handler.CreateClient("https://id.twitch.tv/"));

        return new TwitchTokenProvider(
            factory.Object,
            Options.Create(new IgdbOptions { ClientId = "cid", ClientSecret = "csecret" }),
            _time);
    }

    [Fact]
    public async Task Fetches_the_token_on_the_first_call()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());

        var token = await CreateSut(handler).GetAsync(CancellationToken.None);

        token.Should().Be("abcdefghijklmnopqrstuvwxyz1234");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Sends_the_client_credentials_grant()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());

        await CreateSut(handler).GetAsync(CancellationToken.None);

        var query = handler.Requests.Single().Query;
        query.Should().Contain("client_id=cid");
        query.Should().Contain("client_secret=csecret");
        query.Should().Contain("grant_type=client_credentials");
    }

    [Fact]
    public async Task Reuses_the_cached_token_without_a_second_request()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());
        var sut = CreateSut(handler);

        await sut.GetAsync(CancellationToken.None);
        await sut.GetAsync(CancellationToken.None);

        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Renews_the_token_five_minutes_before_it_expires()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());
        var sut = CreateSut(handler);

        await sut.GetAsync(CancellationToken.None);

        // 60 天有效期，推進到剩 4 分 59 秒
        _time.Advance(TimeSpan.FromSeconds(5184000 - 299));
        await sut.GetAsync(CancellationToken.None);

        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Keeps_the_cached_token_while_more_than_five_minutes_remain()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());
        var sut = CreateSut(handler);

        await sut.GetAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(5184000 - 601));
        await sut.GetAsync(CancellationToken.None);

        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Invalidate_forces_the_next_call_to_refetch()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());
        var sut = CreateSut(handler);

        await sut.GetAsync(CancellationToken.None);
        sut.Invalidate();
        await sut.GetAsync(CancellationToken.None);

        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Concurrent_callers_trigger_only_one_token_request()
    {
        var handler = StubHttpMessageHandler.Json(Fixture());
        var sut = CreateSut(handler);

        await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => sut.GetAsync(CancellationToken.None)));

        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Wraps_http_failures_in_ProviderException(HttpStatusCode status)
    {
        var sut = CreateSut(StubHttpMessageHandler.Status(status));

        var act = () => sut.GetAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<ProviderException>())
            .Which.ProviderKey.Should().Be("igdb");
    }

    [Fact]
    public async Task Wraps_malformed_json_in_ProviderException()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json("not json"));

        var act = () => sut.GetAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ProviderException>();
    }
}
