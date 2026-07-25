using MediatR;
using MyCollection.Application.Auth;

namespace MyCollection.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth").AllowAnonymous();

        group.MapPost("/register", async (RegisterCommand command, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(command, ct)));

        group.MapPost("/login", async (LoginCommand command, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(command, ct)));

        group.MapPost("/refresh", async (RefreshCommand command, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(command, ct)));

        app.MapGet("/auth/me", (Application.Common.IUserContext userContext) =>
                Results.Ok(new { userId = userContext.UserId.ToString() }))
            .RequireAuthorization()
            .WithTags("Auth");

        return app;
    }
}
