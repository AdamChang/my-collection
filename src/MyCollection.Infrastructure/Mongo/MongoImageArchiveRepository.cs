using MongoDB.Driver;
using MyCollection.Application.Common;
using MyCollection.Application.Transfer;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoImageArchiveRepository(MongoContext context, IUserContext userContext)
    : IImageArchiveRepository
{
    public async Task<IReadOnlyList<Item>> ListItemsWithImagesAsync(CancellationToken ct) =>
        await context.Items
            .Find(Builders<Item>.Filter.And(
                Builders<Item>.Filter.Eq(x => x.OwnerId, userContext.UserId),
                Builders<Item>.Filter.SizeGt(x => x.Images, 0)))
            .SortBy(x => x.Id)
            .ToListAsync(ct);
}
