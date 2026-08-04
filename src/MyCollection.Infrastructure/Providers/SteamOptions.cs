namespace MyCollection.Infrastructure.Providers;

public sealed class SteamOptions
{
    public const string SectionName = "Steam";

    public string BaseAddress { get; init; } = "https://api.steampowered.com/";

    /// <summary>
    /// appdetails 在商店主機上，與 Web API 不同源，所以是第二個 HttpClient。
    /// 這支端點沒有官方文件也沒有 SLA，Valve 隨時可能改動——
    /// 但它是唯一同時提供繁體中文品名、簡介與類型的來源。
    /// </summary>
    public string StoreBaseAddress { get; init; } = "https://store.steampowered.com/";

    /// <summary>
    /// Steam 自有的語言代碼，非 BCP 47。沒有繁中版的遊戲，商店會自動退回原文，
    /// 不必在我們這端寫退回邏輯。
    /// </summary>
    public string StoreLanguage { get; init; } = "tchinese";

    public string StoreCountryCode { get; init; } = "tw";

    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>
    /// 兩次商店請求之間的最小間隔。實測上限約 200 req/5 min，
    /// 撞上去會拿到 429 且整段時間被擋，代價不對稱，所以自我節流。
    /// </summary>
    public int StoreMinRequestIntervalMs { get; init; } = 1500;
}
