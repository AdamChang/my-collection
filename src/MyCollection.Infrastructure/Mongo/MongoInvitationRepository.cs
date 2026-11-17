using MongoDB.Driver;
using MyCollection.Application.Auth;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

/// <summary>
/// 邀請是系統層級的資料，沒有 OwnerId，因此不經過 IUserContext 的 owner filter：
/// 只有匿名註冊與維運指令兩個入口，不存在「別人的邀請」。
/// </summary>
public sealed class MongoInvitationRepository(MongoContext context) : IInvitationRepository
{
    private static readonly FilterDefinitionBuilder<Invitation> Filter = Builders<Invitation>.Filter;

    public async Task InsertAsync(Invitation invitation, CancellationToken ct)
    {
        try
        {
            await context.Invitations.InsertOneAsync(invitation, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ConflictException($"A pending invitation for '{invitation.Email}' already exists.");
        }
    }

    public async Task<long> RevokePendingAsync(string email, DateTime revokedAt, CancellationToken ct)
    {
        var result = await context.Invitations.UpdateManyAsync(
            Filter.And(
                Filter.Eq(x => x.Email, email),
                Filter.Eq(x => x.Status, InvitationStatus.Pending)),
            Builders<Invitation>.Update
                .Set(x => x.Status, InvitationStatus.Revoked)
                .Set(x => x.RevokedAt, revokedAt),
            cancellationToken: ct);

        return result.ModifiedCount;
    }

    public async Task<IReadOnlyList<Invitation>> ListAsync(CancellationToken ct) =>
        await context.Invitations.Find(Filter.Empty)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
}
