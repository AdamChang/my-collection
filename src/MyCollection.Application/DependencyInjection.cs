using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MyCollection.Application.Common;
using MyCollection.Application.Ingestion;

namespace MyCollection.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // 補完工作的實作。兩條執行路徑（請求內、背景佇列）共用同一個實例定義。
        services.AddScoped<EnrichJobRunner>();

        return services;
    }
}
