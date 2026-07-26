using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Ingestion;

/// <summary>刻意不含任何金鑰欄位——綁定資訊回傳前端時永遠看不到 API Key。</summary>
public record ExternalAccountDto(string Provider, string ExternalUserId, DateTime UpdatedAt);

public record LinkExternalAccountCommand(string Provider, string ExternalUserId, string ApiKey)
    : IRequest<ExternalAccountDto>;

public record UnlinkExternalAccountCommand(string Provider) : IRequest;

public record ListExternalAccountsQuery : IRequest<IReadOnlyList<ExternalAccountDto>>;

public sealed class LinkExternalAccountCommandValidator : AbstractValidator<LinkExternalAccountCommand>
{
    public LinkExternalAccountCommandValidator()
    {
        RuleFor(x => x.Provider).NotEmpty();
        RuleFor(x => x.ExternalUserId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ApiKey).NotEmpty().MaximumLength(256);
    }
}

public sealed class LinkExternalAccountCommandHandler(
    IExternalAccountRepository accounts,
    ISecretProtector secretProtector,
    ProviderRegistry registry,
    TimeProvider timeProvider) : IRequestHandler<LinkExternalAccountCommand, ExternalAccountDto>
{
    public async Task<ExternalAccountDto> Handle(LinkExternalAccountCommand request, CancellationToken cancellationToken)
    {
        var provider = registry.Require(request.Provider, ProviderCapability.BulkSync);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var account = new ExternalAccount
        {
            Id = ObjectId.GenerateNewId(),
            Provider = provider.Key,
            ExternalUserId = request.ExternalUserId.Trim(),
            ProtectedApiKey = secretProtector.Protect(request.ApiKey),
            CreatedAt = now,
            UpdatedAt = now
        };

        await accounts.UpsertAsync(account, cancellationToken);

        return new ExternalAccountDto(account.Provider, account.ExternalUserId, account.UpdatedAt);
    }
}

public sealed class UnlinkExternalAccountCommandHandler(IExternalAccountRepository accounts)
    : IRequestHandler<UnlinkExternalAccountCommand>
{
    public Task Handle(UnlinkExternalAccountCommand request, CancellationToken cancellationToken) =>
        accounts.DeleteAsync(request.Provider, cancellationToken);
}

public sealed class ListExternalAccountsQueryHandler(IExternalAccountRepository accounts)
    : IRequestHandler<ListExternalAccountsQuery, IReadOnlyList<ExternalAccountDto>>
{
    public async Task<IReadOnlyList<ExternalAccountDto>> Handle(ListExternalAccountsQuery request, CancellationToken cancellationToken)
    {
        var result = await accounts.ListAsync(cancellationToken);

        return result.Select(a => new ExternalAccountDto(a.Provider, a.ExternalUserId, a.UpdatedAt)).ToArray();
    }
}
