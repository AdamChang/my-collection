using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Infrastructure;
using MyCollection.Infrastructure.Providers.Psn;

namespace MyCollection.Tests.Unit;

public class PsnRegistrationTests
{
    private const string TypedClientName = nameof(PsnProvider);

    private static ServiceProvider Build()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mongo:ConnectionString"] = "mongodb://localhost:27017",
                ["Mongo:Database"] = "mycollection-test",
                ["SecretProtection:Key"] = Convert.ToBase64String(new byte[32]),
                ["Psn:TimeoutSeconds"] = "17"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Mock.Of<IUserContext>());
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public void Psn_is_always_registered_with_only_bulk_sync_capability()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        var psn = scope.ServiceProvider.GetRequiredService<ProviderRegistry>()
            .Require<IBulkSyncProvider>(ProviderKeys.Psn);

        psn.Should().BeOfType<PsnProvider>();
        ProviderCapabilities.Of(psn).Should().Be(ProviderCapability.BulkSync);
    }

    [Fact]
    public void Psn_options_are_bound_from_the_Psn_section()
    {
        using var provider = Build();

        provider.GetRequiredService<IOptions<PsnOptions>>().Value.TimeoutSeconds.Should().Be(17);
    }

    [Fact]
    public void Psn_typed_client_never_follows_authorization_redirects()
    {
        using var provider = Build();

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(TypedClientName);

        while (handler is DelegatingHandler { InnerHandler: not null } delegating)
        {
            handler = delegating.InnerHandler;
        }

        handler.Should().BeOfType<HttpClientHandler>()
            .Which.AllowAutoRedirect.Should().BeFalse();
    }
}
