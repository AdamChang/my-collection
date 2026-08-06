using MongoDB.Bson;

namespace MyCollection.Domain.Entities;

public enum CategoryKind
{
    /// <summary>實體收藏，可有位置與購入資訊。</summary>
    Physical,

    /// <summary>數位收藏，LocationId 恆為 null，可被 Provider 同步。</summary>
    Digital
}

public enum FieldType
{
    Text,
    Number,
    Date,
    Select,
    Bool,
    Url
}

/// <summary>精選牆呈現版面。品類提供預設值，Item.DisplayMode 可覆寫。Collage 拼貼牆不受此篩選。</summary>
public enum DisplayMode
{
    List,
    Hero,
    Stats
}

public sealed class CategoryField
{
    public required string Key { get; set; }
    public required string Label { get; set; }
    public FieldType Type { get; set; }

    /// <summary>僅 <see cref="FieldType.Select"/> 使用。</summary>
    public List<string>? Options { get; set; }

    public bool Required { get; set; }
    public bool Searchable { get; set; }
    public bool ShowOnCard { get; set; }
}

public sealed class Category
{
    public ObjectId Id { get; set; }

    /// <summary>null = 系統內建品類，所有使用者可見但不可編輯。</summary>
    public ObjectId? OwnerId { get; set; }

    public required string Name { get; set; }
    public string Icon { get; set; } = "box";
    public CategoryKind Kind { get; set; } = CategoryKind.Physical;
    public List<CategoryField> Fields { get; set; } = [];
    public DisplayMode DefaultDisplayMode { get; set; } = DisplayMode.List;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
