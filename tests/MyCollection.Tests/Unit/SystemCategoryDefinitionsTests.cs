using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Infrastructure.Providers.Igdb;
using MyCollection.Infrastructure.Providers.Psn;

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

    [Fact]
    public void Digital_game_declares_optional_psn_trophy_fields_with_their_schema()
    {
        var fields = SystemCategoryDefinitions.Create(Now)
            .Single(c => c.Id == SystemCategoryDefinitions.DigitalGameId)
            .Fields;

        var progress = fields.Single(f => f.Key == "psnProgress");
        progress.Type.Should().Be(FieldType.Number);
        progress.Required.Should().BeFalse();
        progress.ShowOnCard.Should().BeTrue();

        var lastPlayedAt = fields.Single(f => f.Key == "psnLastPlayedAt");
        lastPlayedAt.Type.Should().Be(FieldType.Date);
        lastPlayedAt.Required.Should().BeFalse();
        lastPlayedAt.ShowOnCard.Should().BeFalse();
    }

    [Fact]
    public void Psn_field_instances_are_not_shared_between_create_calls()
    {
        var first = PsnFields.Create();
        var second = PsnFields.Create();

        first.Single(f => f.Key == "psnProgress").Label = "changed";

        second.Single(f => f.Key == "psnProgress").Label.Should().Be("PSN 獎盃完成度");
    }

    public static TheoryData<ObjectId> GameCategoryIds() =>
    [
        SystemCategoryDefinitions.PhysicalGameId,
        SystemCategoryDefinitions.DigitalGameId
    ];

    [Theory]
    [InlineData("scale")]
    [InlineData("manufacturer")]
    [InlineData("character")]
    [InlineData("material")]
    [InlineData("limitedEdition")]
    [InlineData("condition")]
    public void Plush_figure_declares_the_expected_fields(string key)
    {
        KeysOf(SystemCategoryDefinitions.PlushFigureId).Should().Contain(key);
    }

    [Theory]
    [InlineData("signedBy")]
    [InlineData("certificationNumber")]
    [InlineData("cardNumber")]
    [InlineData("series")]
    [InlineData("condition")]
    public void Trading_card_declares_the_expected_fields(string key)
    {
        KeysOf(SystemCategoryDefinitions.TradingCardId).Should().Contain(key);
    }

    public static TheoryData<ObjectId, DisplayMode> ExpectedDefaultDisplayModes() => new()
    {
        { SystemCategoryDefinitions.PhysicalGameId, DisplayMode.List },
        { SystemCategoryDefinitions.DigitalGameId, DisplayMode.Stats },
        { SystemCategoryDefinitions.MusicAlbumId, DisplayMode.List },
        { SystemCategoryDefinitions.MovieDiscId, DisplayMode.List },
        { SystemCategoryDefinitions.PlushFigureId, DisplayMode.Hero },
        { SystemCategoryDefinitions.TradingCardId, DisplayMode.Hero }
    };

    [Theory]
    [MemberData(nameof(ExpectedDefaultDisplayModes))]
    public void Each_system_category_has_the_expected_default_display_mode(ObjectId categoryId, DisplayMode expected)
    {
        SystemCategoryDefinitions.Create(Now)
            .Single(c => c.Id == categoryId)
            .DefaultDisplayMode.Should().Be(expected);
    }

    [Fact]
    public void Declares_exactly_six_system_categories_with_unique_ids()
    {
        var categories = SystemCategoryDefinitions.Create(Now);

        categories.Should().HaveCount(6);
        categories.Select(c => c.Id).Should().OnlyHaveUniqueItems();
    }
}
