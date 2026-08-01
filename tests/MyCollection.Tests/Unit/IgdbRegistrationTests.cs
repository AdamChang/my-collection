using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Infrastructure;
using MyCollection.Infrastructure.Providers.Igdb;

namespace MyCollection.Tests.Unit;

/// <summary>
/// IGDB 是選配功能，「有沒有註冊」在啟動時就決定完畢。
/// 這裡驗證容器真的組得起來——編譯過不代表 DI 圖解得開，
/// 而 Task 13 的其餘驗證都需要真實憑證才能做。
/// </summary>
public class IgdbRegistrationTests
{
    private static ServiceProvider Build(bool configured)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = "mongodb://localhost:27017",
            ["Mongo:Database"] = "mycollection-test",
            ["SecretProtection:Key"] = Convert.ToBase64String(new byte[32])
        };

        if (configured)
        {
            settings["Igdb:ClientId"] = "client";
            settings["Igdb:ClientSecret"] = "secret";
        }

        var services = new ServiceCollection();
        services.AddLogging();

        // IUserContext 由 Api 層綁 HttpContext 提供，AddInfrastructure 不負責註冊
        services.AddScoped(_ => Mock.Of<IUserContext>());

        services.AddInfrastructure(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        // ValidateScopes 會抓出 singleton 誤依賴 scoped 的 captive dependency
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public void Igdb_is_absent_from_the_registry_when_no_credentials_are_configured()
    {
        using var provider = Build(configured: false);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ProviderRegistry>()
            .All.Select(p => p.Key).Should().NotContain(ProviderKeys.Igdb);
    }

    [Fact]
    public void Igdb_resolves_as_a_search_provider_when_credentials_are_configured()
    {
        using var provider = Build(configured: true);
        using var scope = provider.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<ProviderRegistry>();

        registry.Require<ISearchProvider>(ProviderKeys.Igdb).Should().BeOfType<IgdbProvider>();
    }

    [Fact]
    public void The_token_cache_and_rate_limiter_are_shared_across_scopes()
    {
        using var provider = Build(configured: true);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        // 每個請求各自一份等於沒有快取、也等於沒有節流
        first.ServiceProvider.GetRequiredService<ITwitchTokenProvider>()
            .Should().BeSameAs(second.ServiceProvider.GetRequiredService<ITwitchTokenProvider>());
        first.ServiceProvider.GetRequiredService<IgdbRateLimiter>()
            .Should().BeSameAs(second.ServiceProvider.GetRequiredService<IgdbRateLimiter>());
    }

    [Fact]
    public void The_enrich_writer_is_registered_regardless_of_igdb_configuration()
    {
        using var provider = Build(configured: false);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IItemEnrichWriter>().Should().NotBeNull();
    }
}
