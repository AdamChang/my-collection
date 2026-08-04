using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Providers;
using MyCollection.Infrastructure.Providers.Igdb;

namespace MyCollection.Infrastructure.Mongo;

public static class SystemCategoryDefinitions
{
    public static readonly ObjectId PhysicalGameId = ObjectId.Parse("000000000000000000000001");
    public static readonly ObjectId DigitalGameId = ObjectId.Parse("000000000000000000000002");
    public static readonly ObjectId MusicAlbumId = ObjectId.Parse("000000000000000000000003");
    public static readonly ObjectId MovieDiscId = ObjectId.Parse("000000000000000000000004");

    public static IReadOnlyList<Category> Create(DateTime now) =>
    [
        Category(PhysicalGameId, "實體遊戲", "gamepad-2", CategoryKind.Physical, now,
        [
            Text("platform", "平台", searchable: true, showOnCard: true),
            Text("edition", "版本", searchable: true, showOnCard: true),
            Text("region", "區域", searchable: true),
            Select("mediaFormat", "媒體格式", ["光碟", "卡匣", "記憶卡", "其他"], true, true),
            Text("developer", "開發商", searchable: true),
            Text("publisher", "發行商", searchable: true),
            Date("releaseDate", "發售日期"),
            Text("productCode", "產品編號", searchable: true),
            Text("barcode", "條碼", searchable: true),
            Select("condition", "保存狀況", ["全新", "近全新", "良好", "普通", "需修復"], true),
            // 以下為 IGDB 補完寫入的欄位。developer / publisher / releaseDate 上方已宣告。
            // 定義來源是 IgdbFields，這裡只列出上方尚未出現的 key。
            Number(IgdbFields.MarkerKey, "IGDB ID"),
            Text("genres", "類型", searchable: true),
            Text("platforms", "發行平台", searchable: true),
            Number("igdbRating", "IGDB 評分"),
            Url("coverUrl", "IGDB 封面網址"),
            // Steam 商店的繁體中文補完欄位。genres 上方已宣告，兩者指的是同一個欄位。
            // steamAppId 是識別碼來源（IGDB 反查時也會寫），
            // steamStoreUpdatedAt 是完成標記（只有商店補完會寫）。
            Number(SteamFields.AppIdKey, "Steam App ID"),
            Date(SteamFields.StoreUpdatedAtKey, "Steam 資料更新時間")
        ]),
        Category(DigitalGameId, "數位遊戲", "gamepad-2", CategoryKind.Digital, now,
        [
            Text("platform", "平台／商店", searchable: true, showOnCard: true),
            Text("developer", "開發商", searchable: true),
            Text("publisher", "發行商", searchable: true, showOnCard: true),
            Date("releaseDate", "發售日期"),
            Text("productCode", "產品編號", searchable: true),
            Number("playtimeForever", "遊玩時數（分鐘）", showOnCard: true),
            Url("headerUrl", "封面圖網址"),
            Url("iconUrl", "圖示網址"),
            // 同上：IGDB 補完欄位
            Number(IgdbFields.MarkerKey, "IGDB ID"),
            Text("genres", "類型", searchable: true),
            Text("platforms", "發行平台", searchable: true),
            Number("igdbRating", "IGDB 評分"),
            Url("coverUrl", "IGDB 封面網址"),
            // Steam 商店的繁體中文補完欄位。genres 上方已宣告，兩者指的是同一個欄位。
            // steamAppId 是識別碼來源（IGDB 反查時也會寫），
            // steamStoreUpdatedAt 是完成標記（只有商店補完會寫）。
            Number(SteamFields.AppIdKey, "Steam App ID"),
            Date(SteamFields.StoreUpdatedAtKey, "Steam 資料更新時間")
        ]),
        Category(MusicAlbumId, "音樂專輯", "disc-3", CategoryKind.Physical, now,
        [
            Text("artist", "演出者", searchable: true, showOnCard: true),
            Select("mediaFormat", "媒體格式", ["CD", "黑膠唱片", "卡帶", "SACD", "其他"], true, true),
            Select("albumType", "專輯類型", ["專輯", "單曲", "EP", "精選輯", "原聲帶", "其他"], true),
            Text("label", "唱片公司", searchable: true, showOnCard: true),
            Text("catalogNumber", "目錄編號", searchable: true),
            Text("country", "國家／地區", searchable: true),
            Date("releaseDate", "發行日期"),
            Text("genre", "曲風", searchable: true),
            Text("style", "風格", searchable: true),
            Text("barcode", "條碼", searchable: true)
        ]),
        Category(MovieDiscId, "電影光碟", "film", CategoryKind.Physical, now,
        [
            Select("discFormat", "光碟格式", ["Blu-ray", "4K UHD", "DVD", "VCD", "其他"], true, true),
            Text("edition", "版本", searchable: true, showOnCard: true),
            Text("director", "導演", searchable: true, showOnCard: true),
            Text("studio", "片商", searchable: true),
            Text("regionCode", "區碼", searchable: true),
            Text("country", "國家／地區", searchable: true),
            Date("releaseDate", "發行日期"),
            Text("genre", "類型", searchable: true),
            Text("barcode", "條碼", searchable: true)
        ])
    ];

    private static Category Category(
        ObjectId id, string name, string icon, CategoryKind kind, DateTime now, List<CategoryField> fields) =>
        new()
        {
            Id = id,
            OwnerId = null,
            Name = name,
            Icon = icon,
            Kind = kind,
            Fields = fields,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static CategoryField Text(
        string key, string label, bool searchable = false, bool showOnCard = false) =>
        Field(key, label, FieldType.Text, searchable, showOnCard);

    private static CategoryField Date(string key, string label) =>
        Field(key, label, FieldType.Date);

    private static CategoryField Number(string key, string label, bool showOnCard = false) =>
        Field(key, label, FieldType.Number, showOnCard: showOnCard);

    private static CategoryField Url(string key, string label) =>
        Field(key, label, FieldType.Url);

    private static CategoryField Select(
        string key, string label, List<string> options, bool searchable, bool showOnCard = false) =>
        Field(key, label, FieldType.Select, searchable, showOnCard, options);

    private static CategoryField Field(
        string key,
        string label,
        FieldType type,
        bool searchable = false,
        bool showOnCard = false,
        List<string>? options = null) =>
        new()
        {
            Key = key,
            Label = label,
            Type = type,
            Options = options,
            Required = false,
            Searchable = searchable,
            ShowOnCard = showOnCard
        };
}
