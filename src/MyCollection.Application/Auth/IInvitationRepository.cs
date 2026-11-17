using MyCollection.Domain.Entities;

namespace MyCollection.Application.Auth;

public interface IInvitationRepository
{
    /// <summary>同一 email 已有 Pending 邀請時擲出 <see cref="Domain.Exceptions.ConflictException"/>。</summary>
    Task InsertAsync(Invitation invitation, CancellationToken ct);

    /// <summary>撤銷該 email 所有 Pending 邀請（含已過期但未處理者），回傳筆數。</summary>
    Task<long> RevokePendingAsync(string email, DateTime revokedAt, CancellationToken ct);

    /// <summary>全部邀請，新的在前。</summary>
    Task<IReadOnlyList<Invitation>> ListAsync(CancellationToken ct);
}
