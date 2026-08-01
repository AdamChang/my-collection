using Microsoft.Extensions.Options;

namespace MyCollection.Infrastructure.Providers.Igdb;

/// <summary>
/// IGDB 限制 4 req/sec。撞上去的懲罰是整段時間被擋，代價不對稱，
/// 所以在程序層級自我節流，而不是等到 429 才退避。
/// 註冊為 singleton——每個請求各自一份節流器等於沒有節流。
/// </summary>
public sealed class IgdbRateLimiter(
    IOptions<IgdbOptions> options,
    TimeProvider timeProvider) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DateTimeOffset _nextAllowedAt = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(options.Value.MinRequestIntervalMs);

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
