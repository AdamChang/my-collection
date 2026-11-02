using MongoDB.Bson;

namespace MyCollection.Domain.Entities;

/// <summary>
/// 發給某個 email 的一次性註冊資格（CONTEXT.md「邀請」）。
/// 「已過期」不存成狀態，而是由 Pending 加上 ExpiresAt 推導：系統沒有排程可以去改狀態，推導出來的結果才不會和現實不一致。
/// </summary>
public sealed class Invitation
{
    public ObjectId Id { get; set; }

    /// <summary>已正規化（Trim、小寫）。</summary>
    public required string Email { get; set; }

    /// <summary>正規化後的邀請碼，明文存放（ADR-0014 §三）。</summary>
    public required string Code { get; set; }

    public InvitationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public ObjectId? UsedByUserId { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public enum InvitationStatus
{
    Pending,
    Used,
    Revoked
}
