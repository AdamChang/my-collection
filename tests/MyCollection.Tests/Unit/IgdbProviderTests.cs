using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Providers.Igdb;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Unit;

public class IgdbProviderTests
{
    private readonly Mock<ITwitchTokenProvider> _token = new();

    public IgdbProviderTests() =>
        _token.Setup(t => t.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync("token-1");

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static IgdbOptions Options() => new()
    {
        ClientId = "cid",
        ClientSecret = "csecret",
        MinRequestIntervalMs = 0,
        LookupBatchSize = 10
    };

    private IgdbProvider CreateSut(StubHttpMessageHandler handler)
    {
        var options = Microsoft.Extensions.Options.Options.Create(Options());

        return new IgdbProvider(
            handler.CreateClient("https://api.igdb.com/v4/"),
            _token.Object,
            new IgdbRateLimiter(options, new FakeTimeProvider()),
            options,
            NullLogger<IgdbProvider>.Instance);
    }

    /// <summary>反查先打 external_games 取得 game id，再打 games 取詳情。</summary>
    private static StubHttpMessageHandler LookupHandler(params string[] expectedSteamUids) =>
        new(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("external_games", StringComparison.Ordinal))
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                var expectedUidList = string.Join(",", expectedSteamUids.Select(uid => $"\"{uid}\""));

                request.Method.Should().Be(HttpMethod.Post);
                request.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
                body.Should().Contain("external_game_source = 1");
                body.Should().Contain($"uid = ({expectedUidList})");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri.AbsolutePath.EndsWith("external_games", StringComparison.Ordinal)
                        ? Fixture("igdb-external-steam.json")
                        : Fixture("igdb-games-steam.json"),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

    [Fact]
    public void Declares_search_and_enrich_capabilities()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json("[]"));

