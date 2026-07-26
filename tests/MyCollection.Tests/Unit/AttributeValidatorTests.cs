using System.Text.Json;
using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class AttributeValidatorTests
{
    private readonly AttributeValidator _sut = new();

    private static Category CategoryWith(params CategoryField[] fields) => new()
    {
        Id = ObjectId.GenerateNewId(),
        Name = "公仔",
        Kind = CategoryKind.Physical,
        Fields = fields.ToList()
    };

    private static CategoryField Field(string key, FieldType type, bool required = false, string[]? options = null) =>
        new() { Key = key, Label = key, Type = type, Required = required, Options = options?.ToList() };

    private static BsonDocument Attributes(string json) => BsonJson.ToBson(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void Accepts_valid_attributes()
    {
        var category = CategoryWith(
            Field("brand", FieldType.Select, required: true, options: ["GSC", "ALTER"]),
            Field("scale", FieldType.Text),
            Field("height", FieldType.Number),
            Field("releasedAt", FieldType.Date),
            Field("isLimited", FieldType.Bool),
            Field("productUrl", FieldType.Url));

        var failures = _sut.Validate(category, Attributes("""
            {
              "brand": "GSC",
              "scale": "1/8",
              "height": 200,
              "releasedAt": "2026-01-15T00:00:00Z",
              "isLimited": true,
              "productUrl": "https://www.goodsmile.com/x"
            }
            """));

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Rejects_missing_required_field()
    {
        var category = CategoryWith(Field("brand", FieldType.Text, required: true));

        var failures = _sut.Validate(category, Attributes("{}"));

        failures.Should().ContainSingle();
        failures[0].PropertyName.Should().Be("attributes.brand");
        failures[0].ErrorMessage.Should().Contain("required");
    }

    [Fact]
    public void Rejects_null_for_required_field()
    {
        var category = CategoryWith(Field("brand", FieldType.Text, required: true));

        var failures = _sut.Validate(category, Attributes("""{ "brand": null }"""));

        failures.Should().ContainSingle();
    }

    [Fact]
    public void Allows_null_for_optional_field()
    {
        var category = CategoryWith(Field("brand", FieldType.Text));

        _sut.Validate(category, Attributes("""{ "brand": null }""")).Should().BeEmpty();
    }

    [Fact]
    public void Rejects_unknown_attribute_key()
    {
        var category = CategoryWith(Field("brand", FieldType.Text));

        var failures = _sut.Validate(category, Attributes("""{ "brand": "GSC", "colour": "red" }"""));

        failures.Should().ContainSingle();
        failures[0].PropertyName.Should().Be("attributes.colour");
        failures[0].ErrorMessage.Should().Contain("not defined");
    }

    [Fact]
    public void Rejects_value_outside_select_options()
    {
        var category = CategoryWith(Field("brand", FieldType.Select, options: ["GSC", "ALTER"]));

        var failures = _sut.Validate(category, Attributes("""{ "brand": "MegaHouse" }"""));

        failures.Should().ContainSingle();
        failures[0].ErrorMessage.Should().Contain("GSC");
    }

    [Theory]
    [InlineData("Number", "\"not-a-number\"")]
    [InlineData("Bool", "\"yes\"")]
    [InlineData("Date", "\"15 January\"")]
    [InlineData("Url", "\"not a url\"")]
    [InlineData("Text", "123")]
    public void Rejects_wrong_type(string type, string jsonValue)
    {
        var category = CategoryWith(Field("value", Enum.Parse<FieldType>(type)));

        var failures = _sut.Validate(category, Attributes($$"""{ "value": {{jsonValue}} }"""));

        failures.Should().ContainSingle();
        failures[0].PropertyName.Should().Be("attributes.value");
    }

    [Fact]
    public void Reports_every_failure_not_just_the_first()
    {
        var category = CategoryWith(
            Field("brand", FieldType.Text, required: true),
            Field("height", FieldType.Number));

        var failures = _sut.Validate(category, Attributes("""{ "height": "tall", "colour": "red" }"""));

        failures.Should().HaveCount(3);
    }

    [Fact]
    public void Rejects_steam_shaped_attributes_against_an_undeclared_category()
    {
        // 重現 SyncCommandHandler.EnsureDigitalCategoryAsync 自動建立品類時 Fields = [] 的情境：
        // SteamProvider.ToExternalItem 產生的 attributes 全部都不是「已宣告」的欄位。
        var category = CategoryWith();

        var failures = _sut.Validate(category, Attributes("""
            {
              "playtimeForever": 120,
              "headerUrl": "https://cdn.cloudflare.steamstatic.com/steam/apps/620/header.jpg",
              "iconUrl": "https://media.steampowered.com/steamcommunity/public/images/apps/620/abc.jpg"
            }
            """));

        failures.Should().NotBeEmpty(
            "品類沒有宣告這些欄位時，同步寫入的 attributes 原樣送回 UpdateItemCommand 就會被拒絕");
        failures.Select(f => f.PropertyName).Should().Contain([
            "attributes.playtimeForever", "attributes.headerUrl", "attributes.iconUrl"
        ]);
    }

    [Fact]
    public void BsonJson_roundtrips_nested_structures()
    {
        var bson = Attributes("""{ "spec": { "scale": "1/8", "tags": ["a", "b"] }, "count": 3 }""");

        var dictionary = BsonJson.ToDictionary(bson);

        dictionary["count"].Should().Be(3);
        bson["spec"]["tags"].AsBsonArray.Select(x => x.AsString).Should().BeEquivalentTo("a", "b");
    }
}
