using FluentAssertions;
using FluentValidation;
using MediatR;
using MyCollection.Application.Common;

namespace MyCollection.Tests.Unit;

public class ValidationBehaviorTests
{
    public record Ping(string Message) : IRequest<string>;

    private sealed class PingValidator : AbstractValidator<Ping>
    {
        public PingValidator() => RuleFor(x => x.Message).NotEmpty().WithMessage("Message is required");
    }

    // MediatR 14 的 RequestHandlerDelegate<T> 帶 CancellationToken 參數
    private static Task<string> Next(CancellationToken _) => Task.FromResult("pong");

    [Fact]
    public async Task Passes_through_when_no_validators_registered()
    {
        var sut = new ValidationBehavior<Ping, string>([]);

        var result = await sut.Handle(new Ping(""), Next, CancellationToken.None);

        result.Should().Be("pong");
    }

    [Fact]
    public async Task Passes_through_when_valid()
    {
        var sut = new ValidationBehavior<Ping, string>([new PingValidator()]);

        var result = await sut.Handle(new Ping("hi"), Next, CancellationToken.None);

        result.Should().Be("pong");
    }

    [Fact]
    public async Task Throws_ValidationException_when_invalid()
    {
        var sut = new ValidationBehavior<Ping, string>([new PingValidator()]);

        var act = () => sut.Handle(new Ping(""), Next, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("Message is required");
    }
}
