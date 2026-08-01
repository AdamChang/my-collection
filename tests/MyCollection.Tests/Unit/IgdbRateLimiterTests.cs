using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MyCollection.Infrastructure.Providers.Igdb;

namespace MyCollection.Tests.Unit;

public class IgdbRateLimiterTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 1, 3, 0, 0, TimeSpan.Zero));

    private IgdbRateLimiter CreateSut(int intervalMs = 250) =>
        new(Options.Create(new IgdbOptions { MinRequestIntervalMs = intervalMs }), _time);

    [Fact]
    public async Task First_call_passes_immediately()
    {
        using var sut = CreateSut();

        var wait = sut.WaitAsync(CancellationToken.None);

        wait.IsCompleted.Should().BeTrue();
        await wait;
    }

    [Fact]
    public async Task Second_call_blocks_until_the_interval_has_elapsed()
    {
        using var sut = CreateSut();
        await sut.WaitAsync(CancellationToken.None);

        var second = sut.WaitAsync(CancellationToken.None);
        second.IsCompleted.Should().BeFalse("未達最小間隔前不應放行");

        _time.Advance(TimeSpan.FromMilliseconds(250));

        await second;
        second.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task Second_call_passes_immediately_when_enough_time_already_passed()
    {
        using var sut = CreateSut();
        await sut.WaitAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(1));

        var second = sut.WaitAsync(CancellationToken.None);
        second.IsCompleted.Should().BeTrue();
        await second;
    }

    [Fact]
    public async Task A_zero_interval_disables_throttling()
    {
        using var sut = CreateSut(intervalMs: 0);

        await sut.WaitAsync(CancellationToken.None);
        var second = sut.WaitAsync(CancellationToken.None);

        second.IsCompleted.Should().BeTrue();
        await second;
    }
}
