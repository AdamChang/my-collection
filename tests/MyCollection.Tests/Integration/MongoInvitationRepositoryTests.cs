using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoInvitationRepositoryTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    internal static Invitation NewInvitation(
        string email = "friend@example.com",
        string? code = null,
        InvitationStatus status = InvitationStatus.Pending,
        DateTime? createdAt = null) => new()
    {
        Id = ObjectId.GenerateNewId(),
        Email = email,
        Code = code ?? Application.Auth.InvitationCode.Generate(),
        Status = status,
        CreatedAt = createdAt ?? Now,
        ExpiresAt = (createdAt ?? Now).AddDays(7)
    };

    [Fact]
    public async Task Code_is_unique()
    {
        await fixture.Context.Invitations.InsertOneAsync(NewInvitation(email: "a@example.com", code: "SAMECODE"));

        var act = () => fixture.Context.Invitations.InsertOneAsync(NewInvitation(email: "b@example.com", code: "SAMECODE"));

        (await act.Should().ThrowAsync<MongoWriteException>())
            .Which.WriteError.Category.Should().Be(ServerErrorCategory.DuplicateKey);
    }

    [Fact]
    public async Task Only_one_pending_invitation_per_email()
    {
        await fixture.Context.Invitations.InsertOneAsync(NewInvitation());

        var act = () => fixture.Context.Invitations.InsertOneAsync(NewInvitation());

        (await act.Should().ThrowAsync<MongoWriteException>())
            .Which.WriteError.Category.Should().Be(ServerErrorCategory.DuplicateKey);
    }

    [Fact]
    public async Task Revoked_and_used_invitations_do_not_block_a_new_pending_one()
    {
        await fixture.Context.Invitations.InsertOneAsync(NewInvitation(status: InvitationStatus.Revoked));
        await fixture.Context.Invitations.InsertOneAsync(NewInvitation(status: InvitationStatus.Used));

        var act = () => fixture.Context.Invitations.InsertOneAsync(NewInvitation());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Status_is_stored_as_string()
    {
        var invitation = NewInvitation();
        await fixture.Context.Invitations.InsertOneAsync(invitation);

        var raw = await fixture.Context.Database.GetCollection<BsonDocument>("invitations")
            .Find(new BsonDocument("_id", invitation.Id)).SingleAsync();

        raw["status"].Should().Be(new BsonString("Pending"));
    }

    private MongoInvitationRepository CreateSut() => new(fixture.Context);

    [Fact]
    public async Task Insert_second_pending_for_same_email_throws_conflict()
    {
        await CreateSut().InsertAsync(NewInvitation(), CancellationToken.None);

        var act = () => CreateSut().InsertAsync(NewInvitation(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task RevokePending_revokes_only_pending_invitations_of_that_email()
    {
        var used = NewInvitation(status: InvitationStatus.Used);
        var pending = NewInvitation();
        var other = NewInvitation(email: "other@example.com");
        await fixture.Context.Invitations.InsertManyAsync([used, pending, other]);

        var revoked = await CreateSut().RevokePendingAsync("friend@example.com", Now, CancellationToken.None);

        revoked.Should().Be(1);
        var all = await CreateSut().ListAsync(CancellationToken.None);
        all.Single(x => x.Id == pending.Id).Status.Should().Be(InvitationStatus.Revoked);
        all.Single(x => x.Id == pending.Id).RevokedAt.Should().Be(Now);
        all.Single(x => x.Id == used.Id).Status.Should().Be(InvitationStatus.Used);
        all.Single(x => x.Id == other.Id).Status.Should().Be(InvitationStatus.Pending);
    }

    [Fact]
    public async Task List_returns_newest_first()
    {
        var older = NewInvitation(email: "a@example.com", createdAt: Now.AddDays(-1));
        var newer = NewInvitation(email: "b@example.com", createdAt: Now);
        await fixture.Context.Invitations.InsertManyAsync([older, newer]);

        var list = await CreateSut().ListAsync(CancellationToken.None);

        list.Select(x => x.Id).Should().Equal(newer.Id, older.Id);
    }
}
