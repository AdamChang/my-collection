using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Providers.Psn;

public sealed class PsnProvider(
    HttpClient httpClient,
    ISecretProtector secretProtector,
    IOptions<PsnOptions> options) : IBulkSyncProvider
{
    public const string ProviderKey = ProviderKeys.Psn;

    private const string ClientId = "09515159-7237-4370-9b40-3806e67c0891";
    private const string RedirectUri = "com.scee.psxandroid.scecompcall://redirect";
    private const string Scope = "psn:mobile.v2.core psn:clientapp";
    private const string MobileClientAuthorization =
        "MDk1MTUxNTktNzIzNy00MzcwLTliNDAtMzgwNmU2N2MwODkxOnVjUGprYTV0bnRCMktxc1A=";

    private readonly PsnOptions _options = options.Value;

    public string Key => ProviderKey;

    public async Task<IReadOnlyList<ExternalItem>> SyncAsync(ExternalAccount account, CancellationToken ct)
    {
        try
        {
            return await SyncCoreAsync(account, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ProviderException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   or JsonException
                                   or InvalidOperationException
                                   or FormatException
                                   or OverflowException
                                   or OperationCanceledException)
        {
            throw new ProviderException(ProviderKey, $"PSN request failed: {ex.Message}", ex);
        }
    }

    private async Task<IReadOnlyList<ExternalItem>> SyncCoreAsync(ExternalAccount account, CancellationToken ct)
    {
        var npsso = secretProtector.Unprotect(account.ProtectedApiKey);
        var authorizationCode = await GetAuthorizationCodeAsync(npsso, ct);
        var accessToken = await ExchangeCodeAsync(authorizationCode, ct);
        var items = new List<ExternalItem>();
        var offset = 0;
        int? knownTotal = null;

        while (true)
        {
            var page = await GetTrophyTitlesPageAsync(accessToken, offset, ct);
            var titles = page.TrophyTitles
                         ?? throw InvalidSchema("trophyTitles was missing or null.");

            if (titles.Count > _options.TrophyTitlePageSize)
            {
                throw InvalidSchema("a page contained more items than the requested limit.");
            }

            if (page.TotalItemCount is < 0)
            {
                throw InvalidSchema("totalItemCount was negative.");
            }

            if (page.TotalItemCount is { } pageTotal)
            {
                if (knownTotal is { } existingTotal && pageTotal != existingTotal)
                {
                    throw InvalidSchema("totalItemCount changed between pages.");
                }

                knownTotal ??= pageTotal;
            }

            items.AddRange(titles.Select(ToExternalItem));

            if (knownTotal is { } total && items.Count > total)
            {
                throw InvalidSchema("totalItemCount was inconsistent with the returned items.");
            }

            var pageIsShort = titles.Count < _options.TrophyTitlePageSize;
            var totalReached = knownTotal is { } totalCount && items.Count >= totalCount;
            if (totalReached || pageIsShort && knownTotal is null)
            {
                break;
            }

            if (titles.Count == 0)
            {
                throw InvalidSchema("an empty page did not reach totalItemCount.");
            }

            if (pageIsShort && page.NextOffset is null)
            {
                throw InvalidSchema("a short page required another page but did not supply nextOffset.");
            }

            var nextOffset = page.NextOffset ?? checked(offset + _options.TrophyTitlePageSize);
            if (nextOffset <= offset)
            {
                throw InvalidSchema("nextOffset did not advance.");
            }

            offset = nextOffset;
        }

        return items;
    }

    private async Task<string> GetAuthorizationCodeAsync(string npsso, CancellationToken ct)
    {
        var requestUri = Combine(
            _options.OAuthBaseAddress,
            "authorize" +
            "?access_type=offline" +
            $"&client_id={Uri.EscapeDataString(ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            "&response_type=code" +
            $"&scope={Uri.EscapeDataString(Scope)}");

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.TryAddWithoutValidation("Cookie", $"npsso={npsso}");

        using var response = await httpClient.SendAsync(request, ct);
        if (IsExpiredCredentialStatus(response.StatusCode))
        {
            throw ExpiredNpsso();
        }

        var isRedirect = response.StatusCode is >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest;
        if (!isRedirect)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException(
                    ProviderKey,
                    $"PSN authorization returned HTTP {(int)response.StatusCode}.");
            }

            throw ExpiredNpsso();
        }

        var location = response.Headers.Location ?? throw ExpiredNpsso();
        var code = ParseLocationQueryValue(location, "code");

        return !string.IsNullOrWhiteSpace(code)
            ? code
            : throw ExpiredNpsso();
    }

    private async Task<string> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Combine(_options.OAuthBaseAddress, "token"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", MobileClientAuthorization);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["grant_type"] = "authorization_code",
            ["token_format"] = "jwt"
        });

        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden)
        {
            throw ExpiredNpsso();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderException(
                ProviderKey,
                $"PSN token exchange returned HTTP {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);

        return !string.IsNullOrWhiteSpace(payload?.AccessToken)
            ? payload.AccessToken
            : throw InvalidSchema("token response did not contain a nonblank access_token.");
    }

    private async Task<TrophyTitlesPage> GetTrophyTitlesPageAsync(
        string accessToken, int offset, CancellationToken ct)
    {
        var requestUri = Combine(
            _options.TrophyBaseAddress,
            $"users/me/trophyTitles?limit={_options.TrophyTitlePageSize}&offset={offset}");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderException(
                ProviderKey,
                $"PSN Trophy Titles returned HTTP {(int)response.StatusCode}.");
        }

        return await response.Content.ReadFromJsonAsync<TrophyTitlesPage>(ct)
               ?? throw new InvalidOperationException("PSN Trophy response was empty.");
    }

    private static ExternalItem ToExternalItem(TrophyTitle? title)
    {
        if (title is null
            || string.IsNullOrWhiteSpace(title.NpCommunicationId)
            || string.IsNullOrWhiteSpace(title.TrophyTitleName)
            || string.IsNullOrWhiteSpace(title.TrophyTitleIconUrl)
            || string.IsNullOrWhiteSpace(title.TrophyTitlePlatform)
            || title.Progress is null)
        {
            throw InvalidSchema("a Trophy Title required field was null or blank.");
        }

        if (!Uri.TryCreate(title.TrophyTitleIconUrl, UriKind.Absolute, out var imageUrl))
        {
            throw InvalidSchema("trophyTitleIconUrl was not an absolute URI.");
        }

        if (!DateTimeOffset.TryParse(
                title.LastUpdatedDateTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var lastUpdated))
        {
            throw InvalidSchema("lastUpdatedDateTime was invalid.");
        }

        return new ExternalItem(
            title.NpCommunicationId,
            title.TrophyTitleName,
            title.TrophyTitleDetail,
            imageUrl,
            new Dictionary<string, object?>
            {
                ["iconUrl"] = title.TrophyTitleIconUrl,
                ["platform"] = title.TrophyTitlePlatform,
                [PsnFields.ProgressKey] = title.Progress.Value,
                [PsnFields.LastPlayedAtKey] = lastUpdated.UtcDateTime
            })
        {
            SourceUrl = null,
            FillOnlyIfAbsent = new HashSet<string>(["platform"], StringComparer.Ordinal)
        };
    }

    private static Uri Combine(string baseAddress, string relative) =>
        new(new Uri(baseAddress, UriKind.Absolute), relative);

    private static string? ParseQueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), key, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private static string? ParseLocationQueryValue(Uri location, string key)
    {
        var locationText = location.OriginalString;
        var queryStart = locationText.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
        {
            return null;
        }

        var query = locationText[(queryStart + 1)..];
        var fragmentStart = query.IndexOf('#', StringComparison.Ordinal);
        if (fragmentStart >= 0)
        {
            query = query[..fragmentStart];
        }

        return ParseQueryValue(query, key);
    }

    private static bool IsExpiredCredentialStatus(HttpStatusCode status) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    private static ProviderException ExpiredNpsso() =>
        new(ProviderKey, "NPSSO 已過期，請重新取得");

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken);

    private static ProviderException InvalidSchema(string detail) =>
        new(ProviderKey, $"PSN Trophy response had an invalid schema: {detail}");

    private sealed record TrophyTitlesPage(
        [property: JsonPropertyName("trophyTitles")] IReadOnlyList<TrophyTitle?>? TrophyTitles,
        [property: JsonPropertyName("totalItemCount")] int? TotalItemCount,
        [property: JsonPropertyName("nextOffset")] int? NextOffset);

    private sealed record TrophyTitle(
        [property: JsonPropertyName("npCommunicationId")] string? NpCommunicationId,
        [property: JsonPropertyName("trophyTitleName")] string? TrophyTitleName,
        [property: JsonPropertyName("trophyTitleDetail")] string? TrophyTitleDetail,
        [property: JsonPropertyName("trophyTitleIconUrl")] string? TrophyTitleIconUrl,
        [property: JsonPropertyName("trophyTitlePlatform")] string? TrophyTitlePlatform,
        [property: JsonPropertyName("progress")] int? Progress,
        [property: JsonPropertyName("lastUpdatedDateTime")] string? LastUpdatedDateTime);
}
