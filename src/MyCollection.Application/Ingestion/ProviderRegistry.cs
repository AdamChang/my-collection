using MyCollection.Domain.Exceptions;

namespace MyCollection.Application.Ingestion;

/// <summary>
/// 依 Key 解析 Provider。新增 PSN / Discogs 只需多註冊一個 IMetadataProvider，
/// 這個類別與所有 Handler 都不用改。
/// </summary>
public sealed class ProviderRegistry(IEnumerable<IMetadataProvider> providers)
{
    private readonly Dictionary<string, IMetadataProvider> _byKey =
        providers.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IMetadataProvider> All => _byKey.Values;

    public IMetadataProvider Require(string key)
    {
        if (!_byKey.TryGetValue(key, out var provider))
        {
            throw new NotFoundException("Provider", key);
        }

        return provider;
    }

    public IMetadataProvider Require(string key, ProviderCapability capability)
    {
        var provider = Require(key);

        if (!provider.Capabilities.HasFlag(capability))
        {
            throw new ProviderException(provider.Key, $"Provider '{provider.Key}' does not support {capability}.");
        }

        return provider;
    }
}
