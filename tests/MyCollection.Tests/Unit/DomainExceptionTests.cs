using FluentAssertions;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class DomainExceptionTests
{
    [Fact]
    public void NotFoundException_carries_resource_and_key()
    {
        var ex = new NotFoundException("Item", "abc123");

        ex.Resource.Should().Be("Item");
        ex.Key.Should().Be("abc123");
        ex.Message.Should().Be("Item 'abc123' was not found.");
    }

    [Fact]
    public void ProviderException_carries_provider_key()
    {
        var ex = new ProviderException("steam", "rate limited");

        ex.ProviderKey.Should().Be("steam");
        ex.Message.Should().Be("rate limited");
    }
}
