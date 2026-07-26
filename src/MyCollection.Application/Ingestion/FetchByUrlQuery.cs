using FluentValidation;
using MediatR;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Ingestion;

public record FetchByUrlQuery(string Url, string Provider = "opengraph") : IRequest<FetchedMetadataDto>;

/// <summary>供前端預填表單，不直接建立品項。</summary>
public record FetchedMetadataDto(
    string Provider,
    string ExternalId,
    string Name,
    string? Description,
    string? ImageUrl,
    IReadOnlyDictionary<string, object?> Attributes);

public sealed class FetchByUrlQueryValidator : AbstractValidator<FetchByUrlQuery>
{
    public FetchByUrlQueryValidator()
    {
        RuleFor(x => x.Provider).NotEmpty();

        RuleFor(x => x.Url)
            .NotEmpty()
            .Must(BeAnHttpUrl)
            .WithMessage("Url must be an absolute http(s) URL.");
    }

    private static bool BeAnHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

public sealed class FetchByUrlQueryHandler(ProviderRegistry registry)
    : IRequestHandler<FetchByUrlQuery, FetchedMetadataDto>
{
    public async Task<FetchedMetadataDto> Handle(FetchByUrlQuery request, CancellationToken cancellationToken)
    {
        var provider = registry.Require(request.Provider, ProviderCapability.UrlLookup);

        var item = await provider.FetchByUrlAsync(new Uri(request.Url), cancellationToken)
                   ?? throw new NotFoundException("Metadata", request.Url);

        return new FetchedMetadataDto(
            provider.Key,
            item.ExternalId,
            item.Name,
            item.Description,
            item.ImageUrl?.ToString(),
            item.Attributes);
    }
}
