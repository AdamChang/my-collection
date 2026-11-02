using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MyCollection.Application.Auth;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Infrastructure.Mongo;

/// <summary>
/// 全案第二個跨 collection 的寫入，模式同 <see cref="MongoCategoryFieldRenamer"/>：
/// 單一操作自帶 session，不做通用 UnitOfWork。
/// </summary>
public sealed class MongoInvitationRedeemer(MongoContext context, ILogger<MongoInvitationRedeemer> logger)
    : IInvitationRedeemer
{
    /// <summary>所有失敗原因共用，避免外人藉由不同回應來試探碼或 email。</summary>
    public const string InvalidInvitation = "The invitation is invalid or no longer valid.";

    private static readonly FilterDefinitionBuilder<Invitation> Filter = Builders<Invitation>.Filter;

    public async Task RedeemAsync(string code, User newUser, DateTime now, CancellationToken ct)
    {
        using var session = await context.Database.Client.StartSessionAsync(cancellationToken: ct);

        await session.WithTransactionAsync(async (s, token) =>
        {
            // User 的 Id 已事先產生，所以 usedByUserId 能在同一個條件更新裡寫入
            var consumed = await context.Invitations.UpdateOneAsync(s,
                Filter.And(
                    Filter.Eq(x => x.Code, code),
                    Filter.Eq(x => x.Email, newUser.Email),
                    Filter.Eq(x => x.Status, InvitationStatus.Pending),
                    Filter.Gt(x => x.ExpiresAt, now)),
                Builders<Invitation>.Update
                    .Set(x => x.Status, InvitationStatus.Used)
                    .Set(x => x.UsedAt, now)
                    .Set(x => x.UsedByUserId, newUser.Id),
                cancellationToken: token);

            if (consumed.MatchedCount == 0)
            {
                var existing = await context.Invitations.Find(s, Filter.Eq(x => x.Code, code))
                    .FirstOrDefaultAsync(token);

                // 真實原因只記在 log；不記 email，避免個資進入 Cloud Logging
                logger.LogWarning("Invitation redemption rejected: {Reason} (invitation {InvitationId})",
                    ReasonFor(existing, newUser.Email, now), existing?.Id.ToString() ?? "none");

                throw new ForbiddenException(InvalidInvitation);
            }

            try
            {
                await context.Users.InsertOneAsync(s, newUser, cancellationToken: token);
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // 例外中止 transaction，上面的消耗一併回滾
                throw new ConflictException($"Email '{newUser.Email}' is already registered.");
            }

            return true;
        }, cancellationToken: ct);
    }

    private static string ReasonFor(Invitation? invitation, string email, DateTime now) => invitation switch
    {
        null => "unknown-code",
        { Status: InvitationStatus.Used } => "used",
        { Status: InvitationStatus.Revoked } => "revoked",
        _ when invitation.Email != email => "email-mismatch",
        _ when invitation.ExpiresAt <= now => "expired",
        _ => "concurrently-consumed"
    };
}
