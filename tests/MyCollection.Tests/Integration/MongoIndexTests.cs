using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoIndexTests(MongoFixture fixture)
{
    private async Task<List<BsonDocument>> ListUserIndexesAsync()
    {
        var cursor = await fixture.Context.Users.Indexes.ListAsync();
        return await cursor.ToListAsync();
    }

    [Fact]
    public async Task Users_collection_has_unique_email_index()
    {
        var indexes = await ListUserIndexesAsync();

        var emailIndex = indexes.SingleOrDefault(i => i["name"] == "ux_users_email");

        emailIndex.Should().NotBeNull();
        emailIndex!["key"].Should().Be(new BsonDocument("email", 1));
        emailIndex["unique"].AsBoolean.Should().BeTrue();
    }

    [Fact]
    public async Task Users_collection_has_sparse_refresh_token_index()
    {
        var indexes = await ListUserIndexesAsync();

        var tokenIndex = indexes.SingleOrDefault(i => i["name"] == "ix_users_refreshTokenHash");

        tokenIndex.Should().NotBeNull();
        tokenIndex!["sparse"].AsBoolean.Should().BeTrue("大多數使用者沒有有效 refresh token，不該讓 null 值佔索引");
    }

    [Fact]
    public async Task Unique_email_index_actually_rejects_duplicates()
    {
        await fixture.ResetAsync();

        var first = new Domain.Entities.User
        {
            Id = ObjectId.GenerateNewId(), Email = "dup@example.com",
            PasswordHash = "h", DisplayName = "A", CreatedAt = DateTime.UtcNow
        };
        var second = new Domain.Entities.User
        {
            Id = ObjectId.GenerateNewId(), Email = "dup@example.com",
            PasswordHash = "h", DisplayName = "B", CreatedAt = DateTime.UtcNow
        };

        await fixture.Context.Users.InsertOneAsync(first);
        var act = () => fixture.Context.Users.InsertOneAsync(second);

        var ex = await act.Should().ThrowAsync<MongoWriteException>();
        ex.Which.WriteError.Category.Should().Be(ServerErrorCategory.DuplicateKey,
            "註冊流程的重複帳號防護最終靠這個索引，不是靠 Handler 先查再插");
    }

    [Fact]
    public async Task EnsureIndexes_is_idempotent()
    {
        var before = (await ListUserIndexesAsync()).Count;

        await MyCollection.Infrastructure.Mongo.MongoIndexInitializer
            .EnsureIndexesAsync(fixture.Context, CancellationToken.None);

        (await ListUserIndexesAsync()).Count.Should().Be(before, "重複啟動不該產生重複索引");
    }

    [Fact]
    public async Task Documents_round_trip_through_the_real_driver()
    {
        await fixture.ResetAsync();
        var createdAt = new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc);
        var user = new Domain.Entities.User
        {
            Id = ObjectId.GenerateNewId(), Email = "rt@example.com",
            PasswordHash = "h", DisplayName = "Adam", CreatedAt = createdAt
        };

        await fixture.Context.Users.InsertOneAsync(user);
        var loaded = await fixture.Context.Users
            .Find(Builders<Domain.Entities.User>.Filter.Eq(x => x.Email, "rt@example.com"))
            .SingleAsync();

        loaded.Id.Should().Be(user.Id);
        loaded.DisplayName.Should().Be("Adam");
        loaded.CreatedAt.Should().Be(createdAt);
        loaded.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        loaded.RefreshTokenHash.Should().BeNull();
    }
}
