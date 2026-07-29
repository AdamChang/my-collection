using MediatR;
using MyCollection.Application.Transfer;

namespace MyCollection.Api.Endpoints;

public static class TransferEndpoints
{
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/").WithTags("Transfer").RequireAuthorization();

        group.MapGet("/export", async (HttpContext http, ISender sender, TimeProvider time, CancellationToken ct) =>
        {
            var fileName = $"mycollection-{time.GetUtcNow():yyyyMMdd-HHmmss}.zip";

            http.Response.ContentType = "application/zip";
            http.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

            // 串流開始後就無法再改 status code，中途失敗只能斷線。
            await sender.Send(new ExportArchiveCommand(http.Response.Body), ct);
        });

        group.MapPost("/import", async (IFormFile file, ISender sender, CancellationToken ct) =>
            {
                if (file.Length == 0)
                {
                    return Results.BadRequest(new { title = "The archive must not be empty." });
                }

                // ZipArchive 需要隨機存取 central directory，而 multipart stream 不可 seek，
                // 所以先落一份暫存檔。無論成敗都要刪掉。
                var tempPath = Path.GetTempFileName();

                try
                {
                    await using (var temp = File.Create(tempPath))
                    {
                        await file.CopyToAsync(temp, ct);
                    }

                    await using var archive = File.OpenRead(tempPath);
                    var result = await sender.Send(new ImportArchiveCommand(archive), ct);

                    return Results.Ok(result);
                }
                finally
                {
                    File.Delete(tempPath);
                }
            })
            .DisableAntiforgery()
            .WithMetadata(new UnlimitedRequestBody());

        return app;
    }
}

/// <summary>
/// 解除 Kestrel 對匯入端點的 request body 大小限制。
/// minimal API 沒有 DisableRequestSizeLimit() 擴充方法（那是 MVC 的 attribute），
/// 端點層級要靠這個 metadata 介面。
/// </summary>
internal sealed class UnlimitedRequestBody : Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata
{
    public long? MaxRequestBodySize => null;
}
