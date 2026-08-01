using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Providers.Igdb;

public interface ITwitchTokenProvider
{
    Task<string> GetAsync(CancellationToken ct);

    /// <summary>IGDB 回 401 時呼叫，強制下一次重新取得。</summary>
    void Invalidate();
}

/// <summary>
/// Twitch client credentials 的 app access token（約 60 天）。
/// 存記憶體即可：重啟成本是一次額外請求，換來零狀態管理。
/// 必須註冊為 singleton，否則每次解析都是新的空快取。
/// </summary>
public sealed class TwitchTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<IgdbOptions> options,
    TimeProvider timeProvider) : ITwitchTokenProvider, IDisposable
{
    public const string HttpClientName = "twitch-oauth";

    /// <summary>提前這麼久換新，避免請求還在路上時 token 剛好到期。</summary>
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _cacheLock = new();

    private string? _token;
    private DateTimeOffset _expiresAt;
    private long _generation;

    public async Task<string> GetAsync(CancellationToken ct)
    {
        long generation;
        lock (_cacheLock)
        {
            if (IsFresh())
            {
                return _token!;
            }

            generation = _generation;
        }

        await _gate.WaitAsync(ct);
        try
        {
            // 等鎖期間可能已有人換好了，再確認一次才不會打出多餘請求
            lock (_cacheLock)
            {
                if (IsFresh())
                {
                    return _token!;
                }

                generation = _generation;
            }

            var response = await FetchAsync(ct);

            lock (_cacheLock)
            {
                if (_generation == generation)
                {
                    _token = response.AccessToken;
                    _expiresAt = timeProvider.GetUtcNow().AddSeconds(response.ExpiresIn);
                }
            }

            return response.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        lock (_cacheLock)
        {
            _generation++;
            _token = null;
            _expiresAt = default;
        }
    }

    public void Dispose() => _gate.Dispose();

    private bool IsFresh() =>
        _token is not null && timeProvider.GetUtcNow() + RenewalMargin < _expiresAt;

    private async Task<TokenResponse> FetchAsync(CancellationToken ct)
    {
        var settings = options.Value;
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("client_id", settings.ClientId),
            new KeyValuePair<string, string>("client_secret", settings.ClientSecret),
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        ]);

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            var response = await client.PostAsync("oauth2/token", content, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException(
                    IgdbOptions.ProviderKey,
                    $"Twitch returned HTTP {(int)response.StatusCode} for the token request.");
            }

            return await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                   is { AccessToken: var accessToken, ExpiresIn: > 0 } tokenResponse
                   && !string.IsNullOrWhiteSpace(accessToken)
                ? tokenResponse
                : throw new ProviderException(
                    IgdbOptions.ProviderKey, "Twitch returned an invalid token response.");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
                                   && ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new ProviderException(
                IgdbOptions.ProviderKey, $"Twitch token request failed: {ex.Message}", ex);
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] long ExpiresIn);
}
