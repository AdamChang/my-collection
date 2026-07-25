using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Mongo;

namespace MyCollection.Tests.Unit;

public class MongoConventionTests
{
    private enum SampleKind { First, Second }

    private sealed class SampleDocument
    {
        public ObjectId Id { get; set; }
        public SampleKind Kind { get; set; }
        public DateTime OccurredAt { get; set; }
        public DateTime? MaybeAt { get; set; }
    }

    private static User NewUser(DateTime createdAt) => new()
    {
        Id = ObjectId.GenerateNewId(),
        Email = "a@b.c",
        PasswordHash = "hash",
        DisplayName = "Adam",
        CreatedAt = createdAt
    };

    [Fact]
    public void Serialises_property_names_in_camel_case()
    {
        var doc = NewUser(DateTime.UtcNow).ToBsonDocument();

        doc.Contains("displayName").Should().BeTrue();
        doc.Contains("DisplayName").Should().BeFalse();
        doc["refreshTokenHash"].IsBsonNull.Should().BeTrue();
    }

    [Fact]
    public void Maps_the_Id_property_to_the_document_key()
    {
        var user = NewUser(DateTime.UtcNow);

        var doc = user.ToBsonDocument();

        doc.Contains("_id").Should().BeTrue("driver 必須把 Id 對應成 _id，否則唯一索引與 upsert 冪等性全部失效");
        doc.Contains("id").Should().BeFalse();
        doc["_id"].BsonType.Should().Be(BsonType.ObjectId);
        doc["_id"].AsObjectId.Should().Be(user.Id);
    }

    [Fact]
    public void Serialises_enums_as_strings()
    {
        var doc = new SampleDocument { Kind = SampleKind.Second, OccurredAt = DateTime.UtcNow }.ToBsonDocument();

        doc["kind"].BsonType.Should().Be(BsonType.String);
        doc["kind"].AsString.Should().Be("Second");
    }

    [Fact]
    public void Round_trips_utc_datetimes_without_shifting_them()
    {
        var createdAt = new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc);

        var restored = BsonSerializer.Deserialize<User>(NewUser(createdAt).ToBsonDocument());

        restored.CreatedAt.Should().Be(createdAt);
        restored.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void Refuses_to_persist_datetimes_that_are_not_utc(DateTimeKind kind)
    {
        var user = NewUser(new DateTime(2026, 7, 25, 3, 0, 0, kind));

        var act = () => user.ToBsonDocument();

        // BsonClassMapSerializer 會把成員序列化時拋出的例外包一層 BsonSerializationException
        // （附上類別／屬性名稱做診斷），所以驗證最終根因，而非最外層例外型別。
        var exception = act.Should().Throw<Exception>().Which;
        exception.GetBaseException().Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("Utc", "靜默把本地時間平移成 UTC 會讓購入日期與 token 到期時間無聲歪掉");
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void Refuses_to_persist_nullable_datetimes_that_are_not_utc(DateTimeKind kind)
    {
        var doc = new SampleDocument
        {
            OccurredAt = DateTime.UtcNow,
            MaybeAt = new DateTime(2026, 7, 25, 3, 0, 0, kind)
        };

        var act = () => doc.ToBsonDocument();

        var exception = act.Should().Throw<Exception>().Which;
        exception.GetBaseException().Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void Register_is_idempotent_across_repeated_calls()
    {
        var act = () =>
        {
            MongoConventions.Register();
            MongoConventions.Register();
            MongoConventions.Register();
        };

        act.Should().NotThrow("重複註冊必須是安全的 no-op");
    }

    [Fact]
    public void Register_is_safe_under_concurrent_first_use()
    {
        var act = () => Parallel.For(0, 64, _ => MongoConventions.Register());

        act.Should().NotThrow();

        // 併發呼叫後慣例必須仍然生效
        NewUser(DateTime.UtcNow).ToBsonDocument().Contains("displayName").Should().BeTrue();
    }
}
