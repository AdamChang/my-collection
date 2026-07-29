using MongoDB.Bson;

namespace MyCollection.Application.Common;

/// <summary>
/// 匯入前自動備份的存放區。刻意與 <see cref="IFileStorage"/> 分開：
/// media root 由 AllowAnonymous 的 GET /media/{**path} 對外提供，
/// 備份放在那裡等於把整份收藏資料庫掛在匿名端點上。
///
/// 不提供下載端點——開放就得重做一次授權設計，而使用者本人已在該台機器前。
/// 檔案位於 host 的 {BackupRoot}/{ownerId}/，直接取檔即可。
/// </summary>
public interface IBackupStore
{
    /// <summary>建立備份檔並回傳可寫入的 stream。呼叫端負責 Dispose。</summary>
    Task<Stream> CreateAsync(ObjectId ownerId, string fileName, CancellationToken ct);

    /// <summary>只保留該 ownerId 最新的 <paramref name="keep"/> 份，其餘刪除。</summary>
    Task PruneAsync(ObjectId ownerId, int keep, CancellationToken ct);
}
