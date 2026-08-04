namespace MyCollection.Application.Ingestion;

/// <summary>
/// 由實作的介面推導能力旗標。Provider 不再自行宣告 Capabilities——
/// 同一個事實兩處來源，遲早漂移成「旗標說支援、方法沒實作」。
/// </summary>
public static class ProviderCapabilities
{
    public static ProviderCapability Of(IMetadataProvider provider) =>
        (provider is IBulkSyncProvider ? ProviderCapability.BulkSync : ProviderCapability.None)
        | (provider is IUrlLookupProvider ? ProviderCapability.UrlLookup : ProviderCapability.None)
        | (provider is ISearchProvider ? ProviderCapability.Search : ProviderCapability.None)
        | (provider is IExternalIdLookupProvider ? ProviderCapability.Enrich : ProviderCapability.None);
}
