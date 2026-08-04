using System.Globalization;
using System.Text.Json;
using MyCollection.Application.Ingestion;

namespace MyCollection.Infrastructure.Providers;

/// <summary>
/// Steam 商店 appdetails JSON → ExternalItem。
///
/// 沒有繁體中文版的遊戲，商店會直接回傳原文品名（實測：Cyberpunk 2077 帶 l=tchinese
/// 仍回英文，ELDEN RING 則回「艾爾登法環」）。退回邏輯在 Valve 那端，
/// 我們不判斷、也不把「回來的是英文」當成失敗。
///
/// 缺席的欄位一律省略 key，不寫 null——與 IgdbMapper 同一個約定。
/// </summary>
public static class SteamStoreMapper
{
    public static ExternalItem ToExternalItem(JsonElement data, DateTime fetchedAt)
    {
        var appId = data.GetProperty("steam_appid").GetInt64();
        var name = data.GetProperty("name").GetString();

        if (appId <= 0)
        {
            throw new InvalidOperationException("Steam store app id was not positive.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Steam store app name was null or blank.");
        }

        var headerUrl = Text(data, "header_image");

        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [SteamFields.AppIdKey] = appId,
            [SteamFields.StoreUpdatedAtKey] = fetchedAt
        };

        if (Genres(data) is { } genres)
        {
            attributes[SteamFields.GenresKey] = genres;
        }

        return new ExternalItem(
            ExternalId: appId.ToString(CultureInfo.InvariantCulture),
            Name: name,
            Description: Text(data, "short_description"),
            ImageUrl: headerUrl is null ? null : new Uri(headerUrl),
            Attributes: attributes)
        {
            SourceUrl = new Uri($"https://store.steampowered.com/app/{appId.ToString(CultureInfo.InvariantCulture)}")

            // FillOnlyIfAbsent 刻意留空：本地化補完擁有它寫的每一個欄位。
            // 這條路徑存在的唯一理由就是把既有的英文換成繁體中文，讓位等於什麼都不做。
        };
    }

    /// <summary>缺席與 JSON null 都當作沒有。</summary>
    private static JsonElement? Property(JsonElement element, string name) =>
        element.ValueKind is JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind is not JsonValueKind.Null
            ? value
            : null;

    private static string? Text(JsonElement element, string name)
    {
        var value = Property(element, name)?.GetString();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>類型一律本地化，這是這條路徑相對 IGDB 的主要價值之一。</summary>
    private static string? Genres(JsonElement data)
    {
        if (Property(data, "genres") is not { ValueKind: JsonValueKind.Array } array)
        {
            return null;
        }

        var joined = string.Join(", ", array.EnumerateArray()
            .Select(entry => Text(entry, "description"))
            .Where(value => value is not null));

        return joined.Length == 0 ? null : joined;
    }
}
