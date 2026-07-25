using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Auth;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(ObjectId id, CancellationToken ct);

    /// <summary>email 以小寫正規化後比對。</summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);

    Task<User?> GetByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct);

    /// <summary>email 重複時擲出 <see cref="Domain.Exceptions.ConflictException"/>。</summary>
    Task InsertAsync(User user, CancellationToken ct);

    Task SetRefreshTokenAsync(ObjectId id, string? refreshTokenHash, DateTime? expiresAt, CancellationToken ct);
}
