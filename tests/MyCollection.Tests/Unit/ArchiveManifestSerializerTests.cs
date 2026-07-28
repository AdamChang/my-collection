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

    private static string WrittenJson(ArchiveManifest manifest)
    {
        using var buffer = new MemoryStream();
        ArchiveManifestSerializer.Write(buffer, manifest);

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// 涵蓋所有子物件：ShareLinks、Images、Acquisition 都刻意填值，
    /// 避免一個只測 Categories/Items 的假設漏掉這幾個子物件的序列化問題
    /// （例如 CreatedAt 沒設成 Utc 而讓 UtcOnlyDateTimeSerializer 中箭）。
    /// </summary>
    private static ArchiveManifest ManifestWith(BsonDocument attributes)
    {
        var categoryId = ObjectId.GenerateNewId();
        var itemId = ObjectId.GenerateNewId();

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
                    Fields = [new ArchiveCategoryField { Key = "label", Label = "廠牌", Type = FieldType.Text }],
                    CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            ],
            Items =
            [
                new ArchiveItem
                {
                    Id = itemId,
                    CategoryId = categoryId,
                    Name = "Kind of Blue",
                    Tags = ["jazz"],
                    Source = ItemSource.Manual,
                    Acquisition = new ArchiveAcquisition
                    {
                        AcquiredAt = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
                        Price = new ArchiveMoney { Amount = 899.5m, Currency = "TWD" },
                        Vendor = "光南大批發"
                    },
                    Attributes = attributes,
                    Images =
                    [
                        new ArchiveImage
                        {
                            Id = "img-1",
                            IsPrimary = true,
                            Order = 0,
                            File = ArchivePaths.Image(itemId, "img-1")
                        }
                    ],
                    CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            ],
            ShareLinks =
            [
                new ArchiveShareLink
                {
                    Slug = "showcase-abc",
                    Scope = ShareScope.Showcase,
                    IncludeCategoryIds = [categoryId],
                    IncludePrice = true,
                    ExpiresAt = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                    CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
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
    public void Round_trip_preserves_share_links_images_and_acquisition()
    {
        var original = ManifestWith([]);

        var result = RoundTrip(original);

        result.ShareLinks.Should().ContainSingle();
        result.ShareLinks[0].Slug.Should().Be("showcase-abc");
        result.ShareLinks[0].Scope.Should().Be(ShareScope.Showcase);
        result.ShareLinks[0].IncludeCategoryIds.Should().Equal(original.Categories[0].Id);
        result.ShareLinks[0].ExpiresAt.Should().Be(original.ShareLinks[0].ExpiresAt);

        result.Items[0].Images.Should().ContainSingle();
        result.Items[0].Images[0].Id.Should().Be("img-1");
        result.Items[0].Images[0].File.Should().Be(original.Items[0].Images[0].File);

        var acquisition = result.Items[0].Acquisition;
        acquisition.Should().NotBeNull();
        acquisition!.Vendor.Should().Be("光南大批發");
        acquisition.AcquiredAt.Should().Be(original.Items[0].Acquisition!.AcquiredAt);
        acquisition.Price.Should().NotBeNull();
        acquisition.Price!.Amount.Should().Be(899.5m);
        acquisition.Price.Currency.Should().Be("TWD");
    }

    [Fact]
    public void Written_json_uses_canonical_extended_json_markers()
    {
        var attributes = new BsonDocument
        {
            { "price", new BsonDecimal128(1234.56m) },
            { "playtime", new BsonInt64(42L) }
        };

        var json = WrittenJson(ManifestWith(attributes));

        json.Should().Contain("$oid")
            .And.Contain("$date")
            .And.Contain("$numberDecimal")
            .And.Contain("$numberLong");
    }

    [Fact]
    public void Read_throws_invalid_archive_exception_for_garbage_input()
    {
        using var buffer = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not json at all {{{"));

        var act = () => ArchiveManifestSerializer.Read(buffer);

        act.Should().Throw<InvalidArchiveException>();
    }

    [Fact]
    public void Read_throws_invalid_archive_exception_for_truncated_json()
    {
        var fullJson = WrittenJson(ManifestWith([]));
        var truncated = fullJson[..(fullJson.Length / 2)];
        using var buffer = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(truncated));

        var act = () => ArchiveManifestSerializer.Read(buffer);

        act.Should().Throw<InvalidArchiveException>();
    }

    [Fact]
    public void Read_throws_invalid_archive_exception_for_unsupported_schema_version()
    {
        var manifest = ManifestWith([]);
        manifest.SchemaVersion = 99;
        using var buffer = new MemoryStream();
        ArchiveManifestSerializer.Write(buffer, manifest);
        buffer.Position = 0;

        var act = () => ArchiveManifestSerializer.Read(buffer);

        act.Should().Throw<InvalidArchiveException>();
    }
}
