using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyCollection.Application.Auth;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;
using MyCollection.Application.Items;
using MyCollection.Application.Media;
using MyCollection.Application.Sharing;
using MyCollection.Application.Showcase;
using MyCollection.Application.Transfer;
using MyCollection.Infrastructure.Imaging;
using MyCollection.Infrastructure.Mongo;
using MyCollection.Infrastructure.Providers;
using MyCollection.Infrastructure.Providers.Igdb;
using MyCollection.Infrastructure.Security;
using MyCollection.Infrastructure.Storage;

namespace MyCollection.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<SecretProtectionOptions>(configuration.GetSection(SecretProtectionOptions.SectionName));
        services.Configure<SteamOptions>(configuration.GetSection(SteamOptions.SectionName));
        services.Configure<IgdbOptions>(configuration.GetSection(IgdbOptions.SectionName));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<MongoContext>();

        services.AddScoped<IUserRepository, MongoUserRepository>();
        services.AddScoped<ICategoryRepository, MongoCategoryRepository>();
        services.AddScoped<IItemRepository, MongoItemRepository>();
        services.AddScoped<IShareLinkRepository, MongoShareLinkRepository>();
        services.AddScoped<IImageArchiveRepository, MongoImageArchiveRepository>();
        services.AddScoped<ImageArchiveWriter>();
        services.AddScoped<IPublicCatalogReader, MongoPublicCatalogReader>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IAttributeValidator, AttributeValidator>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IImageProcessor, ImageSharpProcessor>();

        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        services.AddSingleton<IShowcaseImageQueue, ShowcaseImageQueue>();

        services.AddScoped<IExternalAccountRepository, MongoExternalAccountRepository>();
        services.AddScoped<ISyncJobRepository, MongoSyncJobRepository>();
        services.AddScoped<IItemSyncWriter, MongoItemSyncWriter>();
        services.AddScoped<IItemEnrichWriter, MongoItemEnrichWriter>();

        var steam = configuration.GetSection(SteamOptions.SectionName).Get<SteamOptions>() ?? new SteamOptions();

        services.AddHttpClient<SteamProvider>(client =>
            {
                client.BaseAddress = new Uri(steam.BaseAddress);
                client.Timeout = TimeSpan.FromSeconds(steam.TimeoutSeconds);
            })
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(steam.TimeoutSeconds);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(steam.TimeoutSeconds * 4);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(steam.TimeoutSeconds * 4);
            });

        services.AddHttpClient<OpenGraphProvider>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MyCollection/1.0 (+metadata-fetch)");
                client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
            })
            .AddStandardResilienceHandler();

        services.AddHttpClient(ShowcaseImageDownloader.HttpClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(30));

        services.AddHostedService<ShowcaseImageDownloader>();

        services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<SteamProvider>());
        services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<OpenGraphProvider>());

        // IGDB 是選配功能：沒有憑證就整組不註冊，/ingest/providers 自然不會列出它，
        // 前端據此隱藏入口。比「註冊了但呼叫時才炸」乾淨。
        var igdb = configuration.GetSection(IgdbOptions.SectionName).Get<IgdbOptions>() ?? new IgdbOptions();

        if (igdb.IsConfigured)
        {
            // token 快取與速率限制都必須是 singleton——每個請求各自一份等於沒有
            services.AddSingleton<ITwitchTokenProvider, TwitchTokenProvider>();
            services.AddSingleton<IgdbRateLimiter>();

            services.AddHttpClient(TwitchTokenProvider.HttpClientName, client =>
                {
                    client.BaseAddress = new Uri(igdb.TokenBaseAddress);
                    client.Timeout = TimeSpan.FromSeconds(igdb.TimeoutSeconds);
                })
                .AddStandardResilienceHandler();

            services.AddHttpClient<IgdbProvider>(client =>
                {
                    client.BaseAddress = new Uri(igdb.BaseAddress);
                    client.Timeout = TimeSpan.FromSeconds(igdb.TimeoutSeconds);
                })
                .AddStandardResilienceHandler(options =>
                {
                    // 401 要由 IgdbProvider 自己換 token 重試，不能被韌性層吃掉重打
                    options.Retry.MaxRetryAttempts = 2;
                    options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(igdb.TimeoutSeconds);
                    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(igdb.TimeoutSeconds * 4);
                    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(igdb.TimeoutSeconds * 4);
                });

            services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<IgdbProvider>());
        }

        services.AddScoped<ProviderRegistry>();

        return services;
    }
}
