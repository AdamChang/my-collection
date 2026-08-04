using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Providers;

/// <summary>
/// Steam 商店 appdetails 的存取層。與 Web API 不同主機，所以是獨立的 HttpClient。
///
/// 這支端點沒有官方文件也沒有 SLA。實測特性：
/// - 帶多個 appid 只會回傳 null，因此無法批次，一款一個請求。
/// - 查無此 app 或該地區未上架時回 success:false，這是「查無對應」不是失敗。
/// - 速率上限約 200 req/5 min，逾越會拿到 429 且整段時間被擋。
/// </summary>
public sealed class SteamStoreClient(
    HttpClient httpClient,
    SteamStoreRateLimiter rateLimiter,
    IOptions<SteamOptions> options)
{
    public const string HttpClientName = "steam-store";

    /// <summary>查無此 app（該地區未上架、已下架）時回 null——這不是錯誤。</summary>
    public async Task<JsonElement?> FetchAppDetailsAsync(long appId, CancellationToken ct)
    {
        await rateLimiter.WaitAsync(ct);

        var key = appId.ToString(CultureInfo.InvariantCulture);
        var requestUri =
            $"api/appdetails?appids={key}" +
            $"&l={Uri.EscapeDataString(options.Value.StoreLanguage)}" +
            $"&cc={Uri.EscapeDataString(options.Value.StoreCountryCode)}";

        JsonElement root;
        try
        {
            using var response = await httpClient.GetAsync(requestUri, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException(
                    ProviderKeys.Steam,
                    $"Steam store returned HTTP {(int)response.StatusCode} for app {key}.");
            }

            var payload = await response.Content.ReadAsStringAsync(ct);
            root = JsonDocument.Parse(payload).RootElement.Clone();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
                                   && ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new ProviderException(
                ProviderKeys.Steam, $"Steam store request for app {key} failed: {ex.Message}", ex);
        }

        // 帶多個 appid 時整個回應會是 JSON null，單筆查詢不該走到這裡，
        // 但端點無文件保證，所以當成 schema 問題明確擲出而不是靜靜回 null。
        if (root.ValueKind is not JsonValueKind.Object)
        {
            throw new ProviderException(
                ProviderKeys.Steam, $"Steam store response for app {key} was not an object.");
        }

        if (!root.TryGetProperty(key, out var entry)
            || entry.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        return entry.TryGetProperty("success", out var success) && success.ValueKind is JsonValueKind.True
               && entry.TryGetProperty("data", out var data) && data.ValueKind is JsonValueKind.Object
            ? data
            : null;
    }
}
