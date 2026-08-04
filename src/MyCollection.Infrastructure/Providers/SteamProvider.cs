using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Providers;

/// <summary>
/// Steam 有兩條路徑，共用同一個 provider key：
/// 同步走 Web API（只拿得到英文品名），本地化補完走商店的 appdetails（繁體中文）。
/// 合成同一個 provider 而不是兩個，是為了讓外部識別碼在兩條路徑上都是 "steam:&lt;appid&gt;"——
/// 拆成兩個 key 會讓 externalRef 與補完的定址對不上。
/// </summary>
public sealed class SteamProvider(
    HttpClient httpClient,
    SteamStoreClient storeClient,
    ISecretProtector secretProtector,
    TimeProvider timeProvider,
    ILogger<SteamProvider> logger) : IBulkSyncProvider, IExternalIdLookupProvider
{
    public const string ProviderKey = ProviderKeys.Steam;

    private const string ExternalIdPrefix = ProviderKeys.Steam + ":";

    public string Key => ProviderKey;

    public string ExternalIdAttributeKey => SteamFields.AppIdKey;

    public string CompletionMarkerKey => SteamFields.StoreUpdatedAtKey;

    public IReadOnlyList<CategoryField> RequiredFields { get; } = SteamFields.All;

    /// <summary>
    /// 商店端點不可批次且約 200 req/5 min，數百款遊戲要跑數分鐘到十數分鐘，
    /// 遠超過 HTTP 請求的合理生命週期。
    /// </summary>
    public bool PrefersBackgroundExecution => true;

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

    /// <summary>
    /// 逐款查商店：appdetails 不支援批次，帶多個 appid 只會回 null。
    /// 一款失敗不影響其餘——整批補完動輒數百款，全有全無等於一次網路抖動就白跑。
    /// </summary>
    public async Task<ExternalLookupResult> FetchByExternalIdsAsync(
        IReadOnlyList<string> externalIds, CancellationToken ct)
    {
        var found = new Dictionary<string, ExternalItem>(StringComparer.Ordinal);
        var failed = new List<string>();

        foreach (var externalId in externalIds.Distinct(StringComparer.Ordinal))
        {
            if (!TryParseAppId(externalId, out var appId))
            {
                logger.LogWarning("Unsupported or malformed external id: {ExternalId}", externalId);
                failed.Add(externalId);
                continue;
            }

            try
            {
                var data = await storeClient.FetchAppDetailsAsync(appId, ct);

                // 查無對應（未上架、已下架）不是失敗，留給呼叫端記為 skipped
                if (data is { } details)
                {
                    found[externalId] = SteamStoreMapper.ToExternalItem(
                        details, timeProvider.GetUtcNow().UtcDateTime);
                }
            }
            catch (ProviderException ex)
            {
                logger.LogWarning(ex, "Steam store lookup failed for {ExternalId}.", externalId);
                failed.Add(externalId);
            }
            catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
            {
                logger.LogWarning(ex, "Steam store response for {ExternalId} had an invalid schema.", externalId);
                failed.Add(externalId);
            }
        }

        return new ExternalLookupResult(found, failed);
    }

    private static bool TryParseAppId(string externalId, out long appId)
    {
        appId = default;

        if (!externalId.StartsWith(ExternalIdPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = externalId[ExternalIdPrefix.Length..];

        return suffix.Length > 0
               && suffix.All(c => c is >= '0' and <= '9')
               && long.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out appId)
               && appId > 0;
    }

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
