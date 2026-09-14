using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
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

    /// <summary>
    /// 回歸測試：背景路徑的身分必須在「服務圖已建好之後」才設定也依然生效。
    /// 單元測試手動 new 出 executor，看不到容器的解析順序，這個 bug 只在真實容器裡才會出現。
    /// </summary>
    [Fact]
    public void Background_identity_applies_even_when_set_after_the_graph_is_built()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        // Cloud Tasks 端點會在呼叫 handler 前先解析完整棵圖，此時身分尚未確定。
        _ = services.GetRequiredService<IngestionOperationExecutor>();

        var owner = ObjectId.GenerateNewId();
        services.GetRequiredService<BackgroundUserContext>().Set(owner);

        services.GetRequiredService<IUserContext>().UserId.Should().Be(owner);
    }
}
