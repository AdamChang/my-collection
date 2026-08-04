namespace MyCollection.Infrastructure.Providers;

/// <summary>
/// 程序層級的最小間隔節流。IGDB 與 Steam 商店的懲罰都是「撞上去整段時間被擋」，
/// 代價不對稱，所以自我節流而不是等 429 才退避。
///
/// 只有機制在這裡，間隔由各 provider 的節流器提供——兩者的限制值與來源不同，
/// 混成一個共用實例會讓其中一方受另一方拖累。
/// </summary>
public sealed class MinIntervalThrottle(TimeProvider timeProvider) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DateTimeOffset _nextAllowedAt = DateTimeOffset.MinValue;

    public async Task WaitAsync(TimeSpan interval, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = timeProvider.GetUtcNow();
            var remaining = _nextAllowedAt - now;

            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, timeProvider, ct);
                now = timeProvider.GetUtcNow();
            }

            _nextAllowedAt = now + interval;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
