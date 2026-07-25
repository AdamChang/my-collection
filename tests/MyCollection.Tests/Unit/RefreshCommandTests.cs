using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Auth;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class RefreshCommandTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    public RefreshCommandTests()
    {
        _tokens.Setup(t => t.HashRefreshToken("old-token")).Returns("old-hash");
        _tokens.Setup(t => t.CreateRefreshToken()).Returns("new-token");
        _tokens.Setup(t => t.HashRefreshToken("new-token")).Returns("new-hash");
        _tokens.Setup(t => t.CreateAccessToken(It.IsAny<User>())).Returns("access-token");
        _tokens.SetupGet(t => t.AccessTokenLifetime).Returns(TimeSpan.FromMinutes(30));
        _tokens.SetupGet(t => t.RefreshTokenLifetime).Returns(TimeSpan.FromDays(14));
    }

    private static User UserWithToken(DateTime? expiresAt) => new()
    {
        Id = ObjectId.Parse("507f1f77bcf86cd799439011"),
        Email = "adam@example.com",
        PasswordHash = "hash",
        DisplayName = "Adam",
        RefreshTokenHash = "old-hash",
        RefreshTokenExpiresAt = expiresAt,
        CreatedAt = DateTime.UtcNow
    };

    private RefreshCommandHandler CreateSut() => new(_users.Object, _tokens.Object, _time);

    [Fact]
    public async Task Issues_new_pair_and_invalidates_old_token()
    {
        _users.Setup(r => r.GetByRefreshTokenHashAsync("old-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserWithToken(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)));

        var response = await CreateSut().Handle(new RefreshCommand("old-token"), CancellationToken.None);

        response.RefreshToken.Should().Be("new-token");
        _users.Verify(r => r.SetRefreshTokenAsync(
            It.IsAny<ObjectId>(), "new-hash", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unknown_token_throws_ForbiddenException()
    {
        _users.Setup(r => r.GetByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var act = () => CreateSut().Handle(new RefreshCommand("old-token"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Expired_token_throws_and_is_cleared()
    {
        var user = UserWithToken(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        _users.Setup(r => r.GetByRefreshTokenHashAsync("old-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var act = () => CreateSut().Handle(new RefreshCommand("old-token"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        _users.Verify(r => r.SetRefreshTokenAsync(user.Id, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
