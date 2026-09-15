using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Domain.Entities;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoTransactionSmokeTests(MongoFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Test_container_supports_multi_document_transactions()
    {
        var context = fixture.Context;
        var categoryId = ObjectId.GenerateNewId();

        using var session = await context.Database.Client.StartSessionAsync();
        await session.WithTransactionAsync(async (s, ct) =>
        {
            await context.Categories.InsertOneAsync(s, new Category
            {
                Id = categoryId,
                OwnerId = ObjectId.GenerateNewId(),
                Name = "tx",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }, cancellationToken: ct);

            return true;
        });

        var stored = await context.Categories.Find(c => c.Id == categoryId).FirstOrDefaultAsync();
        stored.Should().NotBeNull();
    }
}
