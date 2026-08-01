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
        new() { Key = "coverUrl", Label = "IGDB 封面網址", Type = FieldType.Url }
    ];
}
