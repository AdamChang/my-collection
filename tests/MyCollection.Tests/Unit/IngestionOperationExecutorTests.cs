using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class IngestionOperationExecutorTests
{
    private readonly Mock<IBackgroundSyncJobRepository> _backgroundJobs = new();
    private readonly BackgroundUserContext _userContext = new();
    private readonly FakeTimeProvider _time = new(
        new DateTimeOffset(2026, 8, 9, 1, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Completed_operation_is_a_no_op_on_duplicate_delivery()
    {
        var id = ObjectId.GenerateNewId();
        _backgroundJobs.Setup(repository => repository.ClaimAsync(
                id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncJob?)null);
        _backgroundJobs.Setup(repository => repository.GetUnscopedAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Job(id, attempt: 1, SyncStatus.Succeeded));

        var result = await CreateSut().ExecuteAsync(id, CancellationToken.None);

        result.Should().Be(IngestionExecutionResult.AlreadyCompleted);
        _backgroundJobs.Verify(
            repository => repository.ResetForRetryAsync(It.IsAny<ObjectId>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Fifth_failure_is_terminal_and_is_not_reset_for_retry()
    {
        var id = ObjectId.GenerateNewId();
        _backgroundJobs.Setup(repository => repository.ClaimAsync(
                id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Job(id, attempt: 5, SyncStatus.Running));

        var result = await CreateSut().ExecuteAsync(id, CancellationToken.None);

        result.Should().Be(IngestionExecutionResult.FailedTerminal);
        _backgroundJobs.Verify(
            repository => repository.ResetForRetryAsync(It.IsAny<ObjectId>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Earlier_failure_is_reset_and_rethrown_for_queue_retry()
    {
        var id = ObjectId.GenerateNewId();
        _backgroundJobs.Setup(repository => repository.ClaimAsync(
                id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Job(id, attempt: 4, SyncStatus.Running));

        var act = () => CreateSut().ExecuteAsync(id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _backgroundJobs.Verify(
            repository => repository.ResetForRetryAsync(id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private IngestionOperationExecutor CreateSut()
    {
        var jobs = new Mock<ISyncJobRepository>();
        var user = new Mock<IUserContext>();
        var registry = new ProviderRegistry([]);
        var syncRunner = new SyncJobRunner(
            registry,
            Mock.Of<IExternalAccountRepository>(),
            jobs.Object,
            Mock.Of<IItemSyncWriter>(),
            Mock.Of<ICategoryRepository>(),
            user.Object,
            _time);
        var enrichRunner = new EnrichJobRunner(
            Mock.Of<IItemRepository>(),
            Mock.Of<ICategoryRepository>(),
            jobs.Object,
            Mock.Of<IItemEnrichWriter>(),
            user.Object,
            _time);

        return new IngestionOperationExecutor(
            _backgroundJobs.Object,
            jobs.Object,
            _userContext,
            registry,
            syncRunner,
            enrichRunner,
            _time);
    }

    private static SyncJob Job(ObjectId id, int attempt, SyncStatus status) => new()
    {
        Id = id,
        OwnerId = ObjectId.GenerateNewId(),
        Provider = "invalid",
        Kind = (SyncJobKind)999,
        Attempt = attempt,
        Status = status,
        StartedAt = DateTime.UtcNow
    };
}
