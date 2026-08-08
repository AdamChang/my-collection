using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyCollection.Application.Ingestion;

namespace MyCollection.Infrastructure.Ingestion;

public sealed class IngestionTaskWorker(
    InProcessIngestionTaskDispatcher dispatcher,
    IServiceScopeFactory scopeFactory,
    ILogger<IngestionTaskWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            MongoDB.Bson.ObjectId? operationId = null;
            try
            {
                operationId = await dispatcher.DequeueAsync(stoppingToken);
                await using var scope = scopeFactory.CreateAsyncScope();
                var executor = scope.ServiceProvider.GetRequiredService<IngestionOperationExecutor>();
                await executor.ExecuteAsync(operationId.Value, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "In-process ingestion delivery failed and will be retried.");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                if (operationId is { } failedOperationId)
                {
                    await dispatcher.DispatchAsync(failedOperationId, stoppingToken);
                }
            }
        }
    }
}
