using FluentAssertions;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Tests.Fixtures;

namespace MyCollection.Tests.Integration;

[Collection(MongoCollection.Name)]
public class MongoUserRepositoryTests(MongoFixture fixture) : IAsyncLifetime
{
    private MongoUserRepository _sut = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _sut = new MongoUserRepository(fixture.Context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static User NewUser(string email = "adam@example.com") => new()
    {
        Id = ObjectId.GenerateNewId(),
        Email = email,
        PasswordHash = "hash",
        DisplayName = "Adam",
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Insert_then_GetByEmail_roundtrips()
    {
        var user = NewUser();

        await _sut.InsertAsync(user, CancellationToken.None);
        var found = await _sut.GetByEmailAsync("adam@example.com", CancellationToken.None);

        found.Should().NotBeNull();
        found!.Id.Should().Be(user.Id);
        found.DisplayName.Should().Be("Adam");
    }

    [Fact]
    public async Task GetByEmail_is_case_insensitive_via_normalised_storage()
    {
        await _sut.InsertAsync(NewUser("Adam@Example.COM"), CancellationToken.None);

        var found = await _sut.GetByEmailAsync("adam@example.com", CancellationToken.None);

        found.Should().NotBeNull();
    }

    [Fact]
    public async Task Insert_duplicate_email_throws_ConflictException()
    {
        await _sut.InsertAsync(NewUser(), CancellationToken.None);

        var act = () => _sut.InsertAsync(NewUser(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task SetRefreshToken_then_GetByRefreshTokenHash_roundtrips()
    {
        var user = NewUser();
        await _sut.InsertAsync(user, CancellationToken.None);
        var expiry = DateTime.UtcNow.AddDays(7);

        await _sut.SetRefreshTokenAsync(user.Id, "token-hash", expiry, CancellationToken.None);
        var found = await _sut.GetByRefreshTokenHashAsync("token-hash", CancellationToken.None);

        found.Should().NotBeNull();
        found!.Id.Should().Be(user.Id);
        found.RefreshTokenExpiresAt.Should().BeCloseTo(expiry, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task GetByRefreshTokenHash_returns_null_after_token_cleared()
    {
        var user = NewUser();
        await _sut.InsertAsync(user, CancellationToken.None);
        await _sut.SetRefreshTokenAsync(user.Id, "token-hash", DateTime.UtcNow.AddDays(7), CancellationToken.None);

        await _sut.SetRefreshTokenAsync(user.Id, null, null, CancellationToken.None);

        var found = await _sut.GetByRefreshTokenHashAsync("token-hash", CancellationToken.None);
        found.Should().BeNull();
    }
}
