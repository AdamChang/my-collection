using FluentValidation.Results;
using MongoDB.Bson;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

/// <summary>
/// 匯入階段一。回傳全部失敗（不短路），讓使用者一次看完要修什麼。
/// 這一步跑完之前不得寫入任何資料。
/// </summary>
public sealed class ArchiveValidator(IAttributeValidator attributeValidator)
{
    /// <param name="systemCategories">
    /// 系統品類（OwnerId == null）。它們的 id 是跨機器固定的常數，
    /// 引用它們的 item 不需要在封存檔中帶著品類定義。
    /// </param>
    public IReadOnlyList<ValidationFailure> Validate(
        ArchiveManifest manifest,
        IReadOnlyList<Category> systemCategories)
    {
        // schemaVersion 不在這裡檢查：ArchiveManifestSerializer.Read 會在反序列化之前
        // 就擋掉版本不符的封存檔並擲 InvalidArchiveException。放在這裡只會是永遠不成立的死碼。
        var failures = new List<ValidationFailure>();

        for (var i = 0; i < manifest.Categories.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(manifest.Categories[i].Name))
            {
                failures.Add(new ValidationFailure($"categories[{i}].name", "Category name must not be blank."));
            }
        }

        var schemaById = new Dictionary<ObjectId, Category>();

        foreach (var category in systemCategories)
        {
            schemaById[category.Id] = category;
        }

        foreach (var category in manifest.Categories)
        {
            // 驗證只需要 schema（Fields），OwnerId 無關緊要。
            schemaById[category.Id] = ArchiveMapper.ToDomain(category, ownerId: null);
        }

        for (var i = 0; i < manifest.Items.Count; i++)
        {
            var item = manifest.Items[i];

            if (string.IsNullOrWhiteSpace(item.Name))
            {
                failures.Add(new ValidationFailure($"items[{i}].name", "Item name must not be blank."));
            }

            if (!schemaById.TryGetValue(item.CategoryId, out var category))
            {
                failures.Add(new ValidationFailure(
                    $"items[{i}].categoryId",
                    $"Item '{item.Name}' points at category '{item.CategoryId}', " +
                    "which is neither in the archive nor a system category."));

                continue;
            }

            foreach (var failure in attributeValidator.Validate(category, item.Attributes))
            {
                failures.Add(new ValidationFailure(
                    $"items[{i}].{failure.PropertyName}", failure.ErrorMessage));
            }
        }

        return failures;
    }
}
