using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Items;

/// <summary>
/// 一次品項更新對 attributes 的逐鍵變更：<see cref="Set"/> 寫入、<see cref="Unset"/> 移除，
/// 其餘的鍵原封不動。
///
/// 存在的理由是 ADR-0012 §三「撤回宣告不刪值」：整份覆寫 attributes 會把品項上的
/// 未宣告屬性一起刪掉（tech debt C1）。
/// </summary>
public sealed record AttributeChanges(BsonDocument Set, IReadOnlyCollection<string> Unset)
{
    /// <summary>
    /// 表單送出的是品類「已宣告欄位」的完整樣貌，所以以宣告集合為範圍做置換：
    /// 有值的鍵寫入；值為 null 或請求中沒出現的鍵移除；未宣告的鍵不在範圍內，一律不碰。
    ///
    /// 「沒出現就移除」而非「沒出現就保留」：前端清空欄位時可能送 null，也可能直接省略鍵，
    /// 兩者都必須是「清空」，否則會出現清不掉的欄位。背景寫入被舊表單覆蓋是 C2 的範圍，
    /// 「沒出現就保留」也救不了——表單每次都送出全部欄位。
    ///
    /// 呼叫前 requested 必須已通過 <see cref="IAttributeValidator"/>，因此不含未宣告的鍵。
    /// </summary>
    public static AttributeChanges ForDeclaredFields(Category category, BsonDocument requested)
    {
        var set = new BsonDocument();
        var unset = new List<string>();

        foreach (var field in category.Fields)
        {
            if (requested.TryGetValue(field.Key, out var value) && !value.IsBsonNull)
            {
                set[field.Key] = value;
            }
            else
            {
                unset.Add(field.Key);
            }
        }

        return new AttributeChanges(set, unset);
    }

    /// <summary>在記憶體中套用同一份變更，產生與資料庫寫入後一致的 attributes（含未宣告屬性）。</summary>
    public BsonDocument ApplyTo(BsonDocument current)
    {
        var result = current.DeepClone().AsBsonDocument;

        foreach (var key in Unset)
        {
            result.Remove(key);
        }

        foreach (var element in Set)
        {
            result[element.Name] = element.Value;
        }

        return result;
    }
}
