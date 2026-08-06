using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Auth;
using MyCollection.Application.Sharing;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class ShareCommandTests
{
    private readonly Mock<IShareLinkRepository> _links = new();
    private readonly Mock<IPublicCatalogReader> _catalog = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    private static readonly ObjectId Owner = ObjectId.GenerateNewId();

    [Fact]
    public async Task Create_generates_url_safe_slug()
    {
        ShareLink? saved = null;
        _links.Setup(r => r.InsertAsync(It.IsAny<ShareLink>(), It.IsAny<CancellationToken>()))
            .Callback<ShareLink, CancellationToken>((l, _) => saved = l)
            .Returns(Task.CompletedTask);

        var dto = await new CreateShareLinkCommandHandler(_links.Object, _time)
            .Handle(new CreateShareLinkCommand("Showcase", [], false, null), CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.Slug.Should().MatchRegex("^[A-Za-z0-9]{12}$");
        saved.Scope.Should().Be(ShareScope.Showcase);
        saved.CreatedAt.Should().Be(new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc));
        dto.Slug.Should().Be(saved.Slug);
    }

    [Fact]
    public void Validator_requires_categories_for_category_scope()
    {
        var validator = new CreateShareLinkCommandValidator();

        validator.Validate(new CreateShareLinkCommand("Category", [], false, null)).IsValid.Should().BeFalse();
        validator.Validate(new CreateShareLinkCommand("Category", [ObjectId.GenerateNewId().ToString()], false, null))
            .IsValid.Should().BeTrue();
        validator.Validate(new CreateShareLinkCommand("Nope", [], false, null)).IsValid.Should().BeFalse();
    }

    private GetPublicShareQueryHandler CreatePublicSut() =>
        new(_links.Object, _catalog.Object, _users.Object, _time);

    private static ShareLink Link(DateTime? expiresAt = null, bool includePrice = false, bool includeRating = false) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = Owner,
        Slug = "abc123abc123",
        Scope = ShareScope.Showcase,
        IncludeCategoryIds = [],
        IncludePrice = includePrice,
        IncludeRating = includeRating,
        ExpiresAt = expiresAt,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Public_query_throws_NotFound_for_unknown_slug()
    {
        _links.Setup(r => r.GetBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShareLink?)null);

        var act = () => CreatePublicSut().Handle(new GetPublicShareQuery("nope"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Public_query_throws_NotFound_for_expired_link()
    {
        _links.Setup(r => r.GetBySlugAsync("abc123abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Link(expiresAt: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)));

        var act = () => CreatePublicSut().Handle(new GetPublicShareQuery("abc123abc123"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Public_query_passes_includePrice_flag_through()
    {
        _links.Setup(r => r.GetBySlugAsync("abc123abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Link(includePrice: true));
        _users.Setup(r => r.GetByIdAsync(Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Email = "a@b.c", PasswordHash = "h", DisplayName = "Adam" });
        _catalog.Setup(r => r.ListItemsAsync(Owner, ShareScope.Showcase, It.IsAny<IReadOnlyList<ObjectId>>(), true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PublicItemProjection
            {
                Id = ObjectId.GenerateNewId(),
                CategoryId = ObjectId.GenerateNewId(),
                Name = "精選公仔",
                Price = new Money(12800m, "TWD")
            }]);
        _catalog.Setup(r => r.ListCategoriesAsync(Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<ObjectId, PublicCategoryInfo>());

        var result = await CreatePublicSut().Handle(new GetPublicShareQuery("abc123abc123"), CancellationToken.None);

        result.OwnerDisplayName.Should().Be("Adam");
        result.Items.Should().ContainSingle().Which.Price!.Amount.Should().Be(12800m);
    }

    [Fact]
    public async Task Public_query_passes_includeRating_flag_through()
    {
        _links.Setup(r => r.GetBySlugAsync("abc123abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Link(includeRating: true));
        _users.Setup(r => r.GetByIdAsync(Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Email = "a@b.c", PasswordHash = "h", DisplayName = "Adam" });
        _catalog.Setup(r => r.ListItemsAsync(Owner, ShareScope.Showcase, It.IsAny<IReadOnlyList<ObjectId>>(), false, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PublicItemProjection
            {
                Id = ObjectId.GenerateNewId(),
                CategoryId = ObjectId.GenerateNewId(),
                Name = "精選公仔",
                Rating = 9
            }]);
        _catalog.Setup(r => r.ListCategoriesAsync(Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<ObjectId, PublicCategoryInfo>());

        var result = await CreatePublicSut().Handle(new GetPublicShareQuery("abc123abc123"), CancellationToken.None);

        result.Items.Should().ContainSingle().Which.Rating.Should().Be(9);
    }

    [Fact]
    public async Task Public_query_resolves_effective_display_mode_from_category_default()
    {
        var categoryId = ObjectId.GenerateNewId();
        _links.Setup(r => r.GetBySlugAsync("abc123abc123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Link());
        _users.Setup(r => r.GetByIdAsync(Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Email = "a@b.c", PasswordHash = "h", DisplayName = "Adam" });
        _catalog.Setup(r => r.ListItemsAsync(Owner, ShareScope.Showcase, It.IsAny<IReadOnlyList<ObjectId>>(), false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PublicItemProjection
            {
                Id = ObjectId.GenerateNewId(),
                CategoryId = categoryId,
                Name = "公仔"
            }]);
        _catalog.Setup(r => r.ListCategoriesAsync(Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<ObjectId, PublicCategoryInfo>
            {
                [categoryId] = new("公仔模型", DisplayMode.Hero, [])
            });

        var result = await CreatePublicSut().Handle(new GetPublicShareQuery("abc123abc123"), CancellationToken.None);

        result.Items.Should().ContainSingle().Which.EffectiveDisplayMode.Should().Be("Hero");
    }
}
