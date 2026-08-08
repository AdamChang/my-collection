using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class CloudTaskEndpointTests(MongoFixture mongo) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await mongo.ResetAsync();
        _factory = new ApiFactory(mongo);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/internal/tasks/ingestion",
            new { operationId = ObjectId.GenerateNewId().ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Application_jwt_is_not_accepted_as_cloud_task_identity()
    {
        using var client = await AuthenticatedClient.CreateAsync(_factory, "task-jwt@example.com");

        var response = await client.PostAsJsonAsync(
            "/internal/tasks/ingestion",
            new { operationId = ObjectId.GenerateNewId().ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
