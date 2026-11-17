using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Auth;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class InvitationCommandTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 3, 0, 0, DateTimeKind.Utc);
    private readonly Mock<IInvitationRepository> _invitations = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Now));

    [Theory]
    [InlineData("", 7)]
    [InlineData("not-an-email", 7)]
    [InlineData("a@b.c", 0)]
    [InlineData("a@b.c", 31)]
    public void Create_validator_rejects_invalid_input(string email, int days)
    {
        new CreateInvitationCommandValidator().Validate(new CreateInvitationCommand(email, days))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Create_rejects_email_that_is_already_registered_without_revoking_anything()
    {
        _users.Setup(u => u.GetByEmailAsync("friend@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Email = "friend@example.com", PasswordHash = "h", DisplayName = "F" });

        var act = () => new CreateInvitationCommandHandler(_invitations.Object, _users.Object, _time)
            .Handle(new CreateInvitationCommand(" Friend@Example.com ", 7), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        _invitations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_revokes_previous_pending_then_inserts_normalised_invitation()
    {
        Invitation? inserted = null;
        var sequence = new MockSequence();
        _invitations.InSequence(sequence)
            .Setup(r => r.RevokePendingAsync("friend@example.com", Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _invitations.InSequence(sequence)
            .Setup(r => r.InsertAsync(It.IsAny<Invitation>(), It.IsAny<CancellationToken>()))
            .Callback<Invitation, CancellationToken>((i, _) => inserted = i)
            .Returns(Task.CompletedTask);

        var dto = await new CreateInvitationCommandHandler(_invitations.Object, _users.Object, _time)
            .Handle(new CreateInvitationCommand(" Friend@Example.com ", 3), CancellationToken.None);

        inserted!.Email.Should().Be("friend@example.com");
        inserted.Code.Should().HaveLength(InvitationCode.Length);
        inserted.Status.Should().Be(InvitationStatus.Pending);
        inserted.ExpiresAt.Should().Be(Now.AddDays(3));
        dto.Code.Should().Be(InvitationCode.Format(inserted.Code));
        dto.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task Revoke_returns_number_of_revoked_invitations()
    {
        _invitations.Setup(r => r.RevokePendingAsync("friend@example.com", Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var revoked = await new RevokeInvitationCommandHandler(_invitations.Object, _time)
            .Handle(new RevokeInvitationCommand("Friend@example.com"), CancellationToken.None);

        revoked.Should().Be(1);
    }

    [Fact]
    public async Task List_derives_expired_from_pending_past_expiry()
    {
        var expired = new Invitation
        {
            Id = ObjectId.GenerateNewId(), Email = "a@example.com", Code = "ABCDEFGHJKMNPQRSTVWX",
            Status = InvitationStatus.Pending, CreatedAt = Now.AddDays(-8), ExpiresAt = Now.AddDays(-1)
        };
        _invitations.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([expired]);

        var list = await new ListInvitationsQueryHandler(_invitations.Object, _time)
            .Handle(new ListInvitationsQuery(), CancellationToken.None);

        list.Single().Status.Should().Be("Expired");
        list.Single().Code.Should().Be("ABCDE-FGHJK-MNPQR-STVWX");
    }
}
