using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyCollection.Application.Common;
using MyCollection.Infrastructure;
using MyCollection.Infrastructure.Storage;

namespace MyCollection.Tests.Unit;

public sealed class StorageProviderRegistrationTests
{
    [Fact]
    public void Local_provider_resolves_local_storage()
    {
        var root = Path.Combine(Path.GetTempPath(), "mycollection-tests", Guid.NewGuid().ToString("N"));
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Local",
            ["Storage:LocalRoot"] = root
        });

        try
        {
            provider.GetRequiredService<IFileStorage>().Should().BeOfType<LocalFileStorage>();
        }
        finally
        {
            provider.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Gcs_provider_requires_bucket()
    {
        var act = () => BuildProvider(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Gcs"
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Storage:Bucket is required*");
    }

    [Fact]
    public void Unknown_provider_fails_fast()
    {
        var act = () => BuildProvider(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Unknown"
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Unsupported storage provider*");
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}
