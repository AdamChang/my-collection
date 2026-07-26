using MongoDB.Driver;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoExternalAccountRepository(MongoContext context, IUserContext userContext)
    : IExternalAccountRepository
{
    private static readonly FilterDefinitionBuilder<ExternalAccount> Filter = Builders<ExternalAccount>.Filter;

    private IMongoCollection<ExternalAccount> Accounts => context.ExternalAccounts;

    private FilterDefinition<ExternalAccount> OwnerFilter => Filter.Eq(x => x.OwnerId, userContext.UserId);

    public Task<ExternalAccount?> GetAsync(string provider, CancellationToken ct) =>
        Accounts.Find(Filter.And(OwnerFilter, Filter.Eq(x => x.Provider, provider))).FirstOrDefaultAsync(ct)!;

    public async Task<IReadOnlyList<ExternalAccount>> ListAsync(CancellationToken ct) =>
        await Accounts.Find(OwnerFilter).ToListAsync(ct);

    public Task UpsertAsync(ExternalAccount account, CancellationToken ct)
    {
        account.OwnerId = userContext.UserId;

        return Accounts.UpdateOneAsync(
            Filter.And(OwnerFilter, Filter.Eq(x => x.Provider, account.Provider)),
            Builders<ExternalAccount>.Update
                .Set(x => x.ExternalUserId, account.ExternalUserId)
                .Set(x => x.ProtectedApiKey, account.ProtectedApiKey)
                .Set(x => x.UpdatedAt, account.UpdatedAt)
                .SetOnInsert(x => x.CreatedAt, account.CreatedAt),
            new UpdateOptions { IsUpsert = true },
            ct);
    }

    public Task DeleteAsync(string provider, CancellationToken ct) =>
        Accounts.DeleteOneAsync(Filter.And(OwnerFilter, Filter.Eq(x => x.Provider, provider)), ct);
}
