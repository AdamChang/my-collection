using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class EnrichCommandTests
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId CategoryId = ObjectId.GenerateNewId();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 1, 3, 0, 0, TimeSpan.Zero));
    private readonly Mock<ISearchProvider> _provider = new();
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<ISyncJobRepository> _jobs = new();
    private readonly Mock<IItemEnrichWriter> _writer = new();
    private readonly Mock<IUserContext> _userContext = new();

    private readonly List<ItemEnrichment> _written = [];

    public EnrichCommandTests()
    {
        _provider.SetupGet(p => p.Key).Returns(ProviderKeys.Igdb);
        _provider.SetupGet(p => p.MarkerAttributeKey).Returns("igdbId");
        _userContext.SetupGet(c => c.UserId).Returns(Owner);

        _categories.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Category(["igdbId", "developer", "genres"])]);

        _writer.Setup(w => w.ApplyAsync(
                It.IsAny<ObjectId>(), It.IsAny<IReadOnlyList<ItemEnrichment>>(),
                It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<ObjectId, IReadOnlyList<ItemEnrichment>, DateTime, string, CancellationToken>(
                (_, e, _, _, _) => _written.AddRange(e))
            .ReturnsAsync((ObjectId _, IReadOnlyList<ItemEnrichment> e, DateTime _, string _, CancellationToken _)
                => e.Count);
    }

    private static Category Category(string[] fieldKeys) => new()
    {
        Id = CategoryId,
        Name = "數位遊戲",
        Fields = fieldKeys.Select(k => new CategoryField
        {
            Key = k, Label = k, Type = k is "igdbId" ? FieldType.Number : FieldType.Text
        }).ToList()
    };

    private static Item SteamItem(string appId, string name = "TF2", string? description = null) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = Owner,
        CategoryId = CategoryId,
        Name = name,
        Description = description,
        Source = ItemSource.Steam,
        ExternalRef = new ExternalRef
        {
            Provider = ProviderKeys.Steam, ExternalId = appId, LastSyncedAt = DateTime.UtcNow
        },
        Attributes = []
    };

    private static Item BoundItem(long igdbId) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = Owner,
        CategoryId = CategoryId,
        Name = "已綁定",
        Attributes = new BsonDocument { { "igdbId", igdbId } }
    };

    private static ExternalItem Found(string externalId) => new(
        externalId,
        "The Witcher 3",
        "An adventure.",
        null,
        new Dictionary<string, object?>
        {
            ["igdbId"] = 1942L,
            ["developer"] = "CD Projekt RED",
            ["genres"] = "RPG",
            ["igdbRating"] = 93.5d
        });

    private EnrichCommandHandler CreateSut() => new(
        new ProviderRegistry([_provider.Object]),
        _items.Object,
        _categories.Object,
        _jobs.Object,
        _writer.Object,
        _userContext.Object,
        _time);

    private void SetupLookup(IReadOnlyDictionary<string, ExternalItem> found, params string[] failed) =>
        _provider.Setup(p => p.FetchByExternalIdsAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalLookupResult(found, failed));

    [Fact]
    public async Task Batch_mode_enriches_candidates_that_lack_the_marker()
    {
        _items.Setup(r => r.ListEnrichmentCandidatesAsync("igdbId", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([SteamItem("440")]);
        SetupLookup(new Dictionary<string, ExternalItem> { ["steam:440"] = Found("1942") });

        var job = await CreateSut().Handle(new EnrichCommand(ProviderKeys.Igdb), CancellationToken.None);

        job.Provider.Should().Be("igdb");
        job.Status.Should().Be("Succeeded");
        job.Updated.Should().Be(1);
        job.Skipped.Should().Be(0);
        job.Failed.Should().Be(0);
        job.Created.Should().Be(0, "補完永遠不建立品項");
    }

    [Fact]
    public async Task Uses_the_existing_marker_instead_of_the_steam_id_when_present()
    {
        var item = BoundItem(1942);
        _items.Setup(r => r.ListByIdsAsync(It.IsAny<IReadOnlyList<ObjectId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);
        SetupLookup(new Dictionary<string, ExternalItem> { ["igdb:1942"] = Found("1942") });

        await CreateSut().Handle(
            new EnrichCommand(ProviderKeys.Igdb, [item.Id.ToString()]), CancellationToken.None);

        _provider.Verify(p => p.FetchByExternalIdsAsync(
            It.Is<IReadOnlyList<string>>(ids => ids.Single() == "igdb:1942"),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Skips_items_with_neither_a_marker_nor_an_external_ref()
    {
        var orphan = new Item
        {
            Id = ObjectId.GenerateNewId(), OwnerId = Owner, CategoryId = CategoryId,
            Name = "手辦", Attributes = []
        };
        _items.Setup(r => r.ListByIdsAsync(It.IsAny<IReadOnlyList<ObjectId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([orphan]);
        SetupLookup(new Dictionary<string, ExternalItem>());

        var job = await CreateSut().Handle(
            new EnrichCommand(ProviderKeys.Igdb, [orphan.Id.ToString()]), CancellationToken.None);

        job.Skipped.Should().Be(1);
        job.Failed.Should().Be(0);
        job.Updated.Should().Be(0);
    }

    [Fact]
    public async Task Counts_a_lookup_miss_as_skipped_not_failed()
    {
        _items.Setup(r => r.ListEnrichmentCandidatesAsync("igdbId", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([SteamItem("440"), SteamItem("620", "Portal 2")]);
        SetupLookup(new Dictionary<string, ExternalItem> { ["steam:440"] = Found("1942") });

        var job = await CreateSut().Handle(new EnrichCommand(ProviderKeys.Igdb), CancellationToken.None);

        job.Updated.Should().Be(1);
        job.Skipped.Should().Be(1);
        job.Failed.Should().Be(0);
    }

    [Fact]
    public async Task Counts_a_request_level_failure_as_failed()
    {
        _items.Setup(r => r.ListEnrichmentCandidatesAsync("igdbId", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([SteamItem("440")]);
        SetupLookup(new Dictionary<string, ExternalItem>(), "steam:440");

        var job = await CreateSut().Handle(new EnrichCommand(ProviderKeys.Igdb), CancellationToken.None);

        job.Failed.Should().Be(1);
        job.Skipped.Should().Be(0);
    }

    [Fact]
    public async Task Drops_attributes_the_target_category_has_not_declared()
    {
        _items.Setup(r => r.ListEnrichmentCandidatesAsync("igdbId", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([SteamItem("440")]);
        SetupLookup(new Dictionary<string, ExternalItem> { ["steam:440"] = Found("1942") });

        await CreateSut().Handle(new EnrichCommand(ProviderKeys.Igdb), CancellationToken.None);

        _written.Single().Attributes.Keys.Should().BeEquivalentTo("igdbId", "developer", "genres");
        _written.Single().Attributes.Should().NotContainKey(
            "igdbRating", "品類沒宣告的 key 會被 AttributeValidator 擋掉");
    }

    [Fact]
    public async Task Writes_the_summary_only_when_the_item_has_no_description()
    {
        _items.Setup(r => r.ListEnrichmentCandidatesAsync("igdbId", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([SteamItem("440"), SteamItem("620", "Portal 2", "我自己寫的心得")]);
        SetupLookup(new Dictionary<string, ExternalItem>
        {
            ["steam:440"] = Found("1942"),
            ["steam:620"] = Found("1943")
        });

        await CreateSut().Handle(new EnrichCommand(ProviderKeys.Igdb), CancellationToken.None);

        _written.Should().HaveCount(2);
        _written.Should().ContainSingle(e => e.Description == "An adventure.");
        _written.Should().ContainSingle(e => e.Description == null);
    }

    [Fact]
    public async Task Records_a_failed_job_and_rethrows_when_the_provider_blows_up()
    {
        _items.Setup(r => r.ListEnrichmentCandidatesAsync("igdbId", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([SteamItem("440")]);
        _provider.Setup(p => p.FetchByExternalIdsAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException(ProviderKeys.Igdb, "boom"));

        var act = () => CreateSut().Handle(new EnrichCommand(ProviderKeys.Igdb), CancellationToken.None);

        await act.Should().ThrowAsync<ProviderException>();
        _jobs.Verify(j => j.UpdateAsync(
            It.Is<SyncJob>(job => job.Status == SyncStatus.Failed && job.Error == "boom"),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Requires_a_search_capable_provider()
    {
        var bulkOnly = new Mock<IBulkSyncProvider>();
        bulkOnly.SetupGet(p => p.Key).Returns(ProviderKeys.Steam);

        var sut = new EnrichCommandHandler(
            new ProviderRegistry([bulkOnly.Object]), _items.Object, _categories.Object,
            _jobs.Object, _writer.Object, _userContext.Object, _time);

        var act = () => sut.Handle(new EnrichCommand(ProviderKeys.Steam), CancellationToken.None);

        await act.Should().ThrowAsync<ProviderException>();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(500, 200)]
    [InlineData(50, 50)]
    public async Task Clamps_the_batch_limit(int requested, int expected)
    {
        _items.Setup(r => r.ListEnrichmentCandidatesAsync("igdbId", expected, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        SetupLookup(new Dictionary<string, ExternalItem>());

        await CreateSut().Handle(new EnrichCommand(ProviderKeys.Igdb, null, requested), CancellationToken.None);

        _items.Verify(r => r.ListEnrichmentCandidatesAsync("igdbId", expected, It.IsAny<CancellationToken>()));
    }
}
