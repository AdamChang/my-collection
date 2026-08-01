using MediatR;
using MyCollection.Application.Ingestion;

namespace MyCollection.Api.Endpoints;

public static class IngestionEndpoints
{
    public static IEndpointRouteBuilder MapIngestionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ingest").WithTags("Ingestion").RequireAuthorization();

        group.MapGet("/providers", (ProviderRegistry registry) =>
            Results.Ok(registry.All.Select(p => new
            {
                key = p.Key,
                capabilities = ProviderCapabilities.Of(p).ToString()
            })));

        group.MapPost("/sync/{provider}", async (string provider, ISender sender, CancellationToken ct) =>
            Results.Accepted($"/ingest/jobs", await sender.Send(new SyncCommand(provider), ct)));

        group.MapPost("/enrich/{provider}", async (
            string provider, EnrichRequest? body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new EnrichCommand(provider, body?.ItemIds, body?.Limit ?? 50), ct)));

        group.MapGet("/jobs", async (int? limit, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListSyncJobsQuery(limit ?? 20), ct)));

        group.MapPost("/fetch", async (string url, string? provider, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new FetchByUrlQuery(url, provider ?? "opengraph"), ct)));

        group.MapGet("/search", async (
            string provider, string q, int? limit, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new SearchProviderQuery(provider, q, limit ?? 20), ct)));

        var accounts = app.MapGroup("/external-accounts").WithTags("Ingestion").RequireAuthorization();

        accounts.MapGet("/", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListExternalAccountsQuery(), ct)));

        accounts.MapPost("/", async (LinkExternalAccountCommand command, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(command, ct)));

        accounts.MapDelete("/{provider}", async (string provider, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new UnlinkExternalAccountCommand(provider), ct);
            return Results.NoContent();
        });

        return app;
    }

    /// <summary>兩個欄位都可省略：不給 ItemIds 就是批次補完。</summary>
    public record EnrichRequest(IReadOnlyList<string>? ItemIds, int? Limit);
}
