using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class SyncJobMapperTests
{
    [Fact]
    public void Maps_every_counter_including_skipped()
    {
        var startedAt = new DateTime(2026, 8, 1, 3, 0, 0, DateTimeKind.Utc);
        var job = new SyncJob
        {
            Id = ObjectId.Parse("000000000000000000000009"),
            Provider = ProviderKeys.Igdb,
            Status = SyncStatus.Succeeded,
            Created = 1,
            Updated = 2,
            Failed = 3,
            Skipped = 4,
            Error = null,
            StartedAt = startedAt,
            FinishedAt = startedAt.AddSeconds(5)
        };

        var dto = SyncJobMapper.ToDto(job);

        dto.Id.Should().Be("000000000000000000000009");
        dto.Provider.Should().Be("igdb");
        dto.Status.Should().Be("Succeeded");
        dto.Created.Should().Be(1);
        dto.Updated.Should().Be(2);
        dto.Failed.Should().Be(3);
        dto.Skipped.Should().Be(4);
        dto.FinishedAt.Should().Be(startedAt.AddSeconds(5));
    }

    [Fact]
    public void Skipped_defaults_to_zero_when_not_set()
    {
        var job = new SyncJob
        {
            Id = ObjectId.GenerateNewId(),
            Provider = ProviderKeys.Steam,
            StartedAt = DateTime.UtcNow
        };

        SyncJobMapper.ToDto(job).Skipped.Should().Be(0);
    }
}
