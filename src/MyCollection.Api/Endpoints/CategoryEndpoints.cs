using MediatR;
using MyCollection.Application.Categories;

namespace MyCollection.Api.Endpoints;

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/categories").WithTags("Categories").RequireAuthorization();

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListCategoriesQuery(), ct)));

        group.MapPost("/", async (CreateCategoryCommand command, ISender sender, CancellationToken ct) =>
        {
            var created = await sender.Send(command, ct);
            return Results.Created($"/categories/{created.Id}", created);
        });

        group.MapPut("/{id}", async (string id, UpdateCategoryCommand body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(body with { Id = id }, ct)));

        group.MapDelete("/{id}", async (string id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteCategoryCommand(id), ct);
            return Results.NoContent();
        });

        return app;
    }
}
