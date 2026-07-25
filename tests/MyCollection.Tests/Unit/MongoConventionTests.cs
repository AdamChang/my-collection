using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;

namespace MyCollection.Tests.Unit;

public class MongoConventionTests
{
    public MongoConventionTests() => MongoConventions.Register();

    [Fact]
    public void Serializes_properties_in_camel_case_and_dates_as_utc()
    {
        var user = new User
        {
            Id = ObjectId.GenerateNewId(),
            Email = "a@b.c",
            PasswordHash = "hash",
            DisplayName = "Adam",
            CreatedAt = new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc)
        };

        var doc = user.ToBsonDocument();

        doc.Contains("displayName").Should().BeTrue();
        doc.Contains("DisplayName").Should().BeFalse();
        doc["createdAt"].BsonType.Should().Be(BsonType.DateTime);
        doc["refreshTokenHash"].IsBsonNull.Should().BeTrue();
    }
}
