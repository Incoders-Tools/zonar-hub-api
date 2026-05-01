using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZonarHub.Infrastructure.Persistence.Supabase;

namespace ZonarHub.Infrastructure.Persistence;

/// <summary>
/// Entry point for persistence registration.
/// </summary>
public static class PersistenceExtensions
{
    /// <summary>
    /// Registers Supabase persistence.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration
            .GetSection(PersistenceOptions.SectionName)
            .Get<PersistenceOptions>() ?? new PersistenceOptions();

        if (!options.Provider.Equals("Supabase", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported persistence provider: '{options.Provider}'. Supported provider: Supabase.");
        }

        return services.AddSupabasePersistence(configuration);
    }
}
