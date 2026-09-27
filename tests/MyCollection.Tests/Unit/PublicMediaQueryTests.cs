using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Media;
using MyCollection.Application.Sharing;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class PublicMediaQueryTests
{
    private const string Slug = "abc123abc123";

    private readonly Mock<IShareLinkRepository> _links = new();
    private readonly Mock<IPublicCatalogReader> _catalog = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 27, 3, 0, 0, TimeSpan.Zero));

    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly string Prefix = $"{Owner}/{ObjectId.GenerateNewId()}/img1";

    public PublicMediaQueryTests()
    {
        _links.Setup(r => r.GetBySlugAsync(Slug, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShareLink
            {
                Id = ObjectId.GenerateNewId(),
                OwnerId = Owner,
                Slug = Slug,
                Scope = ShareScope.Showcase,
                IncludeCategoryIds = [],
                CreatedAt = DateTime.UtcNow
            });
        _catalog.Setup(r => r.ListItemsAsync(Owner, ShareScope.Showcase, It.IsAny<IReadOnlyList<ObjectId>>(), false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PublicItemProjection
            {
                Id = ObjectId.GenerateNewId(),
                CategoryId = ObjectId.GenerateNewId(),
                Name = "精選公仔",
                Images =
                [
                    new ItemImage
                    {
                        Id = "img1",
                        Path = $"{Prefix}-full.webp",
                        CardPath = $"{Prefix}-card.webp",
                        ThumbPath = $"{Prefix}-thumb.webp"
                    }
                ]
            }]);
        _storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([1, 2, 3]));
    }

    private OpenPublicMediaQueryHandler CreateSut() => new(_links.Object, _catalog.Object, _storage.Object, _time);

    [Theory]
    [InlineData("card")]
    [InlineData("thumb")]
    public async Task Serves_card_and_thumb_of_shared_items(string size)
    {
        var result = await CreateSut().Handle(new OpenPublicMediaQuery(Slug, $"{Prefix}-{size}.webp"), CancellationToken.None);

        result.ContentType.Should().Be("image/webp");
    }

    [Fact]
    public async Task Does_not_serve_full_size_publicly()
    {
        // S8：公開 DTO 只給 card／thumb，原尺寸不對匿名訪客開放；回 404 而非 403，不透露檔案存在。
        var act = () => CreateSut().Handle(new OpenPublicMediaQuery(Slug, $"{Prefix}-full.webp"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        _storage.Verify(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
