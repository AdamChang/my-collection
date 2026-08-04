using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Providers;

/// <summary>
/// Steam 商店補完寫入的 attribute 欄位定義，唯一來源。
/// SystemCategoryDefinitions 與 SteamProvider.RequiredFields 都由此取得。
///
/// 兩個 key 的分工是刻意的：
/// AppIdKey 是「這款遊戲在 Steam 的識別碼」，IGDB 補完反查得到時也會寫；
/// StoreUpdatedAtKey 是「商店繁中資料抓過了」，只有本 provider 會寫，
/// 批次補完靠它排除已處理的品項。合成一個 key 的話，IGDB 一寫 appId
/// 就等於宣告商店補完做完了，該品項會被永久跳過。
///
/// genres 列在這裡是因為商店補完會寫它（繁體中文），品類必須宣告；
/// 定義與 IgdbFields 的同名欄位一致，兩者指的是同一個欄位。
/// </summary>
public static class SteamFields
{
    public const string AppIdKey = "steamAppId";
    public const string StoreUpdatedAtKey = "steamStoreUpdatedAt";
    public const string GenresKey = "genres";

    /// <summary>唯讀快照，供只需要讀 Key/Type 的呼叫端。</summary>
    public static IReadOnlyList<CategoryField> All { get; } = Create();

    /// <summary>
    /// 回傳可安全交給呼叫端持有的新實例。CategoryField 是可變類別，
    /// 直接共用 All 會讓寫進資料庫的物件被別處改到。
    /// </summary>
    public static List<CategoryField> Create() =>
    [
        CreateAppIdField(),
        new() { Key = StoreUpdatedAtKey, Label = "Steam 資料更新時間", Type = FieldType.Date },
        new() { Key = GenresKey, Label = "類型", Type = FieldType.Text, Searchable = true }
    ];

    /// <summary>
    /// IGDB 補完反查到 Steam 對應時也會寫這個欄位，所以它的定義要能被 IgdbFields 取用。
    /// 兩處各寫一份字面值就會漂移。
    /// </summary>
    public static CategoryField CreateAppIdField() =>
        new() { Key = AppIdKey, Label = "Steam App ID", Type = FieldType.Number };
}
