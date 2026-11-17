using MyCollection.Domain.Entities;

namespace MyCollection.Application.Auth;

/// <summary>
/// 消耗邀請並建立帳號，兩者同時成功或同時失敗（ADR-0014 §二）。
/// 邀請無效（不存在、過期、已使用、已撤銷、email 不符）一律擲出同一訊息的
/// <see cref="Domain.Exceptions.ForbiddenException"/>；email 已被註冊擲出 <see cref="Domain.Exceptions.ConflictException"/>。
/// </summary>
public interface IInvitationRedeemer
{
    /// <param name="code">已經過 <see cref="InvitationCode.Normalise"/>。</param>
    /// <param name="newUser">Id 與正規化後的 Email 已填好。</param>
    /// <param name="now">判斷是否過期，並寫入 usedAt。</param>
    /// <param name="ct">取消權杖。</param>
    Task RedeemAsync(string code, User newUser, DateTime now, CancellationToken ct);
}
