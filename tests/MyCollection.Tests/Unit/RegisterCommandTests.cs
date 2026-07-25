using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Auth;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class RegisterCommandTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    public RegisterCommandTests()
    {
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");
        _tokens.Setup(t => t.CreateAccessToken(It.IsAny<User>())).Returns("access-token");
        _tokens.Setup(t => t.CreateRefreshToken()).Returns("refresh-token");
        _tokens.Setup(t => t.HashRefreshToken("refresh-token")).Returns("refresh-hash");
        _tokens.SetupGet(t => t.AccessTokenLifetime).Returns(TimeSpan.FromMinutes(30));
        _tokens.SetupGet(t => t.RefreshTokenLifetime).Returns(TimeSpan.FromDays(14));
    }

    private RegisterCommandHandler CreateSut() =>
        new(_users.Object, _hasher.Object, _tokens.Object, _time);

    [Theory]
    [InlineData("", "P@ssw0rd!", "Adam")]
    [InlineData("not-an-email", "P@ssw0rd!", "Adam")]
    [InlineData("a@b.c", "short", "Adam")]
    [InlineData("a@b.c", "P@ssw0rd!", "")]
    public void Validator_rejects_invalid_input(string email, string password, string displayName)
    {
        var result = new RegisterCommandValidator().Validate(new RegisterCommand(email, password, displayName));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_accepts_valid_input()
    {
        var result = new RegisterCommandValidator()
            .Validate(new RegisterCommand("adam@example.com", "P@ssw0rd!", "Adam"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_stores_hashed_password_and_refresh_token_hash()
    {
        User? inserted = null;
        _users.Setup(r => r.InsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => inserted = u)
            .Returns(Task.CompletedTask);

        var response = await CreateSut().Handle(
            new RegisterCommand("adam@example.com", "P@ssw0rd!", "Adam"), CancellationToken.None);

        inserted.Should().NotBeNull();
        inserted!.PasswordHash.Should().Be("hashed");
        inserted.PasswordHash.Should().NotContain("P@ssw0rd!");
        inserted.RefreshTokenHash.Should().Be("refresh-hash");
        inserted.CreatedAt.Should().Be(new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc));

        response.AccessToken.Should().Be("access-token");
        response.RefreshToken.Should().Be("refresh-token");
        response.ExpiresAt.Should().Be(new DateTime(2026, 7, 25, 3, 30, 0, DateTimeKind.Utc));
        response.User.Email.Should().Be("adam@example.com");
        response.User.Id.Should().Be(inserted.Id.ToString());
    }
}
