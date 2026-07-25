using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Auth;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class LoginCommandTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    private static readonly User ExistingUser = new()
    {
        Id = ObjectId.Parse("507f1f77bcf86cd799439011"),
        Email = "adam@example.com",
        PasswordHash = "stored-hash",
        DisplayName = "Adam",
        CreatedAt = DateTime.UtcNow
    };

    public LoginCommandTests()
    {
        _tokens.Setup(t => t.CreateAccessToken(It.IsAny<User>())).Returns("access-token");
        _tokens.Setup(t => t.CreateRefreshToken()).Returns("refresh-token");
        _tokens.Setup(t => t.HashRefreshToken("refresh-token")).Returns("refresh-hash");
        _tokens.SetupGet(t => t.AccessTokenLifetime).Returns(TimeSpan.FromMinutes(30));
        _tokens.SetupGet(t => t.RefreshTokenLifetime).Returns(TimeSpan.FromDays(14));
    }

    private LoginCommandHandler CreateSut() => new(_users.Object, _hasher.Object, _tokens.Object, _time);

    [Fact]
    public async Task Rotates_refresh_token_on_success()
    {
        _users.Setup(r => r.GetByEmailAsync("adam@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingUser);
        _hasher.Setup(h => h.Verify("stored-hash", "P@ssw0rd!")).Returns(true);

        var response = await CreateSut().Handle(
            new LoginCommand("adam@example.com", "P@ssw0rd!"), CancellationToken.None);

        response.AccessToken.Should().Be("access-token");
        response.RefreshToken.Should().Be("refresh-token");
        _users.Verify(r => r.SetRefreshTokenAsync(
            ExistingUser.Id,
            "refresh-hash",
            new DateTime(2026, 8, 8, 3, 0, 0, DateTimeKind.Utc),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unknown_email_throws_ForbiddenException()
    {
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var act = () => CreateSut().Handle(new LoginCommand("nobody@example.com", "x"), CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Message.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task Wrong_password_throws_same_message_as_unknown_email()
    {
        _users.Setup(r => r.GetByEmailAsync("adam@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingUser);
        _hasher.Setup(h => h.Verify("stored-hash", "wrong")).Returns(false);

        var act = () => CreateSut().Handle(new LoginCommand("adam@example.com", "wrong"), CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Message.Should().Be("Invalid email or password.");
    }
}
