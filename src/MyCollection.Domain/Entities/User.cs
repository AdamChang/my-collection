using MongoDB.Bson;

namespace MyCollection.Domain.Entities;

public sealed class User
{
    public ObjectId Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string DisplayName { get; set; }

    /// <summary>Refresh token 只存雜湊。單一 token 設計：新登入會作廢舊 token。</summary>
    public string? RefreshTokenHash { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
