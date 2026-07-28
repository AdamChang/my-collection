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

    /// <summary>
    /// 刪除整個目錄前綴底下的所有檔案。不存在時不擲例外。
    /// 逐檔刪除只能清掉 DB 有記錄的檔案，孤兒檔會殘留，因此需要這個方法。
    /// 實作必須以路徑區段為界，不可用字串前綴比對（<c>owner/item</c> 不得誤刪 <c>owner/item2</c>）。
    /// </summary>
    Task DeleteDirectoryAsync(string relativePrefix, CancellationToken ct);
}
