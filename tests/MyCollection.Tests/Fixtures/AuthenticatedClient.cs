using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MyCollection.Application.Auth;

namespace MyCollection.Tests.Fixtures;

public static class AuthenticatedClient
{
    /// <summary>以正式的維運路徑（CreateInvitationCommand）發出邀請，回傳格式化後的邀請碼。</summary>
    public static async Task<string> CreateInvitationAsync(ApiFactory factory, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var invitation = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new CreateInvitationCommand(email));
        return invitation.Code;
    }

    /// <summary>發邀請、註冊一個新帳號，並回傳已帶好 Bearer token 的 client。</summary>
    public static async Task<HttpClient> CreateAsync(ApiFactory factory, string email)
    {
        var invitationCode = await CreateInvitationAsync(factory, email);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/register", new { email, password = "P@ssw0rd!", displayName = "Tester", invitationCode });
        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        return client;
    }
}
