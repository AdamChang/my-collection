using AngleSharp.Html.Parser;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Providers;

/// <summary>
/// 抓任意商品頁的 og:* 標籤。涵蓋多數手動建檔的填表痛苦，且不依賴任何官方 API。
/// </summary>
public sealed class OpenGraphProvider(HttpClient httpClient) : IUrlLookupProvider
{
    public const string ProviderKey = ProviderKeys.OpenGraph;

    private static readonly HtmlParser Parser = new();

    public string Key => ProviderKey;

    public async Task<ExternalItem?> FetchByUrlAsync(Uri url, CancellationToken ct)
    {
        string html;
        try
        {
            var response = await httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException(ProviderKey, $"Fetching '{url}' returned HTTP {(int)response.StatusCode}.");
            }

            html = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ProviderException(ProviderKey, $"Fetching '{url}' failed: {ex.Message}", ex);
        }

        var document = await Parser.ParseDocumentAsync(html, ct);

        string? Meta(string property) =>
            document.QuerySelector($"meta[property='{property}']")?.GetAttribute("content")
            ?? document.QuerySelector($"meta[name='{property}']")?.GetAttribute("content");

        var name = Meta("og:title") ?? document.Title;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var attributes = new Dictionary<string, object?>();
        if (Meta("og:site_name") is { Length: > 0 } siteName)
        {
            attributes["siteName"] = siteName;
        }

        if (Meta("og:type") is { Length: > 0 } type)
        {
            attributes["ogType"] = type;
        }

        return new ExternalItem(
            ExternalId: url.ToString(),
            Name: name.Trim(),
            Description: Meta("og:description")?.Trim(),
            ImageUrl: ResolveImage(Meta("og:image"), url),
            Attributes: attributes)
        {
            SourceUrl = url
        };
    }

    private static Uri? ResolveImage(string? value, Uri pageUrl) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Uri.TryCreate(pageUrl, value, out var absolute) ? absolute : null;
}
