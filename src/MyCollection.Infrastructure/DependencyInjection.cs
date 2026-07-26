using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyCollection.Application.Auth;
using MyCollection.Application.Categories;
using MyCollection.Application.Common;
using MyCollection.Application.Items;
using MyCollection.Application.Media;
using MyCollection.Application.Sharing;
using MyCollection.Infrastructure.Imaging;
using MyCollection.Infrastructure.Mongo;
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

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<MongoContext>();

        services.AddScoped<IUserRepository, MongoUserRepository>();
        services.AddScoped<ICategoryRepository, MongoCategoryRepository>();
        services.AddScoped<IItemRepository, MongoItemRepository>();
        services.AddScoped<IShareLinkRepository, MongoShareLinkRepository>();
        services.AddScoped<IPublicCatalogReader, MongoPublicCatalogReader>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IAttributeValidator, AttributeValidator>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IImageProcessor, ImageSharpProcessor>();

        return services;
    }
}
