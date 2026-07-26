using MongoDB.Bson;

namespace MyCollection.Domain.Entities;

public sealed class ExternalAccount
{
    public ObjectId Id { get; set; }
    public ObjectId OwnerId { get; set; }

    /// <summary>對應 IMetadataProvider.Key。</summary>
    public required string Provider { get; set; }

    /// <summary>Provider 上的使用者識別碼，Steam 為 SteamID64。</summary>
    public required string ExternalUserId { get; set; }

    /// <summary>加密後的 API Key。任何情況下都不回傳給前端。</summary>
    public required string ProtectedApiKey { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
