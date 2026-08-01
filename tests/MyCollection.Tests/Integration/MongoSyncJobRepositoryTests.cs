using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoSyncJobRepositoryTests(MongoFixture fixture) : IAsyncLifetime
{
    private static readonly ObjectId Owner = ObjectId.GenerateNewId();

    private MongoSyncJobRepository _sut = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(c => c.UserId).Returns(Owner);
        userContext.SetupGet(c => c.IsAuthenticated).Returns(true);

        _sut = new MongoSyncJobRepository(fixture.Context, userContext.Object);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// 直接寫入沒有 skipped 欄位的 raw BsonDocument，模擬 Skipped 加入前寫入的舊文件，
    /// 驗證反序列化時會自動補 0，而不是驗證 C# 物件的預設值（那不代表任何 BSON 行為）。
    /// </summary>
    [Fact]
    public async Task Legacy_document_without_skipped_element_deserializes_to_zero()
    {
        var id = ObjectId.GenerateNewId();
        var startedAt = new DateTime(2026, 8, 1, 3, 0, 0, DateTimeKind.Utc);
        var legacyDoc = new BsonDocument
        {
            ["_id"] = id,
            ["ownerId"] = Owner,
            ["provider"] = "steam",
            ["status"] = "Succeeded",
            ["created"] = 1,
            ["updated"] = 2,
            ["failed"] = 3,
            // 刻意省略 "skipped"，模擬欄位新增前就存在的文件
            ["error"] = BsonNull.Value,
            ["startedAt"] = startedAt,
            ["finishedAt"] = startedAt.AddSeconds(5)
        };

        var rawCollection = fixture.Context.SyncJobs.Database.GetCollection<BsonDocument>("syncJobs");
        await rawCollection.InsertOneAsync(legacyDoc);

        var result = await _sut.ListRecentAsync(10, CancellationToken.None);

        var job = result.Should().ContainSingle().Subject;
        job.Id.Should().Be(id);
        job.Skipped.Should().Be(0);
        job.Created.Should().Be(1);
        job.Updated.Should().Be(2);
        job.Failed.Should().Be(3);
    }
}
