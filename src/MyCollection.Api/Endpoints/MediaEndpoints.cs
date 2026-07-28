using MediatR;
using MyCollection.Application.Common;
using MyCollection.Application.Media;

namespace MyCollection.Api.Endpoints;

public static class MediaEndpoints
{
    /// <summary>單張圖片上傳大小上限（10 MB）。</summary>
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/items/{itemId}/images").WithTags("Media").RequireAuthorization();

        group.MapPost("/", async (string itemId, IFormFile file, ISender sender, CancellationToken ct) =>
            {
                if (file.Length is 0 or > MaxUploadBytes)
                {
                    return Results.BadRequest(new { title = "File must be between 1 byte and 10 MB." });
                }

                await using var stream = file.OpenReadStream();
                var image = await sender.Send(new UploadItemImageCommand(itemId, stream), ct);

                return Results.Created($"/media/{image.Path}", image);
            })
            .DisableAntiforgery();

        group.MapDelete("/{imageId}", async (string itemId, string imageId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteItemImageCommand(itemId, imageId), ct);
            return Results.NoContent();
        });

        group.MapPost("/{imageId}/primary", async (string itemId, string imageId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new SetPrimaryImageCommand(itemId, imageId), ct);
            return Results.NoContent();
        });

        // 匿名：分享頁需要讀得到圖片。路徑本身含 ObjectId，難以枚舉。
        app.MapGet("/media/{**path}", async (string path, IFileStorage storage, CancellationToken ct) =>
            {
                // 這是匿名端點。限定副檔名，避免它變成 media root 的任意檔案讀取器。
                if (!path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.NotFound();
                }

                Stream? stream;
                try
                {
                    stream = await storage.OpenReadAsync(path, ct);
                }
                catch (ArgumentException)
                {
                    return Results.NotFound();
                }

                return stream is null
                    ? Results.NotFound()
                    : Results.Stream(stream, "image/webp");
            })
            .AllowAnonymous()
            .WithTags("Media");

        return app;
    }
}
