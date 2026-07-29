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

        return app;
    }
}
