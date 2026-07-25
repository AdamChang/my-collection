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
public class MongoCategoryRepositoryTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId OtherOwner = ObjectId.GenerateNewId();

    private MongoCategoryRepository _sut = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(c => c.UserId).Returns(Owner);
        userContext.SetupGet(c => c.IsAuthenticated).Returns(true);

        _sut = new MongoCategoryRepository(fixture.Context, userContext.Object);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Category NewCategory(ObjectId? ownerId, string name) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = ownerId,
        Name = name,
        Icon = "figure",
        Kind = CategoryKind.Physical,
        Fields = [new CategoryField { Key = "brand", Label = "廠商", Type = FieldType.Text }],
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task ListAsync_returns_own_and_system_categories_only()
    {
        await _sut.InsertAsync(NewCategory(Owner, "公仔"), CancellationToken.None);
        await fixture.Context.Categories.InsertOneAsync(NewCategory(null, "數位遊戲"));
        await fixture.Context.Categories.InsertOneAsync(NewCategory(OtherOwner, "別人的品類"));

        var result = await _sut.ListAsync(CancellationToken.None);

        result.Select(c => c.Name).Should().BeEquivalentTo("公仔", "數位遊戲");
    }

    [Fact]
    public async Task GetAsync_returns_null_for_other_owners_category()
    {
        var foreign = NewCategory(OtherOwner, "別人的品類");
        await fixture.Context.Categories.InsertOneAsync(foreign);

        var result = await _sut.GetAsync(foreign.Id, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_throws_NotFound_for_other_owners_category()
    {
        var foreign = NewCategory(OtherOwner, "別人的品類");
        await fixture.Context.Categories.InsertOneAsync(foreign);
        foreign.Name = "hijacked";

        var act = () => _sut.UpdateAsync(foreign, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_throws_Forbidden_for_system_category()
    {
        var system = NewCategory(null, "數位遊戲");
        await fixture.Context.Categories.InsertOneAsync(system);
        system.Name = "hijacked";

        var act = () => _sut.UpdateAsync(system, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task DeleteAsync_removes_own_category()
    {
        var category = NewCategory(Owner, "公仔");
        await _sut.InsertAsync(category, CancellationToken.None);

        await _sut.DeleteAsync(category.Id, CancellationToken.None);

        (await _sut.GetAsync(category.Id, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_throws_NotFound_when_missing()
    {
        var act = () => _sut.DeleteAsync(ObjectId.GenerateNewId(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
