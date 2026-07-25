using FluentValidation;
using MediatR;
using MyCollection.Application.Common;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Auth;

public record LoginCommand(string Email, string Password) : IRequest<AuthResponse>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    TimeProvider timeProvider) : IRequestHandler<LoginCommand, AuthResponse>
{
    private const string InvalidCredentials = "Invalid email or password.";

    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByEmailAsync(request.Email, cancellationToken);

        // 帳號不存在與密碼錯誤回傳相同訊息，避免帳號列舉
        if (user is null || !passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            throw new ForbiddenException(InvalidCredentials);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var refreshToken = tokenService.CreateRefreshToken();

        await users.SetRefreshTokenAsync(
            user.Id,
            tokenService.HashRefreshToken(refreshToken),
            now.Add(tokenService.RefreshTokenLifetime),
            cancellationToken);

        return new AuthResponse(
            tokenService.CreateAccessToken(user),
            refreshToken,
            now.Add(tokenService.AccessTokenLifetime),
            new UserDto(user.Id.ToString(), user.Email, user.DisplayName));
    }
}