        sut.Key.Should().Be("igdb");
        ProviderCapabilities.Of(sut).Should()
            .Be(ProviderCapability.Search | ProviderCapability.Enrich);
        sut.ExternalIdAttributeKey.Should().Be("igdbId");
        sut.CompletionMarkerKey.Should().Be(
            "igdbId", "只有 IGDB 補完會寫 igdbId，所以有值同時代表查得到與補過了");
        sut.RequiredFields.Select(f => f.Key).Should().Contain("igdbId");
        sut.PrefersBackgroundExecution.Should().BeFalse();
    }

    [Fact]
    public async Task Search_maps_every_result()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json(Fixture("igdb-search-witcher.json")));

        var items = await sut.SearchAsync("witcher 3", 20, CancellationToken.None);

        items.Should().HaveCount(2);
        items[0].ExternalId.Should().Be("1942");
        items[0].Name.Should().Be("The Witcher 3: Wild Hunt");
    }

    [Fact]
    public async Task Search_sends_the_credentials_as_headers_and_the_query_as_the_body()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
        });
        var sut = CreateSut(handler);

        await sut.SearchAsync("witcher 3", 5, CancellationToken.None);

        handler.Requests.Single().AbsolutePath.Should().EndWith("/games");
        handler.LastRequestBody.Should().Contain("search \"witcher 3\";");
        handler.LastRequestBody.Should().Contain("limit 5;");
        handler.LastRequestHeaders!.GetValues("Client-ID").Should().ContainSingle("cid");
        handler.LastRequestHeaders.GetValues("Authorization").Should().ContainSingle("Bearer token-1");
    }

    [Theory]
    [InlineData("wit\"cher; where id = 1", "wit cher where id = 1")]
    [InlineData("witcher\n3", "witcher 3")]
    [InlineData("foo\"bar", "foo bar")]
    [InlineData("foo;bar", "foo bar")]
    [InlineData("foo\"\n;\rbar", "foo bar")]
    public async Task Search_strips_apicalypse_control_characters_from_user_input(string input, string expected)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
        });

        await CreateSut(handler).SearchAsync(input, 5, CancellationToken.None);

        handler.LastRequestBody.Should().Contain($"search \"{expected}\";");
    }

    [Fact]
    public async Task Search_returns_empty_when_igdb_has_no_match()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json("[]"));

        (await sut.SearchAsync("zzzz", 20, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Lookup_resolves_steam_appids_through_external_games()
    {
        var sut = CreateSut(LookupHandler("440", "620"));

        var result = await sut.FetchByExternalIdsAsync(["steam:440", "steam:620"], CancellationToken.None);

        result.Found["steam:440"].ExternalId.Should().Be("891");
        result.Found["steam:440"].Name.Should().Be("Team Fortress 2");
        result.Found["steam:620"].ExternalId.Should().Be("72");
        result.Found["steam:620"].Name.Should().Be("Portal 2");
        result.FailedIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Lookup_omits_ids_igdb_has_no_match_for_without_marking_them_failed()
    {
        var sut = CreateSut(LookupHandler("440", "99999999"));

        var result = await sut.FetchByExternalIdsAsync(["steam:440", "steam:99999999"], CancellationToken.None);

        result.Found.Keys.Should().BeEquivalentTo("steam:440");
        result.FailedIds.Should().BeEmpty("查無對應不是失敗");
    }

    [Fact]
    public async Task Lookup_of_an_igdb_id_skips_the_external_games_round_trip()
    {
        var handler = StubHttpMessageHandler.Json(Fixture("igdb-games-steam.json"));
        var sut = CreateSut(handler);

        var result = await sut.FetchByExternalIdsAsync(["igdb:72"], CancellationToken.None);

        result.Found.Should().ContainKey("igdb:72");
        result.Found["igdb:72"].Name.Should().Be("Portal 2");
        handler.Requests.Should().ContainSingle(uri => uri.AbsolutePath.EndsWith("/games"));
    }

    [Fact]
    public async Task Lookup_marks_an_unknown_prefix_as_failed()
    {
        var sut = CreateSut(LookupHandler());

        var result = await sut.FetchByExternalIdsAsync(["psn:CUSA123"], CancellationToken.None);

        result.Found.Should().BeEmpty();
        result.FailedIds.Should().BeEquivalentTo("psn:CUSA123");
    }

    [Fact]
    public async Task Lookup_marks_malformed_numeric_ids_as_failed_without_polluting_a_valid_steam_request()
    {
        var handler = LookupHandler("440");
        var sut = CreateSut(handler);

        var result = await sut.FetchByExternalIdsAsync(
            ["steam:", "steam:44a", "steam:0", "igdb:", "igdb:abc", "igdb:0", "steam:000440"],
            CancellationToken.None);

        result.Found.Keys.Should().BeEquivalentTo("steam:000440");
        result.FailedIds.Should().BeEquivalentTo(
            "steam:", "steam:44a", "steam:0", "igdb:", "igdb:abc", "igdb:0");
        handler.RequestBodies[0].Should().Contain("uid = (\"440\")");
        handler.RequestBodies[0].Should().NotContain("44a");
    }

    [Fact]
    public async Task Lookup_records_request_failures_as_failed_ids_rather_than_throwing()
    {
        var sut = CreateSut(StubHttpMessageHandler.Status(HttpStatusCode.InternalServerError));

        var result = await sut.FetchByExternalIdsAsync(["steam:440", "steam:620"], CancellationToken.None);

        result.Found.Should().BeEmpty();
        result.FailedIds.Should().BeEquivalentTo("steam:440", "steam:620");
    }

    [Fact]
    public async Task Retries_once_after_a_401_with_a_refreshed_token()
    {
        var responses = new Queue<HttpStatusCode>([HttpStatusCode.Unauthorized, HttpStatusCode.OK]);
        var handler = new StubHttpMessageHandler(_ =>
        {
            var status = responses.Dequeue();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(
                    status is HttpStatusCode.OK ? Fixture("igdb-search-witcher.json") : "",
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        var items = await CreateSut(handler).SearchAsync("witcher 3", 20, CancellationToken.None);

        items.Should().HaveCount(2);
        handler.Requests.Should().HaveCount(2);
        _token.Verify(t => t.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task Gives_up_after_a_second_401()
    {
        var sut = CreateSut(StubHttpMessageHandler.Status(HttpStatusCode.Unauthorized));

        var act = () => sut.SearchAsync("witcher 3", 20, CancellationToken.None);

        (await act.Should().ThrowAsync<ProviderException>()).Which.ProviderKey.Should().Be("igdb");
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Search_wraps_http_failures_in_ProviderException(HttpStatusCode status)
    {
        var sut = CreateSut(StubHttpMessageHandler.Status(status));

        var act = () => sut.SearchAsync("witcher 3", 20, CancellationToken.None);

        (await act.Should().ThrowAsync<ProviderException>()).Which.ProviderKey.Should().Be("igdb");
    }

    [Fact]
    public async Task Search_wraps_malformed_json_in_ProviderException()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json("not json"));

        var act = () => sut.SearchAsync("witcher 3", 20, CancellationToken.None);

        await act.Should().ThrowAsync<ProviderException>();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[{\"id\":72}]")]
    [InlineData("[{\"id\":\"72\",\"name\":\"Portal 2\"}]")]
    public async Task Search_wraps_schema_invalid_json_in_ProviderException(string payload)
    {
        var sut = CreateSut(StubHttpMessageHandler.Json(payload));

        var act = () => sut.SearchAsync("portal", 20, CancellationToken.None);

        await act.Should().ThrowAsync<ProviderException>();
    }

    [Theory]
    [InlineData("[{\"id\":0,\"name\":\"Portal 2\"}]")]
    [InlineData("[{\"id\":72,\"name\":null}]")]
    [InlineData("[{\"id\":72,\"name\":\"   \"}]")]
    public async Task Search_wraps_null_or_blank_required_fields_in_ProviderException(string payload)
    {
        var sut = CreateSut(StubHttpMessageHandler.Json(payload));

        var act = () => sut.SearchAsync("portal", 20, CancellationToken.None);

        await act.Should().ThrowAsync<ProviderException>();
    }

    [Fact]
    public async Task Search_wraps_an_out_of_range_release_date_in_ProviderException()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json(
            "[{\"id\":72,\"name\":\"Portal 2\",\"first_release_date\":9223372036854775807}]"));

        var act = () => sut.SearchAsync("portal", 20, CancellationToken.None);

        await act.Should().ThrowAsync<ProviderException>();
    }

    [Fact]
    public async Task Lookup_marks_a_schema_invalid_external_games_row_as_failed()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json("[{\"game\":\"891\",\"uid\":\"440\"}]"));

        var result = await sut.FetchByExternalIdsAsync(["steam:440"], CancellationToken.None);

        result.Found.Should().BeEmpty();
        result.FailedIds.Should().BeEquivalentTo("steam:440");
    }

    [Theory]
    [InlineData("[{\"game\":0,\"uid\":\"440\"}]")]
    [InlineData("[{\"game\":891,\"uid\":null}]")]
    [InlineData("[{\"game\":891,\"uid\":\"   \"}]")]
    public async Task Lookup_marks_null_or_blank_external_games_required_fields_as_failed(string payload)
    {
        var sut = CreateSut(StubHttpMessageHandler.Json(payload));

        var result = await sut.FetchByExternalIdsAsync(["steam:440"], CancellationToken.None);

        result.Found.Should().BeEmpty();
        result.FailedIds.Should().BeEquivalentTo("steam:440");
    }

    [Fact]
    public async Task Lookup_reuses_a_canonical_steam_uid_and_backfills_each_original_key()
    {
        var handler = LookupHandler("440");
        var sut = CreateSut(handler);

        var result = await sut.FetchByExternalIdsAsync(
            ["steam:440", "steam:000440", "steam:440"],
            CancellationToken.None);

        result.Found.Keys.Should().BeEquivalentTo("steam:440", "steam:000440");
        result.Found["steam:440"].ExternalId.Should().Be("891");
        result.Found["steam:000440"].ExternalId.Should().Be("891");
        result.FailedIds.Should().BeEmpty();
        handler.Requests.Count(uri => uri.AbsolutePath.EndsWith("external_games", StringComparison.Ordinal)).Should().Be(1);
        handler.RequestBodies
            .Single(body => body?.Contains("external_game_source = 1", StringComparison.Ordinal) == true)!
            .Should().Contain("uid = (\"440\")");
    }

    [Fact]
    public async Task Lookup_marks_a_schema_invalid_game_details_payload_as_failed()
    {
        var handler = new StubHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                request.RequestUri!.AbsolutePath.EndsWith("external_games", StringComparison.Ordinal)
                    ? "[{\"game\":891,\"uid\":\"440\"}]"
                    : "[{\"id\":891}]",
                System.Text.Encoding.UTF8,
                "application/json")
        });
        var sut = CreateSut(handler);

        var result = await sut.FetchByExternalIdsAsync(["steam:440"], CancellationToken.None);

        result.Found.Should().BeEmpty();
        result.FailedIds.Should().BeEquivalentTo("steam:440");
    }

    [Fact]
    public async Task Lookup_marks_an_out_of_range_release_date_as_failed()
    {
        var handler = new StubHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                request.RequestUri!.AbsolutePath.EndsWith("external_games", StringComparison.Ordinal)
                    ? "[{\"game\":891,\"uid\":\"440\"}]"
                    : "[{\"id\":891,\"name\":\"Team Fortress 2\",\"first_release_date\":9223372036854775807}]",
                System.Text.Encoding.UTF8,
                "application/json")
        });
        var sut = CreateSut(handler);

        var result = await sut.FetchByExternalIdsAsync(["steam:440"], CancellationToken.None);

        result.Found.Should().BeEmpty();
        result.FailedIds.Should().BeEquivalentTo("steam:440");
    }

    [Fact]
    public async Task Search_propagates_caller_cancellation()
    {
        var sut = CreateSut(StubHttpMessageHandler.Json("[]"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => sut.SearchAsync("witcher 3", 20, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
