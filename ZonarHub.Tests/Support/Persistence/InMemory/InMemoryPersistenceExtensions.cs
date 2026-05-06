using Microsoft.Extensions.DependencyInjection;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

/// <summary>
/// Registers the in-memory persistence adapter.
/// All stores are singletons (shared in-process state).
/// All repositories are scoped (per-request facade over the shared store).
/// </summary>
internal static class InMemoryPersistenceExtensions
{
    internal static IServiceCollection AddInMemoryPersistence(this IServiceCollection services)
    {
        // Shared stores
        services.AddSingleton<InMemorySystemSettingStore>();
        services.AddSingleton<InMemoryOrganizationStore>();
        services.AddSingleton<InMemorySportStore>();
        services.AddSingleton<InMemoryOrganizationSportStore>();
        services.AddSingleton<InMemoryTenantSportStore>();
        services.AddSingleton<InMemoryComplexStore>();
        services.AddSingleton<InMemoryCourtStore>();
        services.AddSingleton<InMemoryTournamentStore>();
        services.AddSingleton<InMemoryRegistrationStore>();
        services.AddSingleton<InMemoryUserStore>();
        services.AddSingleton<InMemoryUserOrganizationAssignmentStore>();
        services.AddSingleton<InMemoryUserOrganizationPermissionStore>();
        services.AddSingleton<InMemoryTenantStore>();
        services.AddSingleton<InMemoryEmailTemplateStore>();
        services.AddSingleton<ISystemPermissionCatalogRepository, InMemorySystemPermissionCatalogRepository>();

        // Repository adapters
        services.AddScoped<ISystemSettingRepository, InMemorySystemSettingRepository>();
        services.AddScoped<IOrganizationRepository, InMemoryOrganizationRepository>();
        services.AddScoped<ISportRepository, InMemorySportRepository>();
        services.AddScoped<IOrganizationSportRepository, InMemoryOrganizationSportRepository>();
        services.AddScoped<ITenantSportRepository, InMemoryTenantSportRepository>();
        services.AddScoped<IComplexRepository, InMemoryComplexRepository>();
        services.AddScoped<ICourtRepository, InMemoryCourtRepository>();
        services.AddScoped<ITournamentRepository, InMemoryTournamentRepository>();
        services.AddScoped<IRegistrationReadRepository, InMemoryRegistrationReadRepository>();
        services.AddScoped<IUserRepository, InMemoryUserRepository>();
        services.AddScoped<IUserOrganizationAssignmentRepository, InMemoryUserOrganizationAssignmentRepository>();
        services.AddScoped<IUserOrganizationPermissionRepository, InMemoryUserOrganizationPermissionRepository>();
        services.AddScoped<ITenantRepository, InMemoryTenantRepository>();
        services.AddScoped<IEmailTemplateRepository, InMemoryEmailTemplateRepository>();

        // Unit of work (no-op for in-memory; mutations are immediate)
        services.AddScoped<IUnitOfWork, InMemoryUnitOfWork>();

        return services;
    }
}
