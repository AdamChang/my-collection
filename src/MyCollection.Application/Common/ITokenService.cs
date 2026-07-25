using MyCollection.Domain.Entities;

namespace MyCollection.Application.Common;

public interface ITokenService
{
    string CreateAccessToken(User user);

    /// <summary>回傳明文 refresh token，只交給用戶端，資料庫僅存其雜湊。</summary>
    string CreateRefreshToken();

    string HashRefreshToken(string refreshToken);

    TimeSpan AccessTokenLifetime { get; }

    TimeSpan RefreshTokenLifetime { get; }
}
