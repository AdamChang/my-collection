using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Infrastructure.Security;

namespace MyCollection.Tests.Unit;

public class JwtTokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "mycollection",
        Audience = "mycollection-web",
        Key = "this-is-a-test-signing-key-with-at-least-32-bytes",
        AccessTokenMinutes = 30,
        RefreshTokenDays = 14
    };

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    private JwtTokenService CreateSut() => new(Microsoft.Extensions.Options.Options.Create(Options), _time);

    private static User NewUser() => new()
    {
        Id = ObjectId.Parse("507f1f77bcf86cd799439011"),
        Email = "adam@example.com",
        PasswordHash = "hash",
        DisplayName = "Adam",
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public void CreateAccessToken_embeds_sub_email_and_expiry()
    {
        var token = CreateSut().CreateAccessToken(NewUser());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value
            .Should().Be("507f1f77bcf86cd799439011");
        jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value
            .Should().Be("adam@example.com");
        jwt.Issuer.Should().Be("mycollection");
        jwt.Audiences.Should().ContainSingle().Which.Should().Be("mycollection-web");
        jwt.ValidTo.Should().BeCloseTo(new DateTime(2026, 7, 25, 3, 30, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CreateRefreshToken_is_random_each_call()
    {
        var sut = CreateSut();

        sut.CreateRefreshToken().Should().NotBe(sut.CreateRefreshToken());
    }

    [Fact]
    public void HashRefreshToken_is_deterministic()
    {
        var sut = CreateSut();
        var token = sut.CreateRefreshToken();

        sut.HashRefreshToken(token).Should().Be(sut.HashRefreshToken(token));
        sut.HashRefreshToken(token).Should().NotBe(token);
    }
}
