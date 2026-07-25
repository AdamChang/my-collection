using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MyCollection.Application.Auth;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class AuthEndpointsTests(MongoFixture mongo) : IAsyncLifetime
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

    private static object RegisterPayload(string email = "adam@example.com") =>
        new { email, password = "P@ssw0rd!", displayName = "Adam" };

    [Fact]
    public async Task Register_returns_tokens_and_user()
    {
        var response = await _client.PostAsJsonAsync("/auth/register", RegisterPayload());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        auth!.AccessToken.Should().NotBeNullOrEmpty();
        auth.RefreshToken.Should().NotBeNullOrEmpty();
        auth.User.Email.Should().Be("adam@example.com");
    }

    [Fact]
    public async Task Register_with_invalid_payload_returns_400_with_errors()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/register", new { email = "nope", password = "x", displayName = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem.Should().ContainKey("errors");
    }

    [Fact]
    public async Task Register_with_duplicate_email_returns_409()
    {
        await _client.PostAsJsonAsync("/auth/register", RegisterPayload());

        var response = await _client.PostAsJsonAsync("/auth/register", RegisterPayload());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_403()
    {
        await _client.PostAsJsonAsync("/auth/register", RegisterPayload());

        var response = await _client.PostAsJsonAsync(
            "/auth/login", new { email = "adam@example.com", password = "wrong-password" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Access_token_authorises_protected_endpoint()
    {
        var registered = await (await _client.PostAsJsonAsync("/auth/register", RegisterPayload()))
            .Content.ReadFromJsonAsync<AuthResponse>();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registered!.AccessToken);
        var response = await _client.GetAsync("/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        body!["userId"].Should().Be(registered.User.Id);
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_401()
    {
        var response = await _client.GetAsync("/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_rotates_token_and_old_token_stops_working()
    {
        var registered = await (await _client.PostAsJsonAsync("/auth/register", RegisterPayload()))
            .Content.ReadFromJsonAsync<AuthResponse>();

        var refreshed = await _client.PostAsJsonAsync(
            "/auth/refresh", new { refreshToken = registered!.RefreshToken });
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);

        var reuseOld = await _client.PostAsJsonAsync(
            "/auth/refresh", new { refreshToken = registered.RefreshToken });
        reuseOld.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
