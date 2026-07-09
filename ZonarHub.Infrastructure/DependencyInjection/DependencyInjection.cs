using ZonarHub.Application.Abstractions;
using ZonarHub.Infrastructure.Auth;
using ZonarHub.Infrastructure.Caching;
using ZonarHub.Infrastructure.Configuration;
using ZonarHub.Infrastructure.Persistence;
using ZonarHub.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ZonarHub.Infrastructure.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddTimeProvider();
        services.AddHttpContextAccessor();

        services.AddPersistence(configuration);
        services.AddAuthInfrastructure(configuration);

        services.AddOptions<FeaturesOptions>()
            .Bind(configuration.GetSection(FeaturesOptions.SectionName));

        services.AddSingleton<IImpersonationFeatureFlags, ImpersonationFeatureFlags>();
        services.AddSingleton<ICacheStore, MemoryCacheStore>();
        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            return;
        }

        services.AddSingleton(TimeProvider.System);
    }
}
