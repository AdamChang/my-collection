using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Providers.Psn;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Unit;

public class PsnProviderTests
{
    private static PsnProvider CreateSut(
        StubHttpMessageHandler handler,
        ISecretProtector? protector = null,
        PsnOptions? options = null) =>
        new(handler.CreateClient("https://unused.example/"),
            protector ?? new FakeSecretProtector(),
            Options.Create(options ?? new PsnOptions()));

    private static ExternalAccount Account() => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = ObjectId.GenerateNewId(),
        Provider = "psn",
        ExternalUserId = "me",
        ProtectedApiKey = "protected-fake-npsso",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static StubHttpMessageHandler SuccessfulHandler(
        string trophyPayload,
        Action<HttpRequestMessage>? inspect = null) =>
        AuthenticatedHandler(
            _ => Json(trophyPayload),
            inspect);

    private static StubHttpMessageHandler AuthenticatedHandler(
        Func<HttpRequestMessage, HttpResponseMessage> trophyResponder,
        Action<HttpRequestMessage>? inspect = null) =>
        RoutingHandler(
            _ => RedirectWithCode(),
            _ => Json(TokenPayload()),
            trophyResponder,
            inspect);

    private static StubHttpMessageHandler RoutingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> authorizeResponder,
        Func<HttpRequestMessage, HttpResponseMessage> tokenResponder,
        Func<HttpRequestMessage, HttpResponseMessage> trophyResponder,
        Action<HttpRequestMessage>? inspect = null) =>
        new(request =>
        {
            inspect?.Invoke(request);

            return request.RequestUri!.AbsolutePath switch
            {
                "/api/authz/v3/oauth/authorize" => authorizeResponder(request),
                "/api/authz/v3/oauth/token" => tokenResponder(request),
                "/api/trophy/v1/users/me/trophyTitles" => trophyResponder(request),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });

    private static HttpResponseMessage RedirectWithCode() => new(HttpStatusCode.Found)
    {
        Headers = { Location = new Uri("com.scee.psxandroid.scecompcall://redirect?code=fake-auth-code") }
    };

    private static string TokenPayload() =>
        """{"access_token":"fake-access-token","refresh_token":"fake-unused-refresh-token","token_type":"bearer","expires_in":3600}""";

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    [Fact]
    public void Declares_only_bulk_sync_capability()
    {
        var sut = CreateSut(StubHttpMessageHandler.Status(HttpStatusCode.OK));

        sut.Key.Should().Be("psn");
        ProviderCapabilities.Of(sut).Should().Be(ProviderCapability.BulkSync);
    }

    [Fact]
    public async Task Sync_uses_the_exact_oauth_and_trophy_request_contract()
    {
        var authorizeSeen = false;
        var tokenSeen = false;
        var trophySeen = false;
        var protector = new FakeSecretProtector();
        var handler = SuccessfulHandler(Fixture("psn-trophy-titles-page-0.json"), request =>
        {
            request.RequestUri!.AbsoluteUri.Should().NotContain("fake-npsso-from-protector");

            switch (request.RequestUri.AbsolutePath)
            {
                case "/api/authz/v3/oauth/authorize":
                    authorizeSeen = true;
                    request.Method.Should().Be(HttpMethod.Get);
                    request.RequestUri.AbsoluteUri.Should().Be(
                        "https://ca.account.sony.com/api/authz/v3/oauth/authorize" +
                        "?access_type=offline" +
                        "&client_id=09515159-7237-4370-9b40-3806e67c0891" +
                        "&redirect_uri=com.scee.psxandroid.scecompcall%3A%2F%2Fredirect" +
                        "&response_type=code" +
                        "&scope=psn%3Amobile.v2.core%20psn%3Aclientapp");
                    request.Headers.GetValues("Cookie").Should()
                        .Equal("npsso=fake-npsso-from-protector");
                    request.Headers.Authorization.Should().BeNull();
                    break;

                case "/api/authz/v3/oauth/token":
                    tokenSeen = true;
                    request.Method.Should().Be(HttpMethod.Post);
                    request.Headers.Should().NotContain(h => h.Key == "Cookie");
                    request.Headers.Authorization.Should().BeEquivalentTo(
                        new AuthenticationHeaderValue(
                            "Basic",
                            "MDk1MTUxNTktNzIzNy00MzcwLTliNDAtMzgwNmU2N2MwODkxOnVjUGprYTV0bnRCMktxc1A="));
                    request.Content!.Headers.ContentType!.MediaType.Should()
                        .Be("application/x-www-form-urlencoded");
                    request.Content.ReadAsStringAsync().GetAwaiter().GetResult().Should().Be(
                        "code=fake-auth-code" +
                        "&redirect_uri=com.scee.psxandroid.scecompcall%3A%2F%2Fredirect" +
                        "&grant_type=authorization_code" +
                        "&token_format=jwt");
                    break;

                case "/api/trophy/v1/users/me/trophyTitles":
                    trophySeen = true;
                    request.Method.Should().Be(HttpMethod.Get);
                    request.RequestUri.AbsoluteUri.Should().Be(
                        "https://m.np.playstation.com/api/trophy/v1/users/me/trophyTitles?limit=800&offset=0");
                    request.Headers.Should().NotContain(h => h.Key == "Cookie");
                    request.Headers.Authorization.Should().BeEquivalentTo(
                        new AuthenticationHeaderValue("Bearer", "fake-access-token"));
                    break;
            }
        });

        await CreateSut(handler, protector).SyncAsync(Account(), CancellationToken.None);

        protector.UnprotectedCiphertexts.Should().Equal("protected-fake-npsso");
        authorizeSeen.Should().BeTrue();
        tokenSeen.Should().BeTrue();
        trophySeen.Should().BeTrue();
        handler.RequestBodies.Where(body => body is not null).Should().OnlyContain(
            body => !body!.Contains("fake-npsso-from-protector", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sync_maps_one_trophy_page_and_preserves_field_ownership()
    {
        var handler = SuccessfulHandler(Fixture("psn-trophy-titles-page-0.json"));

        var items = await CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        items.Should().HaveCount(2);
        var item = items.Single(x => x.ExternalId == "NPWR12345_00");
        item.Name.Should().Be("Astro's Fake Adventure");
        item.Description.Should().Be("Representative public-shaped fixture data.");
        item.ImageUrl.Should().Be(new Uri("https://image.api.playstation.com/fake/NPWR12345_00.png"));
        item.SourceUrl.Should().BeNull();
        item.Attributes.Should().Contain(new Dictionary<string, object?>
        {
            ["iconUrl"] = "https://image.api.playstation.com/fake/NPWR12345_00.png",
            ["platform"] = "PS5,PS4",
            [PsnFields.ProgressKey] = 42,
            [PsnFields.LastPlayedAtKey] = new DateTime(2026, 7, 30, 13, 45, 12, DateTimeKind.Utc)
        });
        item.FillOnlyIfAbsent.Should().BeEquivalentTo(["platform"]);

        items.Single(x => x.ExternalId == "NPWR67890_00")
            .Attributes[PsnFields.LastPlayedAtKey].Should()
            .Be(new DateTime(2026, 7, 30, 17, 2, 3, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Sync_fetches_all_trophy_titles_using_offsets_zero_and_eight_hundred()
    {
        var firstPage = ExpandFirstPageToEightHundred();
        var secondPage = Fixture("psn-trophy-titles-page-800.json");
        var trophyOffsets = new List<string>();
        var handler = AuthenticatedHandler(request =>
        {
            trophyOffsets.Add(request.RequestUri!.Query);
            return request.RequestUri.Query switch
            {
                "?limit=800&offset=0" => Json(firstPage),
                "?limit=800&offset=800" => Json(secondPage),
                _ => throw new InvalidOperationException($"Unexpected Trophy page: {request.RequestUri}")
            };
        });

        var items = await CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        items.Should().HaveCount(801);
        items.Select(x => x.ExternalId).Should().Contain(["NPWR00000_00", "NPWR00799_00", "NPWR99999_00"]);
        trophyOffsets.Should().Equal("?limit=800&offset=0", "?limit=800&offset=800");
    }

    [Fact]
    public async Task Sync_keeps_the_known_total_when_a_later_short_page_omits_it()
    {
        var trophyOffsets = new List<string>();
        var handler = AuthenticatedHandler(request =>
        {
            trophyOffsets.Add(request.RequestUri!.Query);
            return request.RequestUri.Query switch
            {
                "?limit=800&offset=0" => Json(GeneratedPage(800, start: 0, total: 802, nextOffset: 800)),
                "?limit=800&offset=800" => Json(GeneratedPage(1, start: 800, total: null, nextOffset: 801)),
                "?limit=800&offset=801" => Json(GeneratedPage(1, start: 801, total: null, nextOffset: null)),
                _ => throw new InvalidOperationException($"Unexpected Trophy page: {request.RequestUri}")
            };
        });

        var items = await CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        items.Should().HaveCount(802);
        trophyOffsets.Should().Equal(
            "?limit=800&offset=0",
            "?limit=800&offset=800",
            "?limit=800&offset=801");
    }

    [Fact]
    public async Task Sync_stops_at_a_known_exact_multiple_without_requesting_an_extra_page()
    {
        var trophyOffsets = new List<string>();
        var handler = AuthenticatedHandler(request =>
        {
            trophyOffsets.Add(request.RequestUri!.Query);
            return request.RequestUri.Query switch
            {
                "?limit=800&offset=0" => Json(GeneratedPage(800, start: 0, total: 1600, nextOffset: 800)),
                "?limit=800&offset=800" => Json(GeneratedPage(800, start: 800, total: null, nextOffset: null)),
                _ => throw new InvalidOperationException($"Unexpected Trophy page: {request.RequestUri}")
            };
        });

        var items = await CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        items.Should().HaveCount(1600);
        trophyOffsets.Should().Equal("?limit=800&offset=0", "?limit=800&offset=800");
    }

    [Fact]
    public async Task Sync_rejects_a_total_item_count_that_changes_between_pages()
    {
        var handler = AuthenticatedHandler(request => request.RequestUri!.Query switch
        {
            "?limit=800&offset=0" => Json(GeneratedPage(800, start: 0, total: 801, nextOffset: 800)),
            "?limit=800&offset=800" => Json(GeneratedPage(1, start: 800, total: 802, nextOffset: null)),
            _ => throw new InvalidOperationException($"Unexpected Trophy page: {request.RequestUri}")
        });

        var act = () => CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        var exception = (await act.Should()
            .ThrowAsync<MyCollection.Domain.Exceptions.ProviderException>()).Which;
        exception.ProviderKey.Should().Be("psn");
        exception.Message.Should().Contain("totalItemCount changed");
    }

    [Fact]
    public async Task Sync_returns_empty_for_an_empty_first_trophy_page()
    {
        var handler = SuccessfulHandler(
            """{"trophyTitles":[],"totalItemCount":0,"nextOffset":null,"previousOffset":null}""");

        var items = await CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_rejects_a_non_advancing_trophy_page_offset()
    {
        var page = JsonNode.Parse(ExpandFirstPageToEightHundred())!.AsObject();
        page["nextOffset"] = 0;
        var handler = SuccessfulHandler(page.ToJsonString());

        var act = () => CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        (await act.Should().ThrowAsync<MyCollection.Domain.Exceptions.ProviderException>())
            .Which.ProviderKey.Should().Be("psn");
    }

    [Fact]
    public async Task Sync_reports_expired_npsso_when_authorization_redirect_has_no_code()
    {
        var handler = RoutingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri("com.scee.psxandroid.scecompcall://redirect?state=fake") }
            },
            _ => throw new InvalidOperationException("Token endpoint must not be called."),
            _ => throw new InvalidOperationException("Trophy endpoint must not be called."));

        var act = () => CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<MyCollection.Domain.Exceptions.ProviderException>()).Which;
        exception.ProviderKey.Should().Be("psn");
        exception.Message.Should().Contain("NPSSO 已過期，請重新取得");
    }

    [Theory]
    [InlineData("?code=fake-auth-code")]
    [InlineData("callback?code=fake-auth-code")]
    public async Task Sync_accepts_an_authorization_code_from_a_relative_location(string location)
    {
        var handler = RoutingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri(location, UriKind.Relative) }
            },
            _ => Json(TokenPayload()),
            _ => Json(Fixture("psn-trophy-titles-page-0.json")));

        var items = await CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        items.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Sync_reports_expired_npsso_for_authorization_credential_failures(HttpStatusCode status)
    {
        var handler = RoutingHandler(
            _ => new HttpResponseMessage(status),
            _ => throw new InvalidOperationException("Token endpoint must not be called."),
            _ => throw new InvalidOperationException("Trophy endpoint must not be called."));

        var act = () => CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        (await act.Should().ThrowAsync<MyCollection.Domain.Exceptions.ProviderException>())
            .Which.Message.Should().Contain("NPSSO 已過期，請重新取得");
    }

    [Fact]
    public async Task Sync_does_not_claim_npsso_expired_for_other_authorization_http_failures()
    {
        var handler = RoutingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest),
            _ => throw new InvalidOperationException("Token endpoint must not be called."),
            _ => throw new InvalidOperationException("Trophy endpoint must not be called."));

        var act = () => CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<MyCollection.Domain.Exceptions.ProviderException>()).Which;
        exception.ProviderKey.Should().Be("psn");
        exception.Message.Should().NotContain("NPSSO 已過期，請重新取得");
    }

    [Fact]
    public async Task Sync_reports_expired_npsso_when_token_exchange_rejects_the_code()
    {
        var handler = RoutingHandler(
            _ => RedirectWithCode(),
            _ => Json("{}", HttpStatusCode.BadRequest),
            _ => throw new InvalidOperationException("Trophy endpoint must not be called."));

        var act = () => CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        (await act.Should().ThrowAsync<MyCollection.Domain.Exceptions.ProviderException>())
            .Which.Message.Should().Contain("NPSSO 已過期，請重新取得");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Sync_wraps_trophy_http_failures_without_claiming_npsso_expired(HttpStatusCode status)
    {
        var handler = AuthenticatedHandler(_ => Json("{}", status));

        var act = () => CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<MyCollection.Domain.Exceptions.ProviderException>()).Which;
        exception.ProviderKey.Should().Be("psn");
        exception.Message.Should().NotContain("NPSSO 已過期，請重新取得");
    }

    [Fact]
    public async Task Sync_wraps_malformed_trophy_json_in_a_provider_keyed_failure()
    {
        var handler = SuccessfulHandler("not json");

        var act = () => CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<MyCollection.Domain.Exceptions.ProviderException>()).Which;
        exception.ProviderKey.Should().Be("psn");
        exception.Message.Should().NotContain("NPSSO 已過期，請重新取得");
    }

    [Theory]
    [InlineData("npCommunicationId", "")]
    [InlineData("trophyTitleName", " ")]
    [InlineData("trophyTitleIconUrl", "")]
    [InlineData("trophyTitleIconUrl", "relative/icon.png")]
    [InlineData("trophyTitlePlatform", "")]
    [InlineData("lastUpdatedDateTime", "not-a-date")]
    public async Task Sync_wraps_invalid_required_trophy_fields_in_a_provider_keyed_failure(
        string field,
        string invalidValue)
    {
        var page = JsonNode.Parse(Fixture("psn-trophy-titles-page-0.json"))!.AsObject();
        page["trophyTitles"]!.AsArray()[0]!.AsObject()[field] = invalidValue;
        var handler = SuccessfulHandler(page.ToJsonString());

        var act = () => CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        (await act.Should().ThrowAsync<MyCollection.Domain.Exceptions.ProviderException>())
            .Which.ProviderKey.Should().Be("psn");
    }

    [Fact]
    public async Task Sync_wraps_a_missing_trophy_titles_collection_as_invalid_schema()
    {
        var handler = SuccessfulHandler("""{"totalItemCount":1,"nextOffset":null,"previousOffset":null}""");

        var act = () => CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        (await act.Should().ThrowAsync<MyCollection.Domain.Exceptions.ProviderException>())
            .Which.ProviderKey.Should().Be("psn");
    }

    [Fact]
    public async Task Sync_wraps_a_null_trophy_title_element_as_invalid_schema()
    {
        var handler = SuccessfulHandler("""{"trophyTitles":[null],"totalItemCount":1,"nextOffset":null}""");

        var act = () => CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        (await act.Should().ThrowAsync<MyCollection.Domain.Exceptions.ProviderException>())
            .Which.ProviderKey.Should().Be("psn");
    }

    [Fact]
    public async Task Sync_rejects_a_missing_numeric_progress_instead_of_defaulting_it_to_zero()
    {
        var page = JsonNode.Parse(Fixture("psn-trophy-titles-page-0.json"))!.AsObject();
        page["trophyTitles"]!.AsArray()[0]!.AsObject().Remove("progress");
        var handler = SuccessfulHandler(page.ToJsonString());

        var act = () => CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        (await act.Should().ThrowAsync<MyCollection.Domain.Exceptions.ProviderException>())
            .Which.ProviderKey.Should().Be("psn");
    }

    [Fact]
    public async Task Sync_wraps_network_failures_in_a_provider_keyed_failure()
    {
        var handler = AuthenticatedHandler(_ => throw new HttpRequestException("fake network failure"));

        var act = () => CreateSut(handler).SyncAsync(Account(), CancellationToken.None);

        (await act.Should().ThrowAsync<MyCollection.Domain.Exceptions.ProviderException>())
            .Which.ProviderKey.Should().Be("psn");
    }

    [Fact]
    public async Task Sync_wraps_secret_decryption_failure_without_leaking_secret_material()
    {
        var handler = SuccessfulHandler(Fixture("psn-trophy-titles-page-0.json"));
        var protector = new ThrowingSecretProtector();

        var act = () => CreateSut(handler, protector).SyncAsync(Account(), CancellationToken.None);

        var exception = (await act.Should()
            .ThrowAsync<MyCollection.Domain.Exceptions.ProviderException>()).Which;
        exception.ProviderKey.Should().Be("psn");
        exception.Message.Should().NotContain("protected-fake-npsso");
        exception.Message.Should().NotContain("NPSSO");
        exception.Message.Should().NotContain("fake-secret-material");
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public async Task Sync_does_not_relabel_caller_cancellation_as_a_provider_failure()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = SuccessfulHandler(Fixture("psn-trophy-titles-page-0.json"));

        var act = () => CreateSut(handler).SyncAsync(Account(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static string ExpandFirstPageToEightHundred() =>
        GeneratedPage(800, start: 0, total: 801, nextOffset: 800);

    private static string GeneratedPage(int count, int start, int? total, int? nextOffset)
    {
        var page = JsonNode.Parse(Fixture("psn-trophy-titles-page-0.json"))!.AsObject();
        var template = page["trophyTitles"]!.AsArray()[0]!.AsObject();
        var titles = new JsonArray();

        for (var i = start; i < start + count; i++)
        {
            var title = template.DeepClone().AsObject();
            title["npCommunicationId"] = $"NPWR{i:00000}_00";
            title["trophyTitleName"] = $"Generated Fake Title {i}";
            titles.Add(title);
        }

        page["trophyTitles"] = titles;
        if (total is { } knownTotal)
        {
            page["totalItemCount"] = knownTotal;
        }
        else
        {
            page.Remove("totalItemCount");
        }

        page["nextOffset"] = nextOffset;
        page.Remove("previousOffset");
        return page.ToJsonString();
    }

    private sealed class FakeSecretProtector : ISecretProtector
    {
        public List<string> UnprotectedCiphertexts { get; } = [];

        public string Protect(string plaintext) => throw new NotSupportedException();

        public string Unprotect(string ciphertext)
        {
            UnprotectedCiphertexts.Add(ciphertext);
            return "fake-npsso-from-protector";
        }
    }

    private sealed class ThrowingSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => throw new NotSupportedException();

        public string Unprotect(string ciphertext) =>
            throw new CryptographicException(
                "protected-fake-npsso npsso=fake-secret-material");
    }
}
