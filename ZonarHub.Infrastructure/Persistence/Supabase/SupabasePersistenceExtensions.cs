using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal static class SupabasePersistenceExtensions
{
    private const string SecretKeyPrefix = "sb_secret_";
    private const string PublishableKeyPrefix = "sb_publishable_";

    // Strict ASCII shapes, anchored with \z so a trailing newline cannot match. Values are never trimmed:
    // anything else (padding, control characters, case-mangled prefixes, unknown keys) is rejected by
    // options validation before the value can reach HTTP header constructors, whose errors may echo it.
    private static readonly Regex SecretKeyFormat = new(
        @"\Asb_secret_[A-Za-z0-9_-]+\z", RegexOptions.CultureInvariant);

    // Legacy keys are JWTs: base64url header.payload.signature where header and payload are JSON ("eyJ").
    private static readonly Regex LegacyJwtFormat = new(
        @"\AeyJ[A-Za-z0-9_-]*\.eyJ[A-Za-z0-9_-]*\.[A-Za-z0-9_-]+\z", RegexOptions.CultureInvariant);

    internal static IServiceCollection AddSupabasePersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SupabaseOptions>()
            .Bind(configuration.GetSection(SupabaseOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Url), $"{SupabaseOptions.SectionName}:Url is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Key), $"{SupabaseOptions.SectionName}:Key is required.")
            .Validate(
                o => o.Key is null || !o.Key.StartsWith(PublishableKeyPrefix, StringComparison.Ordinal),
                $"{SupabaseOptions.SectionName}:Key must be a server-side secret key; a publishable key is not supported.")
            .Validate(
                o => string.IsNullOrWhiteSpace(o.Key) || IsSupportedKeyFormat(o.Key),
                $"{SupabaseOptions.SectionName}:Key has an unsupported format; expected a server-side sb_secret_ key or a legacy JWT.")
            .ValidateOnStart();

        services.AddScoped<SupabaseOperationContext>();

        services.AddHttpClient(SupabaseHttpClientName.Name, (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<SupabaseOptions>>().Value;
            client.BaseAddress = new Uri(opts.Url.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("apikey", opts.Key);
            // New sb_secret_ keys are not JWTs and must only travel in the apikey header.
            // Legacy JWT keys keep the Bearer header during the migration overlap.
            if (!opts.Key.StartsWith(SecretKeyPrefix, StringComparison.Ordinal))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", opts.Key);
            }

            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        services.AddScoped<ISystemSettingRepository, SystemSettingRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<ISportRepository, SportRepository>();
        services.AddScoped<IOrganizationSportRepository, OrganizationSportRepository>();
        services.AddScoped<ITenantSportRepository, TenantSportRepository>();
        services.AddScoped<IComplexRepository, ComplexRepository>();
        services.AddScoped<ICourtRepository, CourtRepository>();
        services.AddScoped<ITournamentRepository, TournamentRepository>();
        services.AddScoped<ITournamentAdminRepository, TournamentAdminRepository>();
        services.AddScoped<IRegistrationReadRepository, RegistrationReadRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserOrganizationAssignmentRepository, UserOrganizationAssignmentRepository>();
        services.AddScoped<IUserOrganizationPermissionRepository, UserOrganizationPermissionRepository>();
        services.AddScoped<ISystemPermissionCatalogRepository, SystemPermissionCatalogRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();
        services.AddScoped<ITournamentModalityRepository, TournamentModalityRepository>();
        services.AddScoped<ITournamentStatusRepository, TournamentStatusRepository>();
        services.AddScoped<ITournamentRuleRepository, TournamentRuleRepository>();
        services.AddScoped<IGenderRepository, GenderRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IImpersonationSessionStore, SupabaseImpersonationSessionStore>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }

    private static bool IsSupportedKeyFormat(string key) =>
        SecretKeyFormat.IsMatch(key) || LegacyJwtFormat.IsMatch(key);
}
