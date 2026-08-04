using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;

namespace MyCollection.Infrastructure.Ingestion;

/// <summary>
/// 消費補完佇列。單一 reader、逐筆處理——Steam 商店的節流本來就讓平行化沒有意義，
/// 而序列化執行讓速率限制只需要一個地方管。
/// </summary>
public sealed class EnrichJobWorker(
    IEnrichJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<EnrichJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            EnrichJobRequest request;
            try
            {
                request = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await RunAsync(request, stoppingToken);
            }
            catch (Exception ex)
            {
                // RunAsync 內部已把作業標成 Failed 並寫回資料庫，使用者從作業紀錄看得到。
                // 這裡只確保單一作業的例外不會終結整個 worker。
                logger.LogWarning(
                    ex, "Background enrich job {JobId} failed for provider {Provider}.",
                    request.Job.Id, request.Provider);
            }
        }
    }

    private async Task RunAsync(EnrichJobRequest request, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        // 背景作業沒有 HTTP 身分，先把 owner 明講出來，repository 的 filter 才有依據
        scope.ServiceProvider.GetRequiredService<BackgroundUserContext>().Set(request.OwnerId);

        var registry = scope.ServiceProvider.GetRequiredService<ProviderRegistry>();
        var runner = scope.ServiceProvider.GetRequiredService<EnrichJobRunner>();

        await runner.RunAsync(
            request.Job,
            registry.Require<IExternalIdLookupProvider>(request.Provider),
            request.ItemIds,
            request.Limit,
            ct);
    }
}
