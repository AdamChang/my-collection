using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Application.Transfer;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class CategoryReconcilerTests
{
    private static readonly ObjectId OwnerId = ObjectId.GenerateNewId();

    private static Category Local(ObjectId id, string name) => new()
    {
        Id = id,
        OwnerId = OwnerId,
        Name = name,
        CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static ArchiveCategory Archived(ObjectId id, string name) => new()
    {
        Id = id,
        Name = name,
        CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static Item SteamItem(ObjectId categoryId) => new()
    {
        Id = ObjectId.GenerateNewId(),
        OwnerId = OwnerId,
        CategoryId = categoryId,
        Name = "Half-Life",
        Source = ItemSource.Steam,
        CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void Category_present_in_archive_is_deleted_so_step_four_can_rewrite_it()
    {
        var id = ObjectId.GenerateNewId();

        var plan = CategoryReconciler.Plan([Local(id, "黑膠唱片")], [Archived(id, "黑膠唱片")], []);

        plan.Delete.Should().Equal(id);
        plan.Repoints.Should().BeEmpty();
        plan.KeptOrphanNames.Should().BeEmpty();
    }

    [Fact]
    public void Category_absent_from_archive_and_unreferenced_is_deleted()
    {
        var id = ObjectId.GenerateNewId();

        var plan = CategoryReconciler.Plan([Local(id, "公仔")], [], []);

        plan.Delete.Should().Equal(id);
    }

    [Fact]
    public void Orphan_category_with_a_same_named_archive_entry_is_repointed_then_deleted()
    {
        var localId = ObjectId.GenerateNewId();
        var archiveId = ObjectId.GenerateNewId();
        var steamItem = SteamItem(localId);

        var plan = CategoryReconciler.Plan(
            [Local(localId, "數位遊戲")],
            [Archived(archiveId, "數位遊戲")],
            [steamItem]);

        plan.Repoints.Should().ContainSingle();
        plan.Repoints[0].FromCategoryId.Should().Be(localId);
        plan.Repoints[0].ToCategoryId.Should().Be(archiveId);
        plan.Delete.Should().Equal(localId);
        plan.KeptOrphanNames.Should().BeEmpty();
    }

    [Fact]
    public void Orphan_category_without_a_same_named_archive_entry_is_kept_and_reported()
    {
        var localId = ObjectId.GenerateNewId();

        var plan = CategoryReconciler.Plan(
            [Local(localId, "數位遊戲")],
            [Archived(ObjectId.GenerateNewId(), "黑膠唱片")],
            [SteamItem(localId)]);

        plan.Delete.Should().BeEmpty();
        plan.Repoints.Should().BeEmpty();
        plan.KeptOrphanNames.Should().Equal("數位遊戲");
    }

    [Fact]
    public void Archive_membership_wins_over_the_orphan_rule()
    {
        // 同一個 id 既在封存檔中、又被 Steam item 引用：
        // 第 4 步會以封存檔版本重新寫入同一個 id，Steam item 的引用因此仍然有效，
        // 不需要 repoint。
        var id = ObjectId.GenerateNewId();

        var plan = CategoryReconciler.Plan([Local(id, "數位遊戲")], [Archived(id, "數位遊戲")], [SteamItem(id)]);

        plan.Delete.Should().Equal(id);
        plan.Repoints.Should().BeEmpty();
        plan.KeptOrphanNames.Should().BeEmpty();
    }
}
