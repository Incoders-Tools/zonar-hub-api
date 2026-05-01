using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Abstractions.ExternalServices;
using ZonarHub.Infrastructure.Auth;
using ZonarHub.Infrastructure.Caching.InMemory;
using ZonarHub.Infrastructure.ExternalServices;
using ZonarHub.Infrastructure.Persistence;
using ZonarHub.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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

        services.AddSingleton<ICacheStore, InMemoryCacheStore>();
        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        services.AddExchangeRateGateway(configuration);

        return services;
    }


    private static IServiceCollection AddExchangeRateGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ExchangeRateOptions>()
            .Bind(configuration.GetSection(ExchangeRateOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.BaseUrl) && Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                $"{ExchangeRateOptions.SectionName}:BaseUrl must be a valid absolute URL.")
            .ValidateOnStart();

        services.AddHttpClient<ExchangeRateApiClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<ExchangeRateOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
        });

        services.AddScoped<IExchangeRateGateway, ExchangeRateGateway>();

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
