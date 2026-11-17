using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Moq;
using MyCollection.Api.Maintenance;
using MyCollection.Application.Auth;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class InviteMaintenanceTests
{
    private readonly Mock<ISender> _sender = new();
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    private static readonly InvitationDto Dto = new(
        "friend@example.com", "ABCDE-FGHJK-MNPQR-STVWX", "Pending",
        new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
        null, null);

    private Task<int> Run(params string[] args) =>
        InviteMaintenance.RunAsync(args, _sender.Object, "https://collection.example", _out, _err, CancellationToken.None);

    [Fact]
    public async Task Create_prints_code_and_register_link()
    {
        _sender.Setup(s => s.Send(new CreateInvitationCommand("friend@example.com", 3), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Dto);

        var exit = await Run("invite", "create", "--email", "friend@example.com", "--days", "3");

        exit.Should().Be(0);
        _out.ToString().Should().Contain("ABCDE-FGHJK-MNPQR-STVWX")
            .And.Contain("https://collection.example/register?code=ABCDE-FGHJK-MNPQR-STVWX");
    }

    [Theory]
    [InlineData("invite", "create")]
    [InlineData("invite", "create", "--email", "a@b.c", "--days", "x")]
    [InlineData("invite", "delete")]
    [InlineData("other")]
    public async Task Bad_arguments_print_usage_and_exit_2(params string[] args)
    {
        var exit = await Run(args);

        exit.Should().Be(2);
        _err.ToString().Should().Contain("maintenance invite create");
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Conflict_is_reported_with_exit_1()
    {
        _sender.Setup(s => s.Send(It.IsAny<CreateInvitationCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Email 'friend@example.com' is already registered."));

        var exit = await Run("invite", "create", "--email", "friend@example.com");

        exit.Should().Be(1);
        _err.ToString().Should().Contain("already registered");
    }

    [Fact]
    public async Task Validation_failure_is_reported_with_exit_1()
    {
        _sender.Setup(s => s.Send(It.IsAny<CreateInvitationCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationException([new ValidationFailure("Email", "bad email")]));

        var exit = await Run("invite", "create", "--email", "nope");

        exit.Should().Be(1);
        _err.ToString().Should().Contain("bad email");
    }

    [Fact]
    public async Task List_prints_one_line_per_invitation()
    {
        _sender.Setup(s => s.Send(It.IsAny<ListInvitationsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Dto, Dto with { Email = "other@example.com", Status = "Used" }]);

        var exit = await Run("invite", "list");

        exit.Should().Be(0);
        _out.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(2);
    }
}
