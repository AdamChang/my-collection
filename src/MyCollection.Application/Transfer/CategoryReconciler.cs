using MongoDB.Bson;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Transfer;

public sealed record CategoryRepoint(ObjectId FromCategoryId, ObjectId ToCategoryId);

public sealed record CategoryPlan(
    IReadOnlyList<ObjectId> Delete,
    IReadOnlyList<CategoryRepoint> Repoints,
    IReadOnlyList<string> KeptOrphanNames);

/// <summary>
/// 決定匯入時本機自訂品類的去留（spec §6.2 第 3 步）。純函式，不碰 IO。
///
/// 「同名改指」不是裝飾：兩台機器各自跑 Steam 同步時，
/// SyncCommand.EnsureDigitalCategoryAsync 會各自建立一個 id 不同的自訂「數位遊戲」品類。
/// 沒有這步，每來回匯入一次就多累積一個同名品類。名稱是唯一可用的錨點——ObjectId 天生對不上。
/// </summary>
public static class CategoryReconciler
{
    public static CategoryPlan Plan(
        IReadOnlyList<Category> localOwnCategories,
        IReadOnlyList<ArchiveCategory> archiveCategories,
        IReadOnlyList<Item> steamItems)
    {
        var archiveIds = archiveCategories.Select(c => c.Id).ToHashSet();
        var archiveByName = archiveCategories
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

        // 只需要「哪些品類仍被 Steam 品項引用」，不需要知道是哪幾筆品項：
        // RepointItemsAsync 以來源品類過濾，在執行當下對活資料操作。
        var referencedBySteam = steamItems.Select(i => i.CategoryId).ToHashSet();

        var delete = new List<ObjectId>();
        var repoints = new List<CategoryRepoint>();
        var keptOrphanNames = new List<string>();

        foreach (var local in localOwnCategories)
        {
            // 在封存檔中 → 刪掉，第 4 步會以同一個 id 重新寫入封存檔版本。
            // 即使有 Steam item 引用它也無妨，引用的 id 不變。
            if (archiveIds.Contains(local.Id))
            {
                delete.Add(local.Id);
                continue;
            }

            if (!referencedBySteam.Contains(local.Id))
            {
                delete.Add(local.Id);
                continue;
            }

            if (archiveByName.TryGetValue(local.Name, out var target))
            {
                repoints.Add(new CategoryRepoint(local.Id, target));
                delete.Add(local.Id);
                continue;
            }

            keptOrphanNames.Add(local.Name);
        }

        return new CategoryPlan(delete, repoints, keptOrphanNames);
    }
}
