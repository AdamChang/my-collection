namespace MyCollection.Application.Common;

/// <summary>
/// 檔案儲存抽象。所有路徑一律是以 '/' 分隔的相對路徑，實作負責解析成自己的定址方式。
/// 換成 Google Cloud Storage 時只需新增實作並改 Storage:Provider。
/// </summary>
public interface IFileStorage
{
    /// <returns>寫入後的相對路徑（與傳入相同，供呼叫端直接存進文件）。</returns>
    Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct);

    /// <summary>不存在時回傳 null。</summary>
    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct);

    /// <summary>不存在時不擲例外。</summary>
    Task DeleteAsync(string relativePath, CancellationToken ct);
}
