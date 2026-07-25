using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;

namespace MyCollection.Tests.Unit;

public class EntitySerializationTests
{
    public EntitySerializationTests() => MongoConventions.Register();

    [Fact]
    public void Category_serialises_enums_as_strings()
    {
        var category = new Category
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = ObjectId.GenerateNewId(),
            Name = "公仔",
            Icon = "figure",
            Kind = CategoryKind.Physical,
            Fields =
            [
                new CategoryField
                {
                    Key = "brand", Label = "廠商", Type = FieldType.Select,
                    Options = ["Good Smile", "ALTER"],
                    Required = true, Searchable = true, ShowOnCard = true
                }
            ],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var doc = category.ToBsonDocument();

        doc["kind"].AsString.Should().Be("Physical");
        doc["fields"][0]["type"].AsString.Should().Be("Select");
        doc["fields"][0]["key"].AsString.Should().Be("brand");
    }

    [Fact]
    public void Item_roundtrips_nested_attributes_document()
    {
        var item = new Item
        {
            Id = ObjectId.GenerateNewId(),
            OwnerId = ObjectId.GenerateNewId(),
            CategoryId = ObjectId.GenerateNewId(),
            Name = "初音ミク 1/8 スケール",
            Source = ItemSource.Manual,
            Attributes = new BsonDocument
            {
                { "brand", "Good Smile" },
                { "spec", new BsonDocument { { "scale", "1/8" }, { "height", 200 } } }
            },
            Acquisition = new Acquisition
            {
                AcquiredAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Price = new Money(12800m, "TWD"),
                Vendor = "GSC 官網"
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var restored = BsonSerializer.Deserialize<Item>(item.ToBsonDocument());

        restored.Attributes["spec"]["scale"].AsString.Should().Be("1/8");
        restored.Acquisition!.Price!.Amount.Should().Be(12800m);
        restored.Acquisition.Price.Currency.Should().Be("TWD");
        restored.Source.Should().Be(ItemSource.Manual);
        restored.LocationId.Should().BeNull();
        restored.Tags.Should().BeEmpty();
    }
}
