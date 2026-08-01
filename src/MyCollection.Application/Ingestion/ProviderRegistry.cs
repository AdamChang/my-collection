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

    /// <summary>
    /// 解析並要求特定能力介面。回傳強型別，呼叫端不需再轉型，
    /// 也不可能出現「旗標檢查過了但方法不存在」。
    /// </summary>
    public T Require<T>(string key) where T : class, IMetadataProvider
    {
        var provider = Require(key);

        return provider as T
               ?? throw new ProviderException(
                   provider.Key, $"Provider '{provider.Key}' does not support {typeof(T).Name}.");
    }
}
