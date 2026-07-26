using MediatR;
using MyCollection.Application.Sharing;

namespace MyCollection.Api.Endpoints;

public static class ShareEndpoints
{
    public static IEndpointRouteBuilder MapShareEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/shares").WithTags("Sharing").RequireAuthorization();

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListShareLinksQuery(), ct)));

        group.MapPost("/", async (CreateShareLinkCommand command, ISender sender, CancellationToken ct) =>
        {
            var created = await sender.Send(command, ct);
            return Results.Created($"/public/{created.Slug}", created);
        });

        group.MapDelete("/{id}", async (string id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteShareLinkCommand(id), ct);
            return Results.NoContent();
        });

        app.MapGet("/public/{slug}", async (string slug, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetPublicShareQuery(slug), ct)))
            .AllowAnonymous()
            .WithTags("Sharing");

        return app;
    }
}
