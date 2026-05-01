using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Infrastructure.Persistence.Supabase;

namespace ZonarHub.Infrastructure.Persistence;

/// <summary>
/// Entry point for persistence registration. Reads <c>Persistence:Provider</c> from configuration
/// and activates the matching persistence adapter. Application and domain layers remain unaware of
/// which provider is active.
/// </summary>
public static class PersistenceExtensions
{
    /// <summary>
    /// Registers the persistence provider selected by <c>Persistence:Provider</c> in configuration.
    /// </summary>
    /// <remarks>
    /// Supported providers:
    /// <list type="bullet">
    ///   <item><c>InMemory</c> — volatile in-process store, suitable for development and tests.</item>
    ///   <item><c>Supabase</c> — Supabase backend. Requires <c>Supabase:Url</c> and <c>Supabase:Key</c>.</item>
    /// </list>
    /// To add another provider (e.g. <c>SqlServer</c>), add a new <c>AddXxxPersistence</c> extension
    /// alongside the existing ones and extend the switch below. No other file needs to change.
    /// </remarks>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration
            .GetSection(PersistenceOptions.SectionName)
            .Get<PersistenceOptions>() ?? new PersistenceOptions();

        return options.Provider switch
        {
            "InMemory" => services.AddInMemoryPersistence(),
            "Supabase" => services.AddSupabasePersistence(configuration),
            _ => throw new InvalidOperationException(
                $"Unsupported persistence provider: '{options.Provider}'. " +
                $"Supported providers: InMemory, Supabase.")
        };
    }
}
