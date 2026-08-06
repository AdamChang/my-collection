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

    /// <summary>預設 false。true 時公開投影才會額外納入 acquisition.price 與 acquisition.acquiredAt。</summary>
    public bool IncludePrice { get; set; }

    /// <summary>預設 false。true 時公開投影才會額外納入 rating。</summary>
    public bool IncludeRating { get; set; }

    /// <summary>Collage 拼貼牆同時可見的槽位數，1–10。</summary>
    public int CollageSlotCount { get; set; } = 4;

    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
