using FluentAssertions;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoShareLinkRepositoryTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId OtherOwner = ObjectId.GenerateNewId();

    private MongoShareLinkRepository _sut = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(c => c.UserId).Returns(Owner);
        _sut = new MongoShareLinkRepository(fixture.Context, userContext.Object);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static ShareLink NewLink(ObjectId ownerId, string slug) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = ownerId,
        Slug = slug,
        Scope = ShareScope.Showcase,
        IncludeCategoryIds = [],
        IncludePrice = false,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Insert_then_GetBySlug_ignores_owner()
    {
        await _sut.InsertAsync(NewLink(Owner, "abc123"), CancellationToken.None);

        // 公開查詢不帶 ownerId
        var found = await _sut.GetBySlugAsync("abc123", CancellationToken.None);

        found.Should().NotBeNull();
        found!.OwnerId.Should().Be(Owner);
    }

    [Fact]
    public async Task Insert_duplicate_slug_throws_ConflictException()
    {
        await _sut.InsertAsync(NewLink(Owner, "abc123"), CancellationToken.None);

        var act = () => _sut.InsertAsync(NewLink(Owner, "abc123"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task ListAsync_returns_own_links_only()
    {
        await _sut.InsertAsync(NewLink(Owner, "mine"), CancellationToken.None);
        await fixture.Context.ShareLinks.InsertOneAsync(NewLink(OtherOwner, "theirs"));

        var links = await _sut.ListAsync(CancellationToken.None);

        links.Should().ContainSingle().Which.Slug.Should().Be("mine");
    }

    [Fact]
    public async Task DeleteAsync_throws_NotFound_for_other_owners_link()
    {
        var foreign = NewLink(OtherOwner, "theirs");
        await fixture.Context.ShareLinks.InsertOneAsync(foreign);

        var act = () => _sut.DeleteAsync(foreign.Id, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
