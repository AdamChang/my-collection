using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Infrastructure.Providers.Igdb;

namespace MyCollection.Tests.Unit;

public class SystemCategoryDefinitionsTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 3, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<string> KeysOf(ObjectId categoryId) =>
        SystemCategoryDefinitions.Create(Now)
            .Single(c => c.Id == categoryId)
            .Fields.Select(f => f.Key)
            .ToArray();

    [Theory]
    [MemberData(nameof(GameCategoryIds))]
    public void Game_categories_declare_every_igdb_field(ObjectId categoryId)
    {
        KeysOf(categoryId).Should().Contain(IgdbFields.All.Select(f => f.Key));
    }

    [Theory]
    [MemberData(nameof(GameCategoryIds))]
    public void Game_categories_do_not_declare_a_key_twice(ObjectId categoryId)
    {
        KeysOf(categoryId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Reuses_the_existing_labels_for_keys_the_categories_already_had()
    {
        var digital = SystemCategoryDefinitions.Create(Now)
            .Single(c => c.Id == SystemCategoryDefinitions.DigitalGameId);

        digital.Fields.Single(f => f.Key == "releaseDate").Label.Should().Be("發售日期");
        digital.Fields.Single(f => f.Key == "platform").Label.Should().Be("平台／商店");
    }

    [Fact]
    public void Keeps_igdb_platforms_separate_from_the_owned_platform_field()
    {
        var keys = KeysOf(SystemCategoryDefinitions.PhysicalGameId);

        keys.Should().Contain("platform", "使用者這一份收藏在哪個平台");
        keys.Should().Contain("platforms", "IGDB 說這款遊戲發行於哪些平台");
    }

    [Fact]
    public void Non_game_categories_are_untouched_by_igdb()
    {
        KeysOf(SystemCategoryDefinitions.MusicAlbumId).Should().NotContain(IgdbFields.MarkerKey);
        KeysOf(SystemCategoryDefinitions.MovieDiscId).Should().NotContain(IgdbFields.MarkerKey);
    }

    public static TheoryData<ObjectId> GameCategoryIds() =>
    [
        SystemCategoryDefinitions.PhysicalGameId,
        SystemCategoryDefinitions.DigitalGameId
    ];
}
