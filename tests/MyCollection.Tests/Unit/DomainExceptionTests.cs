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

    [Fact]
    public void ForbiddenException_has_a_default_message()
    {
        new ForbiddenException().Message.Should().Be("Access to the requested resource is denied.");
    }

    [Fact]
    public void ForbiddenException_accepts_a_custom_message()
    {
        new ForbiddenException("Invalid email or password.").Message.Should().Be("Invalid email or password.");
    }

    [Fact]
    public void ConflictException_carries_its_message()
    {
        new ConflictException("Email is already registered.").Message.Should().Be("Email is already registered.");
    }

    [Fact]
    public void UnreadableCredentialException_tells_the_user_how_to_recover()
    {
        var inner = new InvalidOperationException("tag mismatch");

        var ex = new UnreadableCredentialException(innerException: inner);

        ex.Message.Should().Contain("re-link");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void ProviderException_preserves_the_inner_exception()
    {
        var inner = new HttpRequestException("timeout");

        var ex = new ProviderException("steam", "Steam request failed", inner);

        ex.InnerException.Should().BeSameAs(inner);
    }
}
