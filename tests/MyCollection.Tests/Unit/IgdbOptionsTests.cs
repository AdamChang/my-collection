using FluentAssertions;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Providers.Igdb;

namespace MyCollection.Tests.Unit;

public class IgdbOptionsTests
{
    [Theory]
    [InlineData("", "", false)]
    [InlineData("client-id", "", false)]
    [InlineData("", "client-secret", false)]
    [InlineData("   ", "   ", false)]
    [InlineData("client-id", "client-secret", true)]
    public void IsConfigured_requires_both_credentials(string clientId, string clientSecret, bool expected)
    {
        new IgdbOptions { ClientId = clientId, ClientSecret = clientSecret }
            .IsConfigured.Should().Be(expected);
    }

    [Fact]
    public void Fields_declare_the_marker_key_first()
    {
        IgdbFields.All[0].Key.Should().Be(IgdbFields.MarkerKey);
        IgdbFields.MarkerKey.Should().Be("igdbId");
    }

    [Fact]
    public void Fields_cover_every_attribute_the_mapper_writes()
    {
        IgdbFields.All.Select(f => f.Key).Should().BeEquivalentTo(
            "igdbId", "developer", "publisher", "releaseDate",
            "genres", "platforms", "igdbRating", "coverUrl");
    }

    [Fact]
    public void Fields_use_types_the_attribute_validator_accepts()
    {
        IgdbFields.All.Single(f => f.Key == "igdbId").Type.Should().Be(FieldType.Number);
        IgdbFields.All.Single(f => f.Key == "releaseDate").Type.Should().Be(FieldType.Date);
        IgdbFields.All.Single(f => f.Key == "coverUrl").Type.Should().Be(FieldType.Url);
        IgdbFields.All.Single(f => f.Key == "igdbRating").Type.Should().Be(FieldType.Number);
        IgdbFields.All.Single(f => f.Key == "genres").Type.Should().Be(FieldType.Text);
    }

    [Fact]
    public void No_field_is_required_because_igdb_data_is_patchy()
    {
        IgdbFields.All.Should().OnlyContain(f => !f.Required);
    }

    [Fact]
    public void Each_call_returns_independent_instances_so_callers_cannot_mutate_the_definition()
    {
        var first = IgdbFields.Create();
        first[0].Label = "tampered";

        IgdbFields.Create()[0].Label.Should().Be("IGDB ID");
    }
}
