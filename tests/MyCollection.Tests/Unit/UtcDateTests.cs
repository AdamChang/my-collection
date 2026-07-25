using FluentAssertions;
using MyCollection.Application.Common;

namespace MyCollection.Tests.Unit;

public class UtcDateTests
{
    [Fact]
    public void Treats_naive_input_as_utc_without_shifting_the_clock()
    {
        var naive = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var result = UtcDate.Normalise(naive);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Converts_local_input_to_utc()
    {
        var local = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);

        var result = UtcDate.Normalise(local);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(local.ToUniversalTime());
    }

    [Fact]
    public void Leaves_utc_input_untouched()
    {
        var utc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        UtcDate.Normalise(utc).Should().Be(utc);
    }

    [Fact]
    public void Passes_null_through()
    {
        UtcDate.Normalise((DateTime?)null).Should().BeNull();
    }
}
