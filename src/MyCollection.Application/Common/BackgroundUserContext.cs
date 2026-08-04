using MongoDB.Bson;

namespace MyCollection.Application.Common;

/// <summary>
/// 讓背景作業在自己的 DI scope 裡指定「這批工作屬於誰」。
///
/// Repository 的所有 filter 都以 IUserContext.UserId 起頭，但背景作業沒有 HTTP 請求脈絡，
/// HttpUserContext 會擲 ForbiddenException。既有的 ShowcaseImageDownloader 是繞過 repository
/// 直接用 MongoContext 解決的；補完需要用到 repository 的查詢邏輯，繞不過去，
/// 所以改成在 scope 裡把身分明講出來。
///
/// 只有背景作業會設定它。HTTP 請求的 scope 裡它永遠是空的，IUserContext 照舊解析到 HttpUserContext。
/// </summary>
public sealed class BackgroundUserContext
{
    public ObjectId? UserId { get; private set; }

    public void Set(ObjectId userId) => UserId = userId;
}

/// <summary>已知身分的 <see cref="IUserContext"/>，供背景作業使用。</summary>
public sealed class FixedUserContext(ObjectId userId) : IUserContext
{
    public ObjectId UserId { get; } = userId;

    public bool IsAuthenticated => true;
}
