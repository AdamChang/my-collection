using MyCollection.Application.Categories;
using MyCollection.Infrastructure.Providers.Igdb;
using MyCollection.Infrastructure.Providers.Psn;

namespace MyCollection.Infrastructure.Providers;

/// <summary>
/// 刻意不從 ProviderRegistry 取：registry 只含本部署有憑證的來源，
/// IGDB 曾因憑證缺失整組未註冊，那段期間若允許刪掉 igdbId，憑證補上後補完就寫不回宣告內。
/// "platform" 是 ADR-0006 的跨品類白名單，同樣以字面值定址。
/// </summary>
public sealed class ProviderFieldKeyCatalog : IProtectedFieldKeys
{
    private static readonly HashSet<string> Keys = SteamFields.All
        .Concat(IgdbFields.All)
        .Concat(PsnFields.All)
        .Select(f => f.Key)
        .Append("platform")
        .ToHashSet(StringComparer.Ordinal);

    public bool IsProtected(string key) => Keys.Contains(key);
}
