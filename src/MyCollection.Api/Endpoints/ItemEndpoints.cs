using MediatR;
using MyCollection.Application.Items;

namespace MyCollection.Api.Endpoints;

public static class ItemEndpoints
{
    public static IEndpointRouteBuilder MapItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/items").WithTags("Items").RequireAuthorization();

        group.MapGet("/", async (HttpRequest request, ISender sender, CancellationToken ct) =>
        {
            var query = request.Query;

            // attr.brand=GSC → Attributes["brand"] = "GSC"
            var attributes = query
                .Where(kv => kv.Key.StartsWith("attr.", StringComparison.Ordinal) && kv.Key.Length > 5)
                .ToDictionary(kv => kv.Key[5..], kv => kv.Value.ToString());

            return Results.Ok(await sender.Send(new SearchItemsQuery(
                query["search"].FirstOrDefault(),
                query["categoryId"].FirstOrDefault(),
                query["tags"].ToArray()!,
                bool.TryParse(query["isShowcased"], out var showcased) ? showcased : null,
                int.TryParse(query["page"], out var page) ? page : 1,
                int.TryParse(query["pageSize"], out var pageSize) ? pageSize : 24,
                attributes), ct));
        });

        // 必須早於 "/{id}"：否則 "tags" 會被當成品項 id。
        group.MapGet("/tags", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListTagsQuery(), ct)));

        group.MapGet("/{id}", async (string id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetItemQuery(id), ct)));

        group.MapPost("/", async (CreateItemCommand command, ISender sender, CancellationToken ct) =>
        {
            var created = await sender.Send(command, ct);
            return Results.Created($"/items/{created.Id}", created);
        });

        group.MapPut("/{id}", async (string id, UpdateItemCommand body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(body with { Id = id }, ct)));

        group.MapDelete("/{id}", async (string id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteItemCommand(id), ct);
            return Results.NoContent();
        });

        return app;
    }
}
