using MongoDB.Bson;
using MongoDB.Driver;
using MyCollection.Application.Common;
using MyCollection.Application.Transfer;
using MyCollection.Domain.Entities;

namespace MyCollection.Infrastructure.Mongo;

public sealed class MongoTransferRepository(MongoContext context, IUserContext userContext) : ITransferRepository
{
    private ObjectId Owner => userContext.UserId;

    private FilterDefinition<Item> OwnItems =>
        Builders<Item>.Filter.Eq(x => x.OwnerId, Owner);

    public async Task<IReadOnlyList<Category>> ListOwnCategoriesAsync(CancellationToken ct) =>
        await context.Categories
            .Find(Builders<Category>.Filter.Eq(x => x.OwnerId, Owner))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Item>> ListExportableItemsAsync(CancellationToken ct) =>
        await context.Items
            .Find(OwnItems & Builders<Item>.Filter.Ne(x => x.Source, ItemSource.Steam))
            .SortBy(x => x.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ShareLink>> ListOwnShareLinksAsync(CancellationToken ct) =>
        await context.ShareLinks
            .Find(Builders<ShareLink>.Filter.Eq(x => x.OwnerId, Owner))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Item>> ListSteamItemsAsync(CancellationToken ct) =>
        await context.Items
            .Find(OwnItems & Builders<Item>.Filter.Eq(x => x.Source, ItemSource.Steam))
            .ToListAsync(ct);

    public async Task DeleteNonSteamItemsAsync(CancellationToken ct) =>
        await context.Items.DeleteManyAsync(
            OwnItems & Builders<Item>.Filter.Ne(x => x.Source, ItemSource.Steam), ct);

    public async Task DeleteOwnShareLinksAsync(CancellationToken ct) =>
        await context.ShareLinks.DeleteManyAsync(
            Builders<ShareLink>.Filter.Eq(x => x.OwnerId, Owner), ct);

    public async Task DeleteCategoriesAsync(IReadOnlyList<ObjectId> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return;
        }

        await context.Categories.DeleteManyAsync(
            Builders<Category>.Filter.Eq(x => x.OwnerId, Owner)
            & Builders<Category>.Filter.In(x => x.Id, ids), ct);
    }

    public async Task RepointItemsAsync(
        IReadOnlyList<ObjectId> itemIds, ObjectId targetCategoryId, CancellationToken ct)
    {
        if (itemIds.Count == 0)
        {
            return;
        }

        await context.Items.UpdateManyAsync(
            OwnItems & Builders<Item>.Filter.In(x => x.Id, itemIds),
            Builders<Item>.Update.Set(x => x.CategoryId, targetCategoryId),
            cancellationToken: ct);
    }

    public async Task InsertCategoriesAsync(IReadOnlyList<Category> categories, CancellationToken ct)
    {
        if (categories.Count == 0)
        {
            return;
        }

        await context.Categories.InsertManyAsync(categories, cancellationToken: ct);
    }

    public async Task InsertItemsAsync(IReadOnlyList<Item> items, CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return;
        }

        await context.Items.InsertManyAsync(items, cancellationToken: ct);
    }

    public async Task InsertShareLinksAsync(IReadOnlyList<ShareLink> links, CancellationToken ct)
    {
        if (links.Count == 0)
        {
            return;
        }

        await context.ShareLinks.InsertManyAsync(links, cancellationToken: ct);
    }

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken ct) =>
        await context.ShareLinks
            .Find(Builders<ShareLink>.Filter.Eq(x => x.Slug, slug))
            .AnyAsync(ct);
}
