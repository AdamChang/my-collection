using MediatR;
using MyCollection.Application.Showcase;

namespace MyCollection.Api.Endpoints;

public static class ShowcaseEndpoints
{
    public static IEndpointRouteBuilder MapShowcaseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/showcase", async (int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetShowcaseQuery(page ?? 1, pageSize ?? 24), ct)))
            .RequireAuthorization()
            .WithTags("Showcase");

        return app;
    }
}
