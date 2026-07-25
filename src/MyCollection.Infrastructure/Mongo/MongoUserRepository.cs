using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Auth;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoUserRepository(MongoContext context) : IUserRepository
{
    private IMongoCollection<User> Users => context.Users;

    public Task<User?> GetByIdAsync(ObjectId id, CancellationToken ct) =>
        Users.Find(Builders<User>.Filter.Eq(x => x.Id, id)).FirstOrDefaultAsync(ct)!;

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct) =>
        Users.Find(Builders<User>.Filter.Eq(x => x.Email, Normalise(email))).FirstOrDefaultAsync(ct)!;

    public Task<User?> GetByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct) =>
        Users.Find(Builders<User>.Filter.Eq(x => x.RefreshTokenHash, refreshTokenHash)).FirstOrDefaultAsync(ct)!;

    public async Task InsertAsync(User user, CancellationToken ct)
    {
        user.Email = Normalise(user.Email);

        try
        {
            await Users.InsertOneAsync(user, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ConflictException($"Email '{user.Email}' is already registered.");
        }
    }

    public Task SetRefreshTokenAsync(ObjectId id, string? refreshTokenHash, DateTime? expiresAt, CancellationToken ct) =>
        Users.UpdateOneAsync(
            Builders<User>.Filter.Eq(x => x.Id, id),
            Builders<User>.Update
                .Set(x => x.RefreshTokenHash, refreshTokenHash)
                .Set(x => x.RefreshTokenExpiresAt, expiresAt),
            cancellationToken: ct);

    private static string Normalise(string email) => email.Trim().ToLowerInvariant();
}
