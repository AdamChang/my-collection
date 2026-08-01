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
        "involved_companies.developer,involved_companies.publisher;";

    public string Key => ProviderKey;

    public string MarkerAttributeKey => IgdbFields.MarkerKey;

    public IReadOnlyList<CategoryField> RequiredFields { get; } = IgdbFields.All;

    public async Task<IReadOnlyList<ExternalItem>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var effectiveLimit = Math.Clamp(limit, 1, options.Value.SearchLimit);
        var body =
            $"search \"{Sanitize(query)}\";\n" +
            $"{GameFields}\n" +
            "where version_parent = null;\n" +
            $"limit {effectiveLimit.ToString(CultureInfo.InvariantCulture)};";

        var games = await QueryAsync("games", body, ct);

        return games.EnumerateArray().Select(IgdbMapper.ToExternalItem).ToArray();
    }

    public async Task<ExternalLookupResult> FetchByExternalIdsAsync(
        IReadOnlyList<string> externalIds, CancellationToken ct)
    {
        var found = new Dictionary<string, ExternalItem>(StringComparer.Ordinal);
        var failed = new List<string>();
        var recognised = new List<string>();

        foreach (var externalId in externalIds.Distinct(StringComparer.Ordinal))
        {
            if (externalId.StartsWith(SteamPrefix, StringComparison.Ordinal)
                || externalId.StartsWith(IgdbPrefix, StringComparison.Ordinal))
            {
                recognised.Add(externalId);
            }
            else
            {
                logger.LogWarning("Unsupported external id prefix: {ExternalId}", externalId);
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
                failed.AddRange(chunk);
            }
        }

        return new ExternalLookupResult(found, failed);
    }

    private async Task ResolveChunkAsync(
        string[] chunk, Dictionary<string, ExternalItem> found, CancellationToken ct)
    {
        var byGameId = new Dictionary<long, List<string>>();
        var steamIds = chunk
            .Where(id => id.StartsWith(SteamPrefix, StringComparison.Ordinal))
            .ToArray();

        foreach (var externalId in chunk.Where(id => id.StartsWith(IgdbPrefix, StringComparison.Ordinal)))
        {
            if (long.TryParse(externalId[IgdbPrefix.Length..], CultureInfo.InvariantCulture, out var gameId))
            {
                Track(byGameId, gameId, externalId);
            }
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

        foreach (var game in games.EnumerateArray())
        {
            var item = IgdbMapper.ToExternalItem(game);

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
        string[] steamExternalIds, CancellationToken ct)
    {
        var uidToExternalId = steamExternalIds.ToDictionary(
            id => id[SteamPrefix.Length..], id => id, StringComparer.Ordinal);
        var uidList = string.Join(",", uidToExternalId.Keys.Select(uid => $"\"{uid}\""));
        var rows = await QueryAsync(
            "external_games",
            "fields game,uid;\n" +
            $"where external_game_source = {SteamExternalGameSource} & uid = ({uidList});\n" +
            "limit 500;",
            ct);
        var resolved = new List<(long, string)>();

        foreach (var row in rows.EnumerateArray())
        {
            if (row.TryGetProperty("uid", out var uid)
                && uid.GetString() is { } uidValue
                && uidToExternalId.TryGetValue(uidValue, out var externalId)
                && row.TryGetProperty("game", out var game))
            {
                resolved.Add((game.GetInt64(), externalId));
            }
        }

        return resolved;
    }

    private static void Track(Dictionary<long, List<string>> byGameId, long gameId, string externalId)
    {
        if (!byGameId.TryGetValue(gameId, out var list))
        {
            byGameId[gameId] = list = [];
        }

        list.Add(externalId);
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
            .Select(c => c is '\n' or '\r' ? ' ' : c)
            .Where(c => c is not ('"' or ';'))
            .ToArray());

        return string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
