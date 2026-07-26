using MyCollection.Domain.Entities;

namespace MyCollection.Application.Ingestion;

public interface IExternalAccountRepository
{
    Task<ExternalAccount?> GetAsync(string provider, CancellationToken ct);

    Task<IReadOnlyList<ExternalAccount>> ListAsync(CancellationToken ct);

    /// <summary>同一 provider 已綁定時覆寫。</summary>
    Task UpsertAsync(ExternalAccount account, CancellationToken ct);

    Task DeleteAsync(string provider, CancellationToken ct);
}
