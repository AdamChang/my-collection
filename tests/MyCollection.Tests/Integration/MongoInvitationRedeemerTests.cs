using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Auth;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoInvitationRedeemerTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);
    private const string Email = "friend@example.com";

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private MongoInvitationRedeemer CreateSut() => new(fixture.Context, NullLogger<MongoInvitationRedeemer>.Instance);

    private static User NewUser(string email = Email) => new()
    {
        Id = ObjectId.GenerateNewId(),
        Email = email,
        PasswordHash = "hash",
        DisplayName = "Friend",
        CreatedAt = Now
    };

    private async Task<Invitation> SeedAsync(Action<Invitation>? tweak = null)
    {
        var invitation = new Invitation
        {
            Id = ObjectId.GenerateNewId(),
            Email = Email,
            Code = InvitationCode.Generate(),
            Status = InvitationStatus.Pending,
            CreatedAt = Now.AddDays(-1),
            ExpiresAt = Now.AddDays(6)
        };
        tweak?.Invoke(invitation);
        await fixture.Context.Invitations.InsertOneAsync(invitation);
        return invitation;
    }

    private Task<Invitation> ReloadAsync(ObjectId id) =>
        fixture.Context.Invitations.Find(x => x.Id == id).SingleAsync();

    [Fact]
    public async Task Redeem_consumes_invitation_and_creates_user_together()
    {
        var invitation = await SeedAsync();
        var user = NewUser();

        await CreateSut().RedeemAsync(invitation.Code, user, Now, CancellationToken.None);

        var stored = await ReloadAsync(invitation.Id);
        stored.Status.Should().Be(InvitationStatus.Used);
        stored.UsedAt.Should().Be(Now);
        stored.UsedByUserId.Should().Be(user.Id);
        (await fixture.Context.Users.CountDocumentsAsync(x => x.Id == user.Id)).Should().Be(1);
    }

    public enum Failure { UnknownCode, Expired, Used, Revoked, EmailMismatch }

    [Theory]
    [InlineData(Failure.UnknownCode)]
    [InlineData(Failure.Expired)]
    [InlineData(Failure.Used)]
    [InlineData(Failure.Revoked)]
    [InlineData(Failure.EmailMismatch)]
    public async Task Every_failure_reason_looks_the_same_and_creates_no_user(Failure failure)
    {
        var invitation = await SeedAsync(i =>
        {
            if (failure == Failure.Expired) i.ExpiresAt = Now;
            if (failure == Failure.Used) i.Status = InvitationStatus.Used;
            if (failure == Failure.Revoked) i.Status = InvitationStatus.Revoked;
        });
        var code = failure == Failure.UnknownCode ? InvitationCode.Generate() : invitation.Code;
        var user = NewUser(failure == Failure.EmailMismatch ? "stranger@example.com" : Email);

        var act = () => CreateSut().RedeemAsync(code, user, Now, CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .WithMessage(MongoInvitationRedeemer.InvalidInvitation);
        (await fixture.Context.Users.CountDocumentsAsync(FilterDefinition<User>.Empty)).Should().Be(0);
    }

    [Fact]
    public async Task Duplicate_user_email_rolls_back_and_keeps_invitation_pending()
    {
        var invitation = await SeedAsync();
        await fixture.Context.Users.InsertOneAsync(NewUser());

        var act = () => CreateSut().RedeemAsync(invitation.Code, NewUser(), Now, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        (await ReloadAsync(invitation.Id)).Status.Should().Be(InvitationStatus.Pending);
    }

    [Fact]
    public async Task Concurrent_redemptions_of_one_invitation_create_exactly_one_user()
    {
        var invitation = await SeedAsync();

        var attempts = Enumerable.Range(0, 2).Select(async _ =>
        {
            try
            {
                await CreateSut().RedeemAsync(invitation.Code, NewUser(), Now, CancellationToken.None);
                return true;
            }
            catch (Exception ex) when (ex is ForbiddenException or ConflictException)
            {
                return false;
            }
        });

        var results = await Task.WhenAll(attempts);

        results.Count(ok => ok).Should().Be(1);
        (await fixture.Context.Users.CountDocumentsAsync(FilterDefinition<User>.Empty)).Should().Be(1);
    }
}
