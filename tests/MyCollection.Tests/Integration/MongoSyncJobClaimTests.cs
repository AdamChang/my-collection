using FluentAssertions;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoSyncJobClaimTests(MongoFixture mongo) : IAsyncLifetime
{
    private readonly ObjectId _ownerId = ObjectId.GenerateNewId();
    private MongoSyncJobRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await mongo.ResetAsync();
        var user = new Mock<IUserContext>();
        user.SetupGet(context => context.UserId).Returns(_ownerId);
        _repository = new MongoSyncJobRepository(mongo.Context, user.Object);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Claim_is_atomic_and_completed_operation_cannot_be_claimed_again()
    {
        var job = NewJob();
        await _repository.InsertAsync(job, CancellationToken.None);
        var now = DateTime.UtcNow;

        var first = await _repository.ClaimAsync(
            job.Id, now, now.AddMinutes(31), CancellationToken.None);
        var duplicate = await _repository.ClaimAsync(
            job.Id, now.AddSeconds(1), now.AddMinutes(32), CancellationToken.None);

        first.Should().NotBeNull();
        first!.Attempt.Should().Be(1);
        duplicate.Should().BeNull();

        first.Status = SyncStatus.Succeeded;
        first.LeaseUntil = null;
        await _repository.UpdateAsync(first, CancellationToken.None);

        (await _repository.ClaimAsync(
            job.Id, now.AddHours(1), now.AddHours(2), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Expired_lease_can_be_reclaimed_and_increments_attempt()
    {
        var job = NewJob();
        await _repository.InsertAsync(job, CancellationToken.None);
        var now = DateTime.UtcNow;

        await _repository.ClaimAsync(job.Id, now, now.AddSeconds(1), CancellationToken.None);
        var reclaimed = await _repository.ClaimAsync(
            job.Id, now.AddSeconds(2), now.AddMinutes(32), CancellationToken.None);

        reclaimed.Should().NotBeNull();
        reclaimed!.Attempt.Should().Be(2);
    }

    private static SyncJob NewJob() => new()
    {
        Id = ObjectId.GenerateNewId(),
        Provider = "steam",
        Kind = SyncJobKind.Enrich,
        Status = SyncStatus.Running,
        StartedAt = DateTime.UtcNow
    };
}
