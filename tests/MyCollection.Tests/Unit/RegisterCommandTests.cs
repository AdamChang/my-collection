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
    private readonly Mock<IInvitationRedeemer> _redeemer = new();
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

    private const string Code = "ABCDE-FGHJK-MNPQR-STVWX";

    private RegisterCommandHandler CreateSut() =>
        new(_redeemer.Object, _hasher.Object, _tokens.Object, _time);

    [Theory]
    [InlineData("", "P@ssw0rd!", "Adam", Code)]
    [InlineData("not-an-email", "P@ssw0rd!", "Adam", Code)]
    [InlineData("a@b.c", "short", "Adam", Code)]
    [InlineData("a@b.c", "P@ssw0rd!", "", Code)]
    [InlineData("a@b.c", "P@ssw0rd!", "Adam", "")]
    public void Validator_rejects_invalid_input(string email, string password, string displayName, string invitationCode)
    {
        var result = new RegisterCommandValidator()
            .Validate(new RegisterCommand(email, password, displayName, invitationCode));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_accepts_valid_input()
    {
        var result = new RegisterCommandValidator()
            .Validate(new RegisterCommand("adam@example.com", "P@ssw0rd!", "Adam", Code));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_redeems_normalised_code_with_hashed_user()
    {
        string? redeemedCode = null;
        User? redeemedUser = null;
        _redeemer.Setup(r => r.RedeemAsync(It.IsAny<string>(), It.IsAny<User>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<string, User, DateTime, CancellationToken>((c, u, _, _) => (redeemedCode, redeemedUser) = (c, u))
            .Returns(Task.CompletedTask);

        var response = await CreateSut().Handle(
            new RegisterCommand(" Adam@Example.com ", "P@ssw0rd!", "Adam", "abcde-fghjk-mnpqr-stvwx"), CancellationToken.None);

        redeemedCode.Should().Be("ABCDEFGHJKMNPQRSTVWX");
        redeemedUser!.Email.Should().Be("adam@example.com");
        redeemedUser.PasswordHash.Should().Be("hashed");
        redeemedUser.PasswordHash.Should().NotContain("P@ssw0rd!");
        redeemedUser.RefreshTokenHash.Should().Be("refresh-hash");
        redeemedUser.CreatedAt.Should().Be(new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc));

        response.AccessToken.Should().Be("access-token");
        response.RefreshToken.Should().Be("refresh-token");
        response.ExpiresAt.Should().Be(new DateTime(2026, 7, 25, 3, 30, 0, DateTimeKind.Utc));
        response.User.Email.Should().Be("adam@example.com");
        response.User.Id.Should().Be(redeemedUser.Id.ToString());
    }
}
