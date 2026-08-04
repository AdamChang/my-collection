using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Providers.Igdb;

/// <summary>
/// IGDB 寫入的 attribute 欄位定義，唯一來源。
/// SystemCategoryDefinitions 與 IgdbProvider.RequiredFields 都由此取得，避免兩處各寫一份而漂移。
///
/// developer / publisher / releaseDate 三個 key 兩個系統遊戲品類本來就有，
/// 標籤沿用既有的（「發售日期」而非「發行日」），不另立同義欄位。
/// 沒有任何欄位設 Required：IGDB 資料缺漏很常見，設了會讓使用者之後每次更新都失敗。
/// </summary>
public static class IgdbFields
{
    public const string MarkerKey = "igdbId";

    /// <summary>
    /// IGDB 讓位的欄位：品項已有值時不寫。
    ///
    /// genres 與 description 的繁體中文版由 Steam 商店補完提供，IGDB 只有英文；
    /// 讓位讓兩者的執行順序不再影響結果。steamAppId 讓位是因為 Steam 商店補完
    /// 才是它的權威來源，IGDB 反查只是替沒有 externalRef 的品項（例如手動建檔的
    /// 實體遊戲）補上入口。
    /// </summary>
    public static IReadOnlySet<string> SoftWriteKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "genres",
        SteamFields.AppIdKey,
        ItemFieldKeys.Description
    };

    /// <summary>唯讀快照，供只需要讀 Key/Type 的呼叫端。</summary>
    public static IReadOnlyList<CategoryField> All { get; } = Create();

    /// <summary>
    /// 回傳可安全交給呼叫端持有的新實例。CategoryField 是可變類別，
    /// 直接共用 All 會讓 SystemCategoryDefinitions 寫進資料庫的物件被別處改到。
    /// </summary>
    public static List<CategoryField> Create() =>
    [
        new() { Key = MarkerKey, Label = "IGDB ID", Type = FieldType.Number },
        new() { Key = "developer", Label = "開發商", Type = FieldType.Text, Searchable = true },
        new() { Key = "publisher", Label = "發行商", Type = FieldType.Text, Searchable = true },
        new() { Key = "releaseDate", Label = "發售日期", Type = FieldType.Date },
        new() { Key = "genres", Label = "類型", Type = FieldType.Text, Searchable = true },
        new() { Key = "platforms", Label = "發行平台", Type = FieldType.Text, Searchable = true },
        new() { Key = "igdbRating", Label = "IGDB 評分", Type = FieldType.Number },
        new() { Key = "coverUrl", Label = "IGDB 封面網址", Type = FieldType.Url },

        // IGDB 反查得到 Steam 對應時會寫入，好讓非 Steam 同步而來的品項也能被商店補完定址。
        // 定義取自 SteamFields，不在這裡重寫一份字面值。
        SteamFields.CreateAppIdField()
    ];
}
