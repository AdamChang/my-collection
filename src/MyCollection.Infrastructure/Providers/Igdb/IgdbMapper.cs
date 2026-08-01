using System.Globalization;
using System.Text.Json;
using MyCollection.Application.Ingestion;

namespace MyCollection.Infrastructure.Providers.Igdb;

/// <summary>
/// IGDB JSON → ExternalItem。
/// 缺席的欄位一律省略 key，不寫 null：AttributeValidator 把 null 視同未提供，
/// 但寫進 Mongo 的 null 會在文件裡留下噪音，且無法與「使用者清空」區分。
/// </summary>
public static class IgdbMapper
{
    private const string CoverUrlTemplate = "https://images.igdb.com/igdb/image/upload/t_cover_big/{0}.jpg";

    public static ExternalItem ToExternalItem(JsonElement game)
    {
        var id = game.GetProperty("id").GetInt64();
        var coverUrl = CoverUrl(game);

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [IgdbFields.MarkerKey] = id
        };

        Add(attributes, "developer", Company(game, wantDeveloper: true));
        Add(attributes, "publisher", Company(game, wantDeveloper: false));
        Add(attributes, "genres", Join(game, "genres", "name"));
        Add(attributes, "platforms", Join(game, "platforms", "abbreviation"));
        Add(attributes, "coverUrl", coverUrl?.ToString());

        if (Property(game, "first_release_date") is { } released)
        {
            attributes["releaseDate"] = DateTimeOffset.FromUnixTimeSeconds(released.GetInt64()).UtcDateTime;
        }

        if (Property(game, "total_rating") is { } rating)
        {
            attributes["igdbRating"] = Math.Round(rating.GetDouble(), 1);
        }

        return new ExternalItem(
            ExternalId: id.ToString(CultureInfo.InvariantCulture),
            Name: game.GetProperty("name").GetString()!,
            Description: Text(game, "summary"),
            ImageUrl: coverUrl,
            Attributes: attributes)
        {
            SourceUrl = Text(game, "url") is { } url ? new Uri(url) : null
        };
    }

    private static void Add(Dictionary<string, object?> attributes, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            attributes[key] = value;
        }
    }

    /// <summary>缺席與 JSON null 都當作沒有。</summary>
    private static JsonElement? Property(JsonElement element, string name) =>
        element.ValueKind is JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind is not JsonValueKind.Null
            ? value
            : null;

    private static string? Text(JsonElement element, string name) =>
        Property(element, name)?.GetString();

    private static Uri? CoverUrl(JsonElement game)
    {
        if (Property(game, "cover") is not { } cover || Text(cover, "image_id") is not { } imageId)
        {
            return null;
        }

        return new Uri(string.Format(CultureInfo.InvariantCulture, CoverUrlTemplate, imageId));
    }

    private static string? Join(JsonElement game, string arrayName, string property)
    {
        if (Property(game, arrayName) is not { ValueKind: JsonValueKind.Array } array)
        {
            return null;
        }

        var values = array.EnumerateArray()
            .Select(element => Text(element, property))
            .Where(value => !string.IsNullOrWhiteSpace(value));

        var joined = string.Join(", ", values);

        return joined.Length == 0 ? null : joined;
    }

    private static string? Company(JsonElement game, bool wantDeveloper)
    {
        if (Property(game, "involved_companies") is not { ValueKind: JsonValueKind.Array } array)
        {
            return null;
        }

        var role = wantDeveloper ? "developer" : "publisher";

        return array.EnumerateArray()
            .Where(entry => Property(entry, role)?.GetBoolean() == true)
            .Select(entry => Property(entry, "company") is { } company ? Text(company, "name") : null)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
    }
}
