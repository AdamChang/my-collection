using Microsoft.Extensions.Options;

namespace MyCollection.Infrastructure.Providers;

/// <summary>
/// Steam 商店 appdetails 的節流器。實測上限約 200 req/5 min，遠比 Web API 嚴格，
/// 而且 appdetails 不支援一次查多個 appid（帶多個只會回 null），
/// 所以補完一整套遊戲庫必然是逐款請求，節流是這條路徑的主要成本來源。
/// 註冊為 singleton——每個請求各自一份節流器等於沒有節流。
/// </summary>
public sealed class SteamStoreRateLimiter(
    IOptions<SteamOptions> options,
    TimeProvider timeProvider) : IDisposable
{
    private readonly MinIntervalThrottle _throttle = new(timeProvider);

    public Task WaitAsync(CancellationToken ct) =>
        _throttle.WaitAsync(TimeSpan.FromMilliseconds(options.Value.StoreMinRequestIntervalMs), ct);

    public void Dispose() => _throttle.Dispose();
}
