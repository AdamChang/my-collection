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

        app.MapGet("/media/{**path}", async (string path, ISender sender, HttpContext context, CancellationToken ct) =>
            {
                var media = await sender.Send(new OpenOwnedMediaQuery(path), ct);
                context.Response.Headers.CacheControl = "private, max-age=300";
                return Results.Stream(media.Content, media.ContentType);
            })
            .RequireAuthorization()
            .WithTags("Media");

        app.MapGet("/public/{slug}/media/{**path}", async (
                string slug,
                string path,
                ISender sender,
                HttpContext context,
                CancellationToken ct) =>
            {
                var media = await sender.Send(new OpenPublicMediaQuery(slug, path), ct);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Stream(media.Content, media.ContentType);
            })
            .AllowAnonymous()
            .WithTags("Media");

        return app;
    }
}
