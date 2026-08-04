using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class IngestionEndpointsTests(MongoFixture mongo) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await mongo.ResetAsync();
        _factory = new ApiFactory(mongo);
        _client = await AuthenticatedClient.CreateAsync(_factory, "ingest@example.com");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Providers_endpoint_lists_steam_psn_and_opengraph()
    {
        var providers = await _client.GetFromJsonAsync<JsonElement>("/ingest/providers");

        providers.EnumerateArray().Select(p => p.GetProperty("key").GetString())
            .Should().BeEquivalentTo("steam", "psn", "opengraph");

        providers.EnumerateArray().Single(p => p.GetProperty("key").GetString() == "psn")
            .GetProperty("capabilities").GetString().Should().Be("BulkSync");
    }

    [Fact]
    public async Task Linking_an_account_never_echoes_the_api_key()
    {
        var response = await _client.PostAsJsonAsync("/external-accounts", new
        {
            provider = "steam", externalUserId = "76561197960287930", apiKey = "SUPER-SECRET-KEY"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("SUPER-SECRET-KEY");

        var listed = await _client.GetStringAsync("/external-accounts");
        listed.Should().NotContain("SUPER-SECRET-KEY");
        listed.Should().Contain("76561197960287930");
    }

    [Fact]
    public async Task Sync_without_a_linked_account_returns_404()
    {
        var response = await _client.PostAsync("/ingest/sync/steam", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sync_with_an_unknown_provider_returns_404()
    {
        var response = await _client.PostAsync("/ingest/sync/unknown", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sync_with_a_url_only_provider_returns_502()
    {
        var response = await _client.PostAsync("/ingest/sync/opengraph", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Fetch_rejects_a_non_http_url_with_400()
    {
        var response = await _client.PostAsync("/ingest/fetch?url=file:///etc/passwd", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Jobs_endpoint_starts_empty()
    {
        var jobs = await _client.GetFromJsonAsync<JsonElement>("/ingest/jobs");

        jobs.GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// Steam 商店補完是背景作業：端點回應時工作還沒開始，狀態必須是 Running，
    /// 進度靠 /ingest/jobs 輪詢。若哪天它回 Succeeded，代表工作被搬回請求內，
    /// 數百款遊戲的補完會讓 HTTP 請求逾時。
    /// </summary>
    [Fact]
    public async Task Enriching_via_steam_returns_a_running_job_without_waiting_for_the_work()
    {
        var response = await _client.PostAsJsonAsync("/ingest/enrich/steam", new { limit = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var job = await response.Content.ReadFromJsonAsync<JsonElement>();
        job.GetProperty("provider").GetString().Should().Be("steam");
        job.GetProperty("status").GetString().Should().Be("Running");
    }

    [Fact]
    public async Task Unlinking_removes_the_account()
    {
        await _client.PostAsJsonAsync("/external-accounts", new
        {
            provider = "steam", externalUserId = "765", apiKey = "k"
        });

        (await _client.DeleteAsync("/external-accounts/steam")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listed = await _client.GetFromJsonAsync<JsonElement>("/external-accounts");
        listed.GetArrayLength().Should().Be(0);
    }
}
