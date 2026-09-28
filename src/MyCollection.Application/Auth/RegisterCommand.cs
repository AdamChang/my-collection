using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Auth;

public record RegisterCommand(string Email, string Password, string DisplayName, string InvitationCode)
    : IRequest<AuthResponse>;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(64);
        RuleFor(x => x.InvitationCode).NotEmpty().MaximumLength(64);
    }
}

public sealed class RegisterCommandHandler(
    IInvitationRedeemer invitations,
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

        // 邀請無效 → ForbiddenException（403，訊息不分原因）；email 重複 → ConflictException（409）。
        // 兩者都會讓整筆 transaction 回滾（ADR-0013 §二）
        await invitations.RedeemAsync(
            Auth.InvitationCode.Normalise(request.InvitationCode), user, now, cancellationToken);

        return new AuthResponse(
            tokenService.CreateAccessToken(user),
            refreshToken,
            now.Add(tokenService.AccessTokenLifetime),
            new UserDto(user.Id.ToString(), user.Email, user.DisplayName));
    }
}
