using FluentAssertions;
using MyCollection.Infrastructure.Providers;
using MyCollection.Infrastructure.Providers.Igdb;
using MyCollection.Infrastructure.Providers.Psn;

namespace MyCollection.Tests.Unit;

/// <summary>
/// 刻意不建 ProviderRegistry、不給任何 options：目錄是靜態的，
/// 不依賴本部署有哪些來源註冊（ADR-0012 §四）。
/// </summary>
public class ProviderFieldKeyCatalogTests
{
    private readonly ProviderFieldKeyCatalog _sut = new();

    [Theory]
    [InlineData(SteamFields.AppIdKey)]
    [InlineData(SteamFields.StoreUpdatedAtKey)]
    [InlineData(SteamFields.GenresKey)]
    [InlineData(IgdbFields.MarkerKey)]
    [InlineData(PsnFields.ProgressKey)]
    [InlineData(PsnFields.LastPlayedAtKey)]
    [InlineData("platform")] // ADR-0006 白名單
    public void Provider_and_platform_keys_are_protected(string key)
    {
        _sut.IsProtected(key).Should().BeTrue();
    }

    [Theory]
    [InlineData("brand")]
    [InlineData("SteamAppId")] // 大小寫敏感：欄位鍵比對一律 Ordinal
    public void User_keys_are_not_protected(string key)
    {
        _sut.IsProtected(key).Should().BeFalse();
    }

    [Fact]
    public void Catalog_covers_every_declared_provider_field()
    {
        var allDeclared = SteamFields.All.Concat(IgdbFields.All).Concat(PsnFields.All).Select(f => f.Key);

        allDeclared.Should().OnlyContain(k => _sut.IsProtected(k));
    }
}
