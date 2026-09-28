using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Domain.Entities;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Auth;

public record InvitationDto(
    string Email,
    string Code,
    string Status,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? UsedAt,
    string? UsedByUserId);

public static class InvitationMapper
{
    /// <summary>「已過期」只存在於讀取端（見 <see cref="Invitation"/> 註解）。</summary>
    public static string StatusOf(Invitation invitation, DateTime now) =>
        invitation.Status == InvitationStatus.Pending && invitation.ExpiresAt <= now
            ? "Expired"
            : invitation.Status.ToString();

    public static InvitationDto ToDto(Invitation invitation, DateTime now) => new(
        invitation.Email,
        InvitationCode.Format(invitation.Code),
        StatusOf(invitation, now),
        invitation.CreatedAt,
        invitation.ExpiresAt,
        invitation.UsedAt,
        invitation.UsedByUserId?.ToString());

    public static string NormaliseEmail(string email) => email.Trim().ToLowerInvariant();
}

public record CreateInvitationCommand(string Email, int Days = 7) : IRequest<InvitationDto>;

public sealed class CreateInvitationCommandValidator : AbstractValidator<CreateInvitationCommand>
{
    public CreateInvitationCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Days).InclusiveBetween(1, 30);
    }
}

public sealed class CreateInvitationCommandHandler(
    IInvitationRepository invitations,
    IUserRepository users,
    TimeProvider timeProvider) : IRequestHandler<CreateInvitationCommand, InvitationDto>
{
    public async Task<InvitationDto> Handle(CreateInvitationCommand request, CancellationToken cancellationToken)
    {
        var email = InvitationMapper.NormaliseEmail(request.Email);

        // 必須在撤銷之前：被拒絕的請求不應作廢既有的有效邀請
        if (await users.GetByEmailAsync(email, cancellationToken) is not null)
        {
            throw new ConflictException($"Email '{email}' is already registered.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // 同一 email 同時只有一份有效邀請；兩步之間的競態由 ux_invitations_email_pending 擋下
        await invitations.RevokePendingAsync(email, now, cancellationToken);

        var invitation = new Invitation
        {
            Id = ObjectId.GenerateNewId(),
            Email = email,
            Code = InvitationCode.Generate(),
            Status = InvitationStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.AddDays(request.Days)
        };

        await invitations.InsertAsync(invitation, cancellationToken);

        return InvitationMapper.ToDto(invitation, now);
    }
}

public record RevokeInvitationCommand(string Email) : IRequest<long>;

public sealed class RevokeInvitationCommandValidator : AbstractValidator<RevokeInvitationCommand>
{
    public RevokeInvitationCommandValidator() => RuleFor(x => x.Email).NotEmpty();
}

public sealed class RevokeInvitationCommandHandler(
    IInvitationRepository invitations,
    TimeProvider timeProvider) : IRequestHandler<RevokeInvitationCommand, long>
{
    public Task<long> Handle(RevokeInvitationCommand request, CancellationToken cancellationToken) =>
        invitations.RevokePendingAsync(
            InvitationMapper.NormaliseEmail(request.Email),
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
}

public record ListInvitationsQuery : IRequest<IReadOnlyList<InvitationDto>>;

public sealed class ListInvitationsQueryHandler(
    IInvitationRepository invitations,
    TimeProvider timeProvider) : IRequestHandler<ListInvitationsQuery, IReadOnlyList<InvitationDto>>
{
    public async Task<IReadOnlyList<InvitationDto>> Handle(ListInvitationsQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var all = await invitations.ListAsync(cancellationToken);
        return all.Select(i => InvitationMapper.ToDto(i, now)).ToList();
    }
}
