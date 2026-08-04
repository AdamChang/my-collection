using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Categories;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class SyncCommandTests
{
    private readonly Mock<IExternalAccountRepository> _accounts = new();
    private readonly Mock<ISyncJobRepository> _jobs = new();
    private readonly Mock<IItemSyncWriter> _writer = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<IBulkSyncProvider> _steam = new();
    private readonly Mock<Application.Common.IUserContext> _userContext = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId GameCategoryId = ObjectId.GenerateNewId();

    private readonly List<SyncJob> _savedJobs = [];

    public SyncCommandTests()
    {
        _userContext.SetupGet(c => c.UserId).Returns(Owner);

        _steam.SetupGet(p => p.Key).Returns("steam");
        _steam.Setup(p => p.SyncAsync(It.IsAny<ExternalAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ExternalItem("440", "TF2", null, null, new Dictionary<string, object?>())]);

        _accounts.Setup(r => r.GetAsync("steam", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalAccount
            {
                Id = ObjectId.GenerateNewId(), OwnerId = Owner, Provider = "steam",
                ExternalUserId = "765", ProtectedApiKey = "protected"
            });

        _categories.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Category
                {
                    Id = GameCategoryId,
                    OwnerId = Owner,
                    Name = "數位遊戲",
                    Kind = CategoryKind.Digital
                }
            ]);

        _writer.Setup(w => w.UpsertAsync(
                It.IsAny<ObjectId>(), It.IsAny<ObjectId>(), It.IsAny<ItemSource>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ExternalItem>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncOutcome(120, 80, 3));

        _jobs.Setup(r => r.InsertAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()))
            .Callback<SyncJob, CancellationToken>((j, _) => _savedJobs.Add(j))
            .Returns(Task.CompletedTask);
        _jobs.Setup(r => r.UpdateAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private SyncCommandHandler CreateSut() => new(
        new ProviderRegistry([_steam.Object]),
        _accounts.Object, _jobs.Object, _writer.Object, _categories.Object,
        _userContext.Object, _time);

    [Fact]
    public async Task Records_a_running_job_then_marks_it_succeeded_with_counts()
    {
        var dto = await CreateSut().Handle(new SyncCommand("steam"), CancellationToken.None);

        _savedJobs.Should().ContainSingle();
        var job = _savedJobs[0];
        job.Provider.Should().Be("steam");
        job.StartedAt.Should().Be(new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc));

        dto.Status.Should().Be(nameof(SyncStatus.Succeeded));
        dto.Created.Should().Be(120);
        dto.Updated.Should().Be(80);
        dto.Failed.Should().Be(3, "部分成功如實記錄，不做全有全無");
        dto.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Passes_the_digital_category_to_the_writer()
    {
        ObjectId? usedCategory = null;
        _writer.Setup(w => w.UpsertAsync(
                Owner, It.IsAny<ObjectId>(), ItemSource.Steam, "steam",
                It.IsAny<IReadOnlyList<ExternalItem>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<ObjectId, ObjectId, ItemSource, string, IReadOnlyList<ExternalItem>, DateTime, CancellationToken>(
                (_, c, _, _, _, _, _) => usedCategory = c)
            .ReturnsAsync(new SyncOutcome(1, 0, 0));

        await CreateSut().Handle(new SyncCommand("steam"), CancellationToken.None);

        usedCategory.Should().Be(GameCategoryId);
    }

    [Fact]
    public async Task Missing_digital_category_marks_the_job_failed_and_throws_without_creating_one()
    {
        _categories.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _steam.Setup(p => p.SyncAsync(It.IsAny<ExternalAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var act = () => CreateSut().Handle(new SyncCommand("steam"), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NotFoundException>();
        exception.Which.Resource.Should().Be("Category");
        exception.Which.Key.Should().Be("數位遊戲");
        _savedJobs.Should().ContainSingle().Which.Status.Should().Be(SyncStatus.Failed);
        _categories.Verify(
            x => x.InsertAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Uses_the_system_digital_category_when_no_owner_category_exists()
    {
        var systemId = ObjectId.Parse("000000000000000000000002");
        _categories.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Category
                {
                    Id = systemId,
                    OwnerId = null,
                    Name = "數位遊戲",
                    Kind = CategoryKind.Digital
                }
            ]);

        ObjectId? usedCategory = null;
        _writer.Setup(w => w.UpsertAsync(
                Owner, It.IsAny<ObjectId>(), ItemSource.Steam, "steam",
                It.IsAny<IReadOnlyList<ExternalItem>>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Callback<ObjectId, ObjectId, ItemSource, string, IReadOnlyList<ExternalItem>, DateTime, CancellationToken>(
                (_, categoryId, _, _, _, _, _) => usedCategory = categoryId)
            .ReturnsAsync(new SyncOutcome(1, 0, 0));

        await CreateSut().Handle(new SyncCommand("steam"), CancellationToken.None);

        usedCategory.Should().Be(systemId);
        _categories.Verify(
            x => x.InsertAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Owner_digital_category_takes_precedence_over_system_category()
    {
        var systemId = ObjectId.Parse("000000000000000000000002");
        _categories.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Category { Id = systemId, OwnerId = null, Name = "數位遊戲", Kind = CategoryKind.Digital },
                new Category { Id = GameCategoryId, OwnerId = Owner, Name = "數位遊戲", Kind = CategoryKind.Digital }
            ]);

        ObjectId? usedCategory = null;
        _writer.Setup(w => w.UpsertAsync(
                Owner, It.IsAny<ObjectId>(), ItemSource.Steam, "steam",
                It.IsAny<IReadOnlyList<ExternalItem>>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Callback<ObjectId, ObjectId, ItemSource, string, IReadOnlyList<ExternalItem>, DateTime, CancellationToken>(
                (_, categoryId, _, _, _, _, _) => usedCategory = categoryId)
            .ReturnsAsync(new SyncOutcome(1, 0, 0));

        await CreateSut().Handle(new SyncCommand("steam"), CancellationToken.None);

        usedCategory.Should().Be(GameCategoryId);
    }

    [Fact]
    // 帳號查詢發生在建立 SyncJob 之前，所以這條路徑不會留下任何 job 記錄
    public async Task Missing_linked_account_throws_NotFound()
    {
        _accounts.Setup(r => r.GetAsync("steam", It.IsAny<CancellationToken>())).ReturnsAsync((ExternalAccount?)null);

        var act = () => CreateSut().Handle(new SyncCommand("steam"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Provider_failure_is_recorded_on_the_job_and_rethrown()
    {
        _steam.Setup(p => p.SyncAsync(It.IsAny<ExternalAccount>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException("steam", "rate limited"));

        SyncJob? finalJob = null;
        _jobs.Setup(r => r.UpdateAsync(It.IsAny<SyncJob>(), It.IsAny<CancellationToken>()))
            .Callback<SyncJob, CancellationToken>((j, _) => finalJob = j)
            .Returns(Task.CompletedTask);

        var act = () => CreateSut().Handle(new SyncCommand("steam"), CancellationToken.None);

        await act.Should().ThrowAsync<ProviderException>();
        finalJob.Should().NotBeNull();
        finalJob!.Status.Should().Be(SyncStatus.Failed);
        finalJob.Error.Should().Contain("rate limited");
        finalJob.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Provider_without_bulk_sync_is_rejected()
    {
        var opengraph = new Mock<IMetadataProvider>();
        opengraph.SetupGet(p => p.Key).Returns("opengraph");

        var sut = new SyncCommandHandler(
            new ProviderRegistry([opengraph.Object]),
            _accounts.Object, _jobs.Object, _writer.Object, _categories.Object,
            _userContext.Object, _time);

        var act = () => sut.Handle(new SyncCommand("opengraph"), CancellationToken.None);

        await act.Should().ThrowAsync<ProviderException>();
    }
}
