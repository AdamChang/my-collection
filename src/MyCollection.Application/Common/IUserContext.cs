using MongoDB.Bson;

namespace MyCollection.Application.Common;

/// <summary>
/// 目前登入者。Repository 層的所有 filter 都必須以 <see cref="UserId"/> 起頭，
/// 這比在 Handler 逐一檢查可靠：忘記過濾的後果是查不到資料，而不是洩漏資料。
/// </summary>
public interface IUserContext
{
    /// <summary>未通過驗證時擲出 <see cref="Domain.Exceptions.ForbiddenException"/>。</summary>
    ObjectId UserId { get; }

    bool IsAuthenticated { get; }
}
