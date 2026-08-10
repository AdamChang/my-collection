using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public sealed class ProductionBaselineTests(MongoFixture mongo) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await mongo.ResetAsync();
        _factory = new ApiFactory(mongo);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Health_endpoints_separate_liveness_from_startup_readiness()
    {
        var state = _factory.Services.GetRequiredService<StartupHealthState>();
        state.MarkNotReady();

        (await _client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync("/health/startup")).StatusCode
            .Should().Be(HttpStatusCode.ServiceUnavailable);

        state.MarkReady();
        (await _client.GetAsync("/health/startup")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("https://allowed.example", true)]
    [InlineData("https://blocked.example", false)]
    public async Task Cors_allows_only_configured_origin(string origin, bool expectedAllowed)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/health/live");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
            .Should().Be(expectedAllowed);
        if (expectedAllowed)
        {
            values.Should().ContainSingle(origin);
        }
    }
}
