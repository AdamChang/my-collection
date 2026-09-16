using MongoDB.Bson;

namespace MyCollection.Application.Categories;

/// <summary>
/// 改名的「全做或全不做」承諾：schema 的鍵與該品類下所有品項的屬性一起搬，或一起不動。
/// 實作自己管 transaction，不把 session 外露給 Application。
/// </summary>
public interface ICategoryFieldRenamer
{
    /// <summary>
    /// 回傳搬移的品項數。若任一品項同時帶有 oldKey 與 newKey，擲 ConflictException 且不改任何東西——
    /// 未宣告屬性是資料，覆蓋等於刪（ADR-0012 §三）。
    /// </summary>
    Task<long> RenameAsync(ObjectId categoryId, string oldKey, string newKey, DateTime updatedAt, CancellationToken ct);
}
