using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MyCollection.Tests.Fixtures;

public sealed class ApiFactory(MongoFixture mongo) : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = mongo.ConnectionString,
            ["Mongo:Database"] = mongo.DatabaseName,
            ["Jwt:Key"] = "integration-test-signing-key-at-least-32-bytes!!",
            ["Jwt:Issuer"] = "mycollection",
            ["Jwt:Audience"] = "mycollection-web",
            ["Storage:LocalRoot"] = Path.Combine(Path.GetTempPath(), "mycollection-tests", Guid.NewGuid().ToString("N")),
            ["SecretProtection:Key"] = Convert.ToBase64String(new byte[32])
        }));

        return base.CreateHost(builder);
    }
}
