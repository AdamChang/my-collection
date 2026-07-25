using FluentValidation;
using MediatR;
using MyCollection.Application.Common;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Auth;

public record RefreshCommand(string RefreshToken) : IRequest<AuthResponse>;

public sealed class RefreshCommandValidator : AbstractValidator<RefreshCommand>
{
    public RefreshCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

public sealed class RefreshCommandHandler(
    IUserRepository users,
    ITokenService tokenService,
    TimeProvider timeProvider) : IRequestHandler<RefreshCommand, AuthResponse>
{
    private const string InvalidToken = "Invalid or expired refresh token.";

    public async Task<AuthResponse> Handle(RefreshCommand request, CancellationToken cancellationToken)
    {
        var hash = tokenService.HashRefreshToken(request.RefreshToken);
        var user = await users.GetByRefreshTokenHashAsync(hash, cancellationToken);

        if (user is null)
        {
            throw new ForbiddenException(InvalidToken);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (user.RefreshTokenExpiresAt is null || user.RefreshTokenExpiresAt <= now)
        {
            await users.SetRefreshTokenAsync(user.Id, null, null, cancellationToken);
            throw new ForbiddenException(InvalidToken);
        }

        var newRefreshToken = tokenService.CreateRefreshToken();
        await users.SetRefreshTokenAsync(
            user.Id,
            tokenService.HashRefreshToken(newRefreshToken),
            now.Add(tokenService.RefreshTokenLifetime),
            cancellationToken);

        return new AuthResponse(
            tokenService.CreateAccessToken(user),
            newRefreshToken,
            now.Add(tokenService.AccessTokenLifetime),
            new UserDto(user.Id.ToString(), user.Email, user.DisplayName));
    }
}
