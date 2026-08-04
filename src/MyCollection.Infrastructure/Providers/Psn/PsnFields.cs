using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Providers.Psn;

/// <summary>
/// PSN 獎盃同步寫入的 attribute 欄位定義，唯一來源。
/// SystemCategoryDefinitions 由此取得，避免品類 schema 與 provider 寫入內容漂移。
/// </summary>
public static class PsnFields
{
    public const string ProgressKey = "psnProgress";
    public const string LastPlayedAtKey = "psnLastPlayedAt";

    /// <summary>唯讀快照，供只需要讀取欄位定義的呼叫端。</summary>
    public static IReadOnlyList<CategoryField> All { get; } = Create();

    /// <summary>
    /// 回傳可安全交給呼叫端持有的新實例。CategoryField 是可變類別，
    /// 直接共用 All 會讓寫進資料庫的物件被別處改到。
    /// </summary>
    public static List<CategoryField> Create() =>
    [
        new()
        {
            Key = ProgressKey,
            Label = "PSN 獎盃完成度",
            Type = FieldType.Number,
            Required = false,
            ShowOnCard = true
        },
        new() { Key = LastPlayedAtKey, Label = "PSN 最後遊玩時間", Type = FieldType.Date, Required = false }
    ];
}
