using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MyCollection.Api;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class GlobalExceptionHandlerTests
{
    private readonly Mock<IProblemDetailsService> _problemDetails = new();

    private ProblemDetailsContext? Captured { get; set; }

    private GlobalExceptionHandler CreateSut()
    {
        _problemDetails
            .Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(c => Captured = c)
            .ReturnsAsync(true);

        return new GlobalExceptionHandler(_problemDetails.Object, NullLogger<GlobalExceptionHandler>.Instance);
    }

    private async Task<int> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        await CreateSut().TryHandleAsync(context, exception, CancellationToken.None);
        return context.Response.StatusCode;
    }

    [Fact]
    public async Task NotFoundException_maps_to_404() =>
        (await HandleAsync(new NotFoundException("Item", "x"))).Should().Be(404);

    [Fact]
    public async Task ForbiddenException_maps_to_403() =>
        (await HandleAsync(new ForbiddenException())).Should().Be(403);

    [Fact]
    public async Task ConflictException_maps_to_409() =>
        (await HandleAsync(new ConflictException("dup"))).Should().Be(409);

    [Fact]
    public async Task ProviderException_maps_to_502() =>
        (await HandleAsync(new ProviderException("steam", "boom"))).Should().Be(502);

    [Fact]
    public async Task Unknown_exception_maps_to_500_without_leaking_details()
    {
        var status = await HandleAsync(new InvalidOperationException("internal detail"));

        status.Should().Be(500);
        Captured!.ProblemDetails.Detail.Should().NotContain("internal detail");
    }

    [Fact]
    public async Task ValidationException_maps_to_400_with_errors_extension()
    {
        var failures = new[]
        {
            new ValidationFailure("Email", "Email is required"),
            new ValidationFailure("Email", "Email is invalid"),
            new ValidationFailure("Password", "Password too short")
        };

        var status = await HandleAsync(new ValidationException(failures));

        status.Should().Be(400);
        Captured!.ProblemDetails.Extensions.Should().ContainKey("errors");
        var errors = (IDictionary<string, string[]>)Captured.ProblemDetails.Extensions["errors"]!;
        errors["Email"].Should().BeEquivalentTo("Email is required", "Email is invalid");
        errors["Password"].Should().BeEquivalentTo("Password too short");
    }
}
