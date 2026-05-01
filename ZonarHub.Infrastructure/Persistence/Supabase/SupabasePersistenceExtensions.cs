using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal static class SupabasePersistenceExtensions
{
    internal static IServiceCollection AddSupabasePersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SupabaseOptions>()
            .Bind(configuration.GetSection(SupabaseOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Url), $"{SupabaseOptions.SectionName}:Url is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Key), $"{SupabaseOptions.SectionName}:Key is required.")
            .ValidateOnStart();

        services.AddScoped<SupabaseOperationContext>();

        services.AddHttpClient(SupabaseHttpClientName.Name, (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<SupabaseOptions>>().Value;
            client.BaseAddress = new Uri(opts.Url.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("apikey", opts.Key);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", opts.Key);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        services.AddScoped<ISystemSettingRepository, SystemSettingRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<ISocialNetworkRepository, SocialNetworkRepository>();
        services.AddScoped<ISportRepository, SportRepository>();
        services.AddScoped<IOrganizationSportRepository, OrganizationSportRepository>();
        services.AddScoped<ITenantSportRepository, TenantSportRepository>();
        services.AddScoped<IComplexRepository, ComplexRepository>();
        services.AddScoped<ICourtRepository, CourtRepository>();
        services.AddScoped<ITournamentRepository, TournamentRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
