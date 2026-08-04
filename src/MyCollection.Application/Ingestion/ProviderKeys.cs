namespace MyCollection.Application.Ingestion;

/// <summary>
/// Provider key 是寫進 externalRef.provider 與 API 路由的識別字串。
/// 集中一處，避免 Application 層散落字面值。
/// </summary>
public static class ProviderKeys
{
    public const string Steam = "steam";
    public const string Psn = "psn";
    public const string OpenGraph = "opengraph";
    public const string Igdb = "igdb";
}
