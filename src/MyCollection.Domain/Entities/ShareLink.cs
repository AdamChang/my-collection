using MongoDB.Bson;

namespace MyCollection.Domain.Entities;

public enum ShareScope
{
    /// <summary>只輸出 isShowcased = true 的品項。</summary>
    Showcase,

    /// <summary>輸出 IncludeCategoryIds 指定品類的全部品項。</summary>
    Category
}

public sealed class ShareLink
{
    public ObjectId Id { get; set; }
    public ObjectId OwnerId { get; set; }

    /// <summary>公開網址的識別碼，全域唯一。</summary>
    public required string Slug { get; set; }

    public ShareScope Scope { get; set; } = ShareScope.Showcase;
    public List<ObjectId> IncludeCategoryIds { get; set; } = [];

    /// <summary>預設 false。true 時公開投影才會額外納入 acquisition.price。</summary>
    public bool IncludePrice { get; set; }

    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
