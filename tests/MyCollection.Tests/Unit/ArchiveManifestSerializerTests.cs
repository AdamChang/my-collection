using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Application.Transfer;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class ArchiveManifestSerializerTests
{
    private static ArchiveManifest RoundTrip(ArchiveManifest manifest)
    {
        using var buffer = new MemoryStream();
        ArchiveManifestSerializer.Write(buffer, manifest);
        buffer.Position = 0;

        return ArchiveManifestSerializer.Read(buffer);
    }

    private static ArchiveManifest ManifestWith(BsonDocument attributes)
    {
        var categoryId = ObjectId.GenerateNewId();

        return new ArchiveManifest
        {
            ExportedAt = new DateTime(2026, 7, 28, 3, 0, 0, DateTimeKind.Utc),
            Categories =
            [
                new ArchiveCategory
                {
                    Id = categoryId,
                    Name = "黑膠唱片",
                    Icon = "disc-3",
                    Kind = CategoryKind.Physical,
                    Fields = [new CategoryField { Key = "label", Label = "廠牌", Type = FieldType.Text }],
                    CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            ],
            Items =
            [
                new ArchiveItem
                {
                    Id = ObjectId.GenerateNewId(),
                    CategoryId = categoryId,
                    Name = "Kind of Blue",
                    Tags = ["jazz"],
                    Source = ItemSource.Manual,
                    Attributes = attributes,
                    CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            ]
        };
    }

    [Fact]
    public void Attributes_preserve_decimal128_across_round_trip()
    {
        var attributes = new BsonDocument { { "price", new BsonDecimal128(1234.56m) } };

        var value = RoundTrip(ManifestWith(attributes)).Items[0].Attributes["price"];

        value.BsonType.Should().Be(BsonType.Decimal128);
        value.AsDecimal.Should().Be(1234.56m);
    }

    [Fact]
    public void Attributes_preserve_int64_and_do_not_collapse_to_int32()
    {
        var attributes = new BsonDocument { { "playtime", new BsonInt64(42L) } };

        RoundTrip(ManifestWith(attributes)).Items[0].Attributes["playtime"]
            .BsonType.Should().Be(BsonType.Int64);
    }

    [Fact]
    public void Attributes_preserve_utc_datetime_across_round_trip()
    {
        var released = new DateTime(1959, 8, 17, 0, 0, 0, DateTimeKind.Utc);
        var attributes = new BsonDocument { { "releaseDate", new BsonDateTime(released) } };

        RoundTrip(ManifestWith(attributes)).Items[0].Attributes["releaseDate"]
            .ToUniversalTime().Should().Be(released);
    }

    [Fact]
    public void Round_trip_preserves_object_ids_and_enums()
    {
        var original = ManifestWith([]);

        var result = RoundTrip(original);

        result.SchemaVersion.Should().Be(ArchiveManifest.CurrentSchemaVersion);
        result.Categories[0].Id.Should().Be(original.Categories[0].Id);
        result.Categories[0].Kind.Should().Be(CategoryKind.Physical);
        result.Categories[0].Fields[0].Type.Should().Be(FieldType.Text);
        result.Items[0].CategoryId.Should().Be(original.Categories[0].Id);
        result.Items[0].Source.Should().Be(ItemSource.Manual);
        result.Items[0].Tags.Should().Equal("jazz");
    }

    [Fact]
    public void Written_json_uses_canonical_extended_json_markers()
    {
        using var buffer = new MemoryStream();
        ArchiveManifestSerializer.Write(buffer, ManifestWith([]));

        var json = System.Text.Encoding.UTF8.GetString(buffer.ToArray());

        json.Should().Contain("$oid").And.Contain("$date");
    }
}
