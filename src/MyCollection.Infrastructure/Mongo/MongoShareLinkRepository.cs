using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Common;
using MyCollection.Application.Sharing;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoShareLinkRepository(MongoContext context, IUserContext userContext) : IShareLinkRepository
{
    private static readonly FilterDefinitionBuilder<ShareLink> Filter = Builders<ShareLink>.Filter;

    private IMongoCollection<ShareLink> Links => context.ShareLinks;

    public async Task<IReadOnlyList<ShareLink>> ListAsync(CancellationToken ct) =>
        await Links
            .Find(Filter.Eq(x => x.OwnerId, userContext.UserId))
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

    public Task<ShareLink?> GetBySlugAsync(string slug, CancellationToken ct) =>
        Links.Find(Filter.Eq(x => x.Slug, slug)).FirstOrDefaultAsync(ct)!;

    public async Task InsertAsync(ShareLink link, CancellationToken ct)
    {
        link.OwnerId = userContext.UserId;

        try
        {
            await Links.InsertOneAsync(link, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ConflictException($"Share slug '{link.Slug}' is already taken.");
        }
    }

    public async Task DeleteAsync(ObjectId id, CancellationToken ct)
    {
        var result = await Links.DeleteOneAsync(
            Filter.And(Filter.Eq(x => x.Id, id), Filter.Eq(x => x.OwnerId, userContext.UserId)), ct);

        if (result.DeletedCount == 0)
        {
            throw new NotFoundException(nameof(ShareLink), id);
        }
    }
}
