using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Common;
using MyCollection.Application.Media;
using MyCollection.Application.Showcase;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;

namespace MyCollection.Infrastructure.Imaging;

/// <summary>
/// 消費 Showcase 佇列，把 provider 的遠端圖片抓回本地。
/// 直接用 MongoContext 而非 IItemRepository：背景服務沒有 HTTP 請求脈絡，
/// IUserContext 不可用，因此以品項自身的 ownerId 定址。
/// </summary>
public sealed class ShowcaseImageDownloader(
    IShowcaseImageQueue queue,
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<ShowcaseImageDownloader> logger) : BackgroundService
{
    public const string HttpClientName = "showcase-images";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ObjectId itemId;
            try
            {
                itemId = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await DownloadAsync(itemId, stoppingToken);
            }
            catch (Exception ex)
            {
                // 背景下載失敗不影響任何使用者流程，圖片繼續依賴遠端 CDN
                logger.LogWarning(ex, "Showcase image download failed for item {ItemId}.", itemId);
            }
        }
    }

    private async Task DownloadAsync(ObjectId itemId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var processor = scope.ServiceProvider.GetRequiredService<IImageProcessor>();

        var item = await context.Items
            .Find(Builders<Item>.Filter.Eq(x => x.Id, itemId))
            .FirstOrDefaultAsync(ct);

        if (item is null || item.Images.Count > 0)
        {
            return;
        }

        var sourceUrl = ResolveSourceUrl(item);
        if (sourceUrl is null)
        {
            return;
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        await using var remote = await client.GetStreamAsync(sourceUrl, ct);
        using var buffer = new MemoryStream();
        await remote.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var processed = await processor.ProcessAsync(buffer, ct);
        var imageId = ObjectId.GenerateNewId().ToString();

        var image = new ItemImage
        {
            Id = imageId,
            Path = await SaveAsync(storage, $"{item.OwnerId}/{item.Id}/{imageId}-full.webp", processed.Full, ct),
            CardPath = await SaveAsync(storage, $"{item.OwnerId}/{item.Id}/{imageId}-card.webp", processed.Card, ct),
            ThumbPath = await SaveAsync(storage, $"{item.OwnerId}/{item.Id}/{imageId}-thumb.webp", processed.Thumb, ct),
            IsPrimary = true,
            Order = 0
        };

        await context.Items.UpdateOneAsync(
            Builders<Item>.Filter.And(
                Builders<Item>.Filter.Eq(x => x.Id, item.Id),
                Builders<Item>.Filter.Size(x => x.Images, 0)),
            Builders<Item>.Update.Push(x => x.Images, image),
            cancellationToken: ct);

        logger.LogInformation("Downloaded showcase image for item {ItemId}.", itemId);
    }

    private static Uri? ResolveSourceUrl(Item item)
    {
        foreach (var key in (string[])["headerUrl", "iconUrl"])
        {
            if (item.Attributes.TryGetValue(key, out var value)
                && value.IsString
                && Uri.TryCreate(value.AsString, UriKind.Absolute, out var uri))
            {
                return uri;
            }
        }

        return null;
    }

    private static async Task<string> SaveAsync(IFileStorage storage, string path, byte[] content, CancellationToken ct)
    {
        using var stream = new MemoryStream(content);
        return await storage.SaveAsync(path, stream, ct);
    }
}
