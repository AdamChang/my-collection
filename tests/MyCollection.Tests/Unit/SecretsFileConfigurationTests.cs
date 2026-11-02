using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MyCollection.Api;

namespace MyCollection.Tests.Unit;

public class SecretsFileConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Without_path_adds_no_source(string? path)
    {
        var builder = new ConfigurationBuilder();

        builder.AddSecretsFile(path);

        builder.Sources.Should().BeEmpty();
    }

    [Fact]
    public void Existing_file_provides_every_secret()
    {
        var path = Path.Combine(Path.GetTempPath(), $"secrets-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {"Jwt":{"Key":"jwt-key"},"SecretProtection":{"Key":"protection-key"},"Igdb":{"ClientId":"client-id","ClientSecret":"client-secret"}}
            """);

        try
        {
            var configuration = new ConfigurationBuilder().AddSecretsFile(path).Build();

            configuration["Jwt:Key"].Should().Be("jwt-key");
            configuration["SecretProtection:Key"].Should().Be("protection-key");
            configuration["Igdb:ClientId"].Should().Be("client-id");
            configuration["Igdb:ClientSecret"].Should().Be("client-secret");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_file_fails_startup()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");

        var act = () => new ConfigurationBuilder().AddSecretsFile(path);

        act.Should().Throw<FileNotFoundException>();
    }
}
