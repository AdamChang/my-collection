using Microsoft.Extensions.Options;

namespace MyCollection.Infrastructure.Providers.Igdb;

/// <summary>
/// IGDB 限制 4 req/sec。撞上去的懲罰是整段時間被擋，代價不對稱，
/// 所以在程序層級自我節流，而不是等到 429 才退避。
/// 註冊為 singleton——每個請求各自一份節流器等於沒有節流。
///
/// 機制在 <see cref="MinIntervalThrottle"/>；這個型別只綁定 IGDB 的間隔設定，
/// 好讓 DI 把它與 Steam 商店的節流器維持成兩個互不拖累的實例。
/// </summary>
public sealed class IgdbRateLimiter(
    IOptions<IgdbOptions> options,
    TimeProvider timeProvider) : IDisposable
{
    private readonly MinIntervalThrottle _throttle = new(timeProvider);

    public Task WaitAsync(CancellationToken ct) =>
        _throttle.WaitAsync(TimeSpan.FromMilliseconds(options.Value.MinRequestIntervalMs), ct);

    public void Dispose() => _throttle.Dispose();
}
