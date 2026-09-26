using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Application.Items;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class AttributeChangesTests
{
    private static readonly Category Category = new()
    {
        Id = ObjectId.GenerateNewId(),
        Name = "公仔",
        Fields =
        [
            new CategoryField { Key = "brand", Label = "廠商", Type = FieldType.Text },
            new CategoryField { Key = "scale", Label = "比例", Type = FieldType.Text },
            new CategoryField { Key = "note", Label = "備註", Type = FieldType.Text }
        ]
    };

    [Fact]
    public void Declared_keys_with_values_are_set_null_or_absent_are_unset()
    {
        var requested = new BsonDocument { { "brand", "GSC" }, { "scale", BsonNull.Value } };

        var changes = AttributeChanges.ForDeclaredFields(Category, requested);

        changes.Set.Should().BeEquivalentTo(new BsonDocument("brand", "GSC"));
        changes.Unset.Should().BeEquivalentTo(["scale", "note"]);
    }

    [Fact]
    public void Empty_string_is_a_value_not_a_clear()
    {
        var changes = AttributeChanges.ForDeclaredFields(Category, new BsonDocument("note", ""));

        changes.Set["note"].AsString.Should().BeEmpty("空字串的語意維持現狀，required 檢查也把它當成有值");
        changes.Unset.Should().NotContain("note");
    }

    [Fact]
    public void ApplyTo_keeps_keys_outside_the_changes_and_does_not_mutate_input()
    {
        var current = new BsonDocument { { "brand", "Old" }, { "scale", "1/8" }, { "legacyNote", "限定版" } };
        var changes = new AttributeChanges(new BsonDocument("brand", "New"), ["scale"]);

        var result = changes.ApplyTo(current);

        result.Should().BeEquivalentTo(new BsonDocument { { "brand", "New" }, { "legacyNote", "限定版" } });
        current["scale"].AsString.Should().Be("1/8");
    }
}
