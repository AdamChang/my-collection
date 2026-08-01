using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Bson;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;

namespace MyCollection.Tests.Unit;

public class ExternalAccountCommandTests
{
    private readonly Mock<IExternalAccountRepository> _accounts = new();
    private readonly Mock<ISecretProtector> _protector = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero));

    private readonly ProviderRegistry _registry;

    public ExternalAccountCommandTests()
    {
        _protector.Setup(p => p.Protect("real-key")).Returns("protected-key");

        var steam = new Mock<IBulkSyncProvider>();
        steam.SetupGet(p => p.Key).Returns("steam");
        _registry = new ProviderRegistry([steam.Object]);
    }

    private LinkExternalAccountCommandHandler CreateSut() =>
        new(_accounts.Object, _protector.Object, _registry, _time);

    [Fact]
    public async Task Stores_the_api_key_encrypted_and_never_returns_it()
    {
        ExternalAccount? saved = null;
        _accounts.Setup(r => r.UpsertAsync(It.IsAny<ExternalAccount>(), It.IsAny<CancellationToken>()))
            .Callback<ExternalAccount, CancellationToken>((a, _) => saved = a)
            .Returns(Task.CompletedTask);

        var dto = await CreateSut().Handle(
            new LinkExternalAccountCommand("steam", "76561197960287930", "real-key"), CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.ProtectedApiKey.Should().Be("protected-key");
        saved.ProtectedApiKey.Should().NotContain("real-key");
        saved.ExternalUserId.Should().Be("76561197960287930");
        saved.UpdatedAt.Should().Be(new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Utc));

        dto.Provider.Should().Be("steam");
        dto.ExternalUserId.Should().Be("76561197960287930");
        dto.GetType().GetProperties().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Rejects_an_unknown_provider()
    {
        var act = () => CreateSut().Handle(
            new LinkExternalAccountCommand("psn", "user", "key"), CancellationToken.None);

        await act.Should().ThrowAsync<Domain.Exceptions.NotFoundException>();
    }

    [Fact]
    public void Validator_requires_all_fields()
    {
        var validator = new LinkExternalAccountCommandValidator();

        validator.Validate(new LinkExternalAccountCommand("", "id", "key")).IsValid.Should().BeFalse();
        validator.Validate(new LinkExternalAccountCommand("steam", "", "key")).IsValid.Should().BeFalse();
        validator.Validate(new LinkExternalAccountCommand("steam", "id", "")).IsValid.Should().BeFalse();
        validator.Validate(new LinkExternalAccountCommand("steam", "id", "key")).IsValid.Should().BeTrue();
    }
}
