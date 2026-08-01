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

        group.MapGet("/{id}/missing-fields", async (
            string id, string provider, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new MissingProviderFieldsQuery(id, provider), ct)));

        group.MapPost("/{id}/ensure-fields", async (
            string id, EnsureFieldsRequest body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new EnsureProviderFieldsCommand(id, body.Provider), ct)));

        return app;
    }

    public record EnsureFieldsRequest(string Provider);
}
