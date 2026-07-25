using MediatR;
using MyCollection.Application.Items;

namespace MyCollection.Api.Endpoints;

public static class ItemEndpoints
{
    public static IEndpointRouteBuilder MapItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/items").WithTags("Items").RequireAuthorization();

        group.MapGet("/", async (
            string? search,
            string? categoryId,
            string[]? tags,
            bool? isShowcased,
            int? page,
            int? pageSize,
            ISender sender,
            CancellationToken ct) =>
            Results.Ok(await sender.Send(new SearchItemsQuery(
                search, categoryId, tags, isShowcased, page ?? 1, pageSize ?? 24), ct)));

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
