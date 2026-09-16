using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoCategoryFieldRenamerTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();
    private static readonly ObjectId OtherOwner = ObjectId.GenerateNewId();
    private static readonly DateTime Now = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private MongoCategoryFieldRenamer _sut = null!;
    private Category _category = null!;
    private Category _otherCategory = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(c => c.UserId).Returns(Owner);
        userContext.SetupGet(c => c.IsAuthenticated).Returns(true);
        _sut = new MongoCategoryFieldRenamer(fixture.Context, userContext.Object);

        _category = NewCategory(Owner, "price", "brand");
        _otherCategory = NewCategory(Owner, "price");
        await fixture.Context.Categories.InsertManyAsync([_category, _otherCategory]);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Category NewCategory(ObjectId owner, params string[] keys) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = owner,
        Name = "c",
        Fields = keys.Select(k => new CategoryField { Key = k, Label = k, Type = FieldType.Text }).ToList(),
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now.AddDays(-1)
    };

    private static Item NewItem(ObjectId owner, ObjectId categoryId, BsonDocument attributes) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = owner,
        CategoryId = categoryId,
        Name = "i",
        Attributes = attributes,
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now.AddDays(-1)
    };

    private Task<Item> Load(ObjectId id) => fixture.Context.Items.Find(i => i.Id == id).SingleAsync();

    [Fact]
    public async Task Moves_attribute_on_own_items_of_that_category_only()
    {
        var mine = NewItem(Owner, _category.Id, new BsonDocument { ["price"] = 100, ["brand"] = "GSC" });
        var mineWithout = NewItem(Owner, _category.Id, new BsonDocument { ["brand"] = "ALTER" });
        var otherCategory = NewItem(Owner, _otherCategory.Id, new BsonDocument { ["price"] = 5 });
        var otherOwner = NewItem(OtherOwner, _category.Id, new BsonDocument { ["price"] = 7 });
        await fixture.Context.Items.InsertManyAsync([mine, mineWithout, otherCategory, otherOwner]);

        var moved = await _sut.RenameAsync(_category.Id, "price", "purchasePrice", Now, CancellationToken.None);

        moved.Should().Be(1);
        (await Load(mine.Id)).Attributes.Should().Equal(new BsonDocument { ["brand"] = "GSC", ["purchasePrice"] = 100 });
        (await Load(mineWithout.Id)).Attributes.Should().Equal(new BsonDocument { ["brand"] = "ALTER" });
        (await Load(otherCategory.Id)).Attributes.Should().Equal(new BsonDocument { ["price"] = 5 });
        (await Load(otherOwner.Id)).Attributes.Should().Equal(new BsonDocument { ["price"] = 7 });
    }

    [Fact]
    public async Task Updates_schema_key_in_place_and_touches_updated_at()
    {
        await _sut.RenameAsync(_category.Id, "price", "purchasePrice", Now, CancellationToken.None);

        var stored = await fixture.Context.Categories.Find(c => c.Id == _category.Id).SingleAsync();
        stored.Fields.Select(f => f.Key).Should().Equal("purchasePrice", "brand");
        stored.Fields[0].Label.Should().Be("price"); // 只動 key
        stored.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Leaves_items_that_only_carry_the_new_key_untouched()
    {
        // Q6 情況 3：新鍵是未宣告屬性、沒有舊鍵 → 放行，它自然變成宣告過的值
        var redeclared = NewItem(Owner, _category.Id, new BsonDocument { ["purchasePrice"] = 42 });
        await fixture.Context.Items.InsertOneAsync(redeclared);

        var moved = await _sut.RenameAsync(_category.Id, "price", "purchasePrice", Now, CancellationToken.None);

        moved.Should().Be(0);
        (await Load(redeclared.Id)).Attributes.Should().Equal(new BsonDocument { ["purchasePrice"] = 42 });
    }

    [Fact]
    public async Task Conflict_when_an_item_carries_both_keys_and_nothing_changes()
    {
        var clean = NewItem(Owner, _category.Id, new BsonDocument { ["price"] = 1 });
        var both = NewItem(Owner, _category.Id, new BsonDocument { ["price"] = 2, ["purchasePrice"] = 3 });
        await fixture.Context.Items.InsertManyAsync([clean, both]);

        var act = () => _sut.RenameAsync(_category.Id, "price", "purchasePrice", Now, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Message.Should().Contain("1");

        // 全不做：schema 與所有品項原封不動
        var stored = await fixture.Context.Categories.Find(c => c.Id == _category.Id).SingleAsync();
        stored.Fields.Select(f => f.Key).Should().Equal("price", "brand");
        (await Load(clean.Id)).Attributes.Should().Equal(new BsonDocument { ["price"] = 1 });
        (await Load(both.Id)).Attributes.Should().Equal(new BsonDocument { ["price"] = 2, ["purchasePrice"] = 3 });
    }

    [Fact]
    public async Task Throws_not_found_for_category_of_another_owner()
    {
        var foreign = NewCategory(OtherOwner, "price");
        await fixture.Context.Categories.InsertOneAsync(foreign);

        var act = () => _sut.RenameAsync(foreign.Id, "price", "purchasePrice", Now, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
