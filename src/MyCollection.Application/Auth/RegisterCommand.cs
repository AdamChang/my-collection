using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Auth;

public record RegisterCommand(string Email, string Password, string DisplayName) : IRequest<AuthResponse>;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(64);
    }
}

public sealed class RegisterCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    TimeProvider timeProvider) : IRequestHandler<RegisterCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var refreshToken = tokenService.CreateRefreshToken();

        var user = new User
        {
            Id = ObjectId.GenerateNewId(),
            Email = request.Email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHasher.Hash(request.Password),
            DisplayName = request.DisplayName.Trim(),
            RefreshTokenHash = tokenService.HashRefreshToken(refreshToken),
            RefreshTokenExpiresAt = now.Add(tokenService.RefreshTokenLifetime),
            CreatedAt = now
        };

        // email 重複時 Repository 擲 ConflictException → 409
        await users.InsertAsync(user, cancellationToken);

        return new AuthResponse(
            tokenService.CreateAccessToken(user),
            refreshToken,
            now.Add(tokenService.AccessTokenLifetime),
            new UserDto(user.Id.ToString(), user.Email, user.DisplayName));
    }
}
