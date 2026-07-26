using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Providers;

public sealed class SteamProvider(
    HttpClient httpClient,
    ISecretProtector secretProtector,
    ILogger<SteamProvider> logger) : IMetadataProvider
{
    public const string ProviderKey = "steam";

    public string Key => ProviderKey;

    public ProviderCapability Capabilities => ProviderCapability.BulkSync;

    public async Task<IReadOnlyList<ExternalItem>> SyncAsync(ExternalAccount account, CancellationToken ct)
    {
        var apiKey = secretProtector.Unprotect(account.ProtectedApiKey);
        var requestUri =
            $"IPlayerService/GetOwnedGames/v1/?key={Uri.EscapeDataString(apiKey)}" +
            $"&steamid={Uri.EscapeDataString(account.ExternalUserId)}" +
            "&include_appinfo=1&include_played_free_games=1&format=json";

        GetOwnedGamesResponse? payload;
        try
        {
            var response = await httpClient.GetAsync(requestUri, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException(
                    ProviderKey, $"Steam returned HTTP {(int)response.StatusCode} for GetOwnedGames.");
            }

            payload = await response.Content.ReadFromJsonAsync<GetOwnedGamesResponse>(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new ProviderException(ProviderKey, $"Steam request failed: {ex.Message}", ex);
        }

        var games = payload?.Response?.Games;
        if (games is null or { Count: 0 })
        {
            // 個人資料未設為公開時 Steam 回傳空的 response 物件，不是錯誤
            logger.LogInformation("Steam returned no games for {SteamId}; profile may be private.", account.ExternalUserId);
            return [];
        }

        return games.Select(ToExternalItem).ToArray();
    }

    /// <summary>Steam 不提供由商店 URL 反查的公開 API。</summary>
    public Task<ExternalItem?> FetchByUrlAsync(Uri url, CancellationToken ct) =>
        Task.FromResult<ExternalItem?>(null);

    private static ExternalItem ToExternalItem(SteamGame game)
    {
        var attributes = new Dictionary<string, object?>
        {
            ["playtimeForever"] = game.PlaytimeForever,
            ["headerUrl"] = HeaderUrl(game.AppId).ToString()
        };

        if (!string.IsNullOrWhiteSpace(game.ImgIconUrl))
        {
            attributes["iconUrl"] =
                $"https://media.steampowered.com/steamcommunity/public/images/apps/{game.AppId}/{game.ImgIconUrl}.jpg";
        }

        return new ExternalItem(
            game.AppId.ToString(),
            game.Name,
            Description: null,
            ImageUrl: HeaderUrl(game.AppId),
            Attributes: attributes)
        {
            SourceUrl = new Uri($"https://store.steampowered.com/app/{game.AppId}")
        };
    }

    private static Uri HeaderUrl(long appId) =>
        new($"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg");

    private sealed record GetOwnedGamesResponse(
        [property: JsonPropertyName("response")] OwnedGames? Response);

    private sealed record OwnedGames(
        [property: JsonPropertyName("game_count")] int GameCount,
        [property: JsonPropertyName("games")] List<SteamGame>? Games);

    private sealed record SteamGame(
        [property: JsonPropertyName("appid")] long AppId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("playtime_forever")] int PlaytimeForever,
        [property: JsonPropertyName("img_icon_url")] string? ImgIconUrl);
}
