using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Providers.Igdb;

/// <summary>
/// IGDB 只提供公開遊戲資料，不綁使用者身分，因此憑證是全站共用的環境變數，
/// 不走 ExternalAccount 那套每人一把的加密儲存。
/// </summary>
public sealed class IgdbProvider(
    HttpClient httpClient,
    ITwitchTokenProvider tokenProvider,
    IgdbRateLimiter rateLimiter,
    IOptions<IgdbOptions> options,
    ILogger<IgdbProvider> logger) : ISearchProvider
{
    public const string ProviderKey = IgdbOptions.ProviderKey;

    private const string SteamPrefix = "steam:";
    private const string IgdbPrefix = "igdb:";
    private const int SteamExternalGameSource = 1;

    private const string GameFields =
        "fields name,summary,url,first_release_date,total_rating,cover.image_id," +
        "genres.name,platforms.abbreviation,involved_companies.company.name," +
        "involved_companies.developer,involved_companies.publisher," +
        "external_games.uid,external_games.external_game_source;";

    public string Key => ProviderKey;

    /// <summary>
    /// IGDB 的識別碼來源與完成標記是同一個欄位——只有 IGDB 補完會寫 igdbId，
    /// 所以「有值」同時代表「查得到」與「補過了」。Steam 兩者必須分開，
    /// 因為 IGDB 也會寫它的識別碼欄位。
    /// </summary>
    public string ExternalIdAttributeKey => IgdbFields.MarkerKey;

    public string CompletionMarkerKey => IgdbFields.MarkerKey;

    public IReadOnlyList<CategoryField> RequiredFields { get; } = IgdbFields.All;

    /// <summary>IGDB 可批次反查且允許 4 req/sec，一次補完在請求內就跑得完。</summary>
    public bool PrefersBackgroundExecution => false;

    public async Task<IReadOnlyList<ExternalItem>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var effectiveLimit = Math.Clamp(limit, 1, options.Value.SearchLimit);
        var body =
            $"search \"{Sanitize(query)}\";\n" +
            $"{GameFields}\n" +
            "where version_parent = null;\n" +
            $"limit {effectiveLimit.ToString(CultureInfo.InvariantCulture)};";

        var games = await QueryAsync("games", body, ct);

        return MapGames(games);
    }

    public async Task<ExternalLookupResult> FetchByExternalIdsAsync(
        IReadOnlyList<string> externalIds, CancellationToken ct)
    {
        var found = new Dictionary<string, ExternalItem>(StringComparer.Ordinal);
        var failed = new List<string>();
        var recognised = new List<LookupId>();

        foreach (var externalId in externalIds.Distinct(StringComparer.Ordinal))
        {
            if (TryParseNumericId(externalId, SteamPrefix, out var steamId))
            {
                recognised.Add(new LookupId(externalId, IsSteam: true, steamId));
            }
            else if (TryParseNumericId(externalId, IgdbPrefix, out var igdbId))
            {
                recognised.Add(new LookupId(externalId, IsSteam: false, igdbId));
            }
            else
            {
                logger.LogWarning("Unsupported or malformed external id: {ExternalId}", externalId);
                failed.Add(externalId);
            }
        }

        foreach (var chunk in recognised.Chunk(Math.Max(1, options.Value.LookupBatchSize)))
        {
            try
            {
                await ResolveChunkAsync(chunk, found, ct);
            }
            catch (ProviderException ex)
            {
                logger.LogWarning(ex, "IGDB lookup failed for a chunk of {Count} ids.", chunk.Length);
                failed.AddRange(chunk.Select(id => id.ExternalId));
            }
        }

        return new ExternalLookupResult(found, failed);
    }

    private async Task ResolveChunkAsync(
        LookupId[] chunk, Dictionary<string, ExternalItem> found, CancellationToken ct)
    {
        var byGameId = new Dictionary<long, List<string>>();
        var steamIds = chunk
            .Where(id => id.IsSteam)
            .ToArray();

        foreach (var externalId in chunk.Where(id => !id.IsSteam))
        {
            Track(byGameId, externalId.NumericId, externalId.ExternalId);
        }

        if (steamIds.Length > 0)
        {
            foreach (var (gameId, externalId) in await ResolveSteamAsync(steamIds, ct))
            {
                Track(byGameId, gameId, externalId);
            }
        }

        if (byGameId.Count == 0)
        {
            return;
        }

        var idList = string.Join(",", byGameId.Keys.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        var games = await QueryAsync(
            "games",
            $"{GameFields}\nwhere id = ({idList});\nlimit 500;",
            ct);

        foreach (var item in MapGames(games))
        {
            if (!long.TryParse(item.ExternalId, CultureInfo.InvariantCulture, out var gameId)
                || !byGameId.TryGetValue(gameId, out var externalIds))
            {
                continue;
            }

            foreach (var externalId in externalIds)
            {
                found[externalId] = item;
            }
        }
    }

    private async Task<IReadOnlyList<(long GameId, string ExternalId)>> ResolveSteamAsync(
        LookupId[] steamExternalIds, CancellationToken ct)
    {
        var uidToExternalIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var externalId in steamExternalIds)
        {
            var uid = externalId.NumericId.ToString(CultureInfo.InvariantCulture);

            if (!uidToExternalIds.TryGetValue(uid, out var originalIds))
            {
                uidToExternalIds[uid] = originalIds = [];
            }

            originalIds.Add(externalId.ExternalId);
        }

        var uidList = string.Join(",", uidToExternalIds.Keys.Select(uid => $"\"{uid}\""));
        var rows = await QueryAsync(
            "external_games",
            "fields game,uid;\n" +
            $"where external_game_source = {SteamExternalGameSource} & uid = ({uidList});\n" +
            "limit 500;",
            ct);
        return MapSteamRows(rows, uidToExternalIds);
    }

    private static void Track(Dictionary<long, List<string>> byGameId, long gameId, string externalId)
    {
        if (!byGameId.TryGetValue(gameId, out var list))
        {
            byGameId[gameId] = list = [];
        }

        list.Add(externalId);
    }

    private static bool TryParseNumericId(string externalId, string prefix, out long numericId)
    {
        numericId = default;

        if (!externalId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = externalId[prefix.Length..];

        return suffix.Length > 0
               && suffix.All(c => c is >= '0' and <= '9')
               && long.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out numericId)
               && numericId > 0;
    }

    private static IReadOnlyList<ExternalItem> MapGames(JsonElement games) =>
        MapSchema("games", () => games.EnumerateArray()
            .Select(game =>
            {
                ValidateGame(game);
                return IgdbMapper.ToExternalItem(game);
            })
            .ToArray());

    private static IReadOnlyList<(long GameId, string ExternalId)> MapSteamRows(
        JsonElement rows, IReadOnlyDictionary<string, List<string>> uidToExternalIds) =>
        MapSchema("external_games", () =>
        {
            var resolved = new List<(long, string)>();

            foreach (var row in rows.EnumerateArray())
            {
                var uid = row.GetProperty("uid").GetString()
                    ?? throw new InvalidOperationException("IGDB external game uid was null.");
                var gameId = row.GetProperty("game").GetInt64();

                if (string.IsNullOrWhiteSpace(uid))
                {
                    throw new InvalidOperationException("IGDB external game uid was blank.");
                }

                if (gameId <= 0)
                {
                    throw new InvalidOperationException("IGDB external game id was not positive.");
                }

                if (uidToExternalIds.TryGetValue(uid, out var externalIds))
                {
                    resolved.AddRange(externalIds.Select(externalId => (gameId, externalId)));
                }
            }

            return (IReadOnlyList<(long GameId, string ExternalId)>)resolved;
        });

    private static void ValidateGame(JsonElement game)
    {
        var id = game.GetProperty("id").GetInt64();
        var name = game.GetProperty("name").GetString();

        if (id <= 0)
        {
            throw new InvalidOperationException("IGDB game id was not positive.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("IGDB game name was null or blank.");
        }
    }

    private static T MapSchema<T>(string endpoint, Func<T> map)
    {
        try
        {
            return map();
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or FormatException or ArgumentOutOfRangeException)
        {
            throw new ProviderException(ProviderKey, $"IGDB {endpoint} response had an invalid schema: {ex.Message}", ex);
        }
    }

    private async Task<JsonElement> QueryAsync(string endpoint, string body, CancellationToken ct)
    {
        var response = await SendAsync(endpoint, body, ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            tokenProvider.Invalidate();
            response = await SendAsync(endpoint, body, ct);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException(
                    ProviderKey, $"IGDB returned HTTP {(int)response.StatusCode} for {endpoint}.");
            }

            try
            {
                var payload = await response.Content.ReadAsStringAsync(ct);
                return JsonDocument.Parse(payload).RootElement.Clone();
            }
            catch (Exception ex) when (!ct.IsCancellationRequested
                                       && ex is JsonException or HttpRequestException or TaskCanceledException)
            {
                throw new ProviderException(ProviderKey, $"IGDB {endpoint} response was unreadable: {ex.Message}", ex);
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string endpoint, string body, CancellationToken ct)
    {
        await rateLimiter.WaitAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

        request.Headers.Add("Client-ID", options.Value.ClientId);
        request.Headers.Add("Authorization", $"Bearer {await tokenProvider.GetAsync(ct)}");

        try
        {
            return await httpClient.SendAsync(request, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
                                   && ex is HttpRequestException or TaskCanceledException)
        {
            throw new ProviderException(ProviderKey, $"IGDB request to {endpoint} failed: {ex.Message}", ex);
        }
    }

    private static string Sanitize(string query)
    {
        var normalized = new string(query
            .Select(c => c is '"' or ';' or '\n' or '\r' ? ' ' : c)
            .ToArray());

        return string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record LookupId(string ExternalId, bool IsSteam, long NumericId);
}
