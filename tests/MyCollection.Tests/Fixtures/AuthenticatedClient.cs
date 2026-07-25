using System.Net.Http.Headers;
using System.Net.Http.Json;
using MyCollection.Application.Auth;

namespace MyCollection.Tests.Fixtures;

public static class AuthenticatedClient
{
    /// <summary>註冊一個新帳號並回傳已帶好 Bearer token 的 client。</summary>
    public static async Task<HttpClient> CreateAsync(ApiFactory factory, string email)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/register", new { email, password = "P@ssw0rd!", displayName = "Tester" });
        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        return client;
    }
}
