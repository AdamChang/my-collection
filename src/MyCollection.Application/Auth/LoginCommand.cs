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

    /// <summary>
    /// 帳號不存在時拿來墊檔的雜湊。只在型別初始化時算一次，不影響每次請求的成本。
    /// 目的是讓「帳號不存在」與「密碼錯誤」兩條路徑跑一樣多的 PBKDF2 迭代，
    /// 否則回應時間差（約 20ms）會直接洩漏該 email 是否已註冊。
    ///
    /// base64 內容是全零，格式合法（Pbkdf2PasswordHasher.Verify 會解析成功並實際跑滿
    /// 210,000 次迭代），但永遠不會與真實密碼相符。不可改成 "invalid" 之類的字串——
    /// Verify 會在格式檢查階段就 return false，等於沒跑 PBKDF2，這個防護就白做了。
    /// </summary>
    private static readonly string DummyHash =
        "pbkdf2.210000.AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByEmailAsync(request.Email, cancellationToken);

        // 即使帳號不存在也跑一次驗證，兩條路徑的耗時才一致（見 DummyHash 註解）
        var passwordMatches = passwordHasher.Verify(user?.PasswordHash ?? DummyHash, request.Password);

        // 帳號不存在與密碼錯誤回傳相同訊息，避免帳號列舉
        if (user is null || !passwordMatches)
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
