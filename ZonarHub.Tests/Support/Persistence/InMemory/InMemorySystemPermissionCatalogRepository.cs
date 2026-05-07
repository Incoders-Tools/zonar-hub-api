using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemorySystemPermissionCatalogRepository : ISystemPermissionCatalogRepository
{
    private readonly IReadOnlyList<SystemModuleDefinition> _modules;
    private readonly IReadOnlyList<SystemToolDefinition> _tools;

    public InMemorySystemPermissionCatalogRepository()
    {
        _modules = BuildModules();
        _tools = _modules.SelectMany(module => module.Tools).ToList();
    }

    public Task<IReadOnlyList<SystemModuleDefinition>> ListModulesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_modules);
    }

    public Task<IReadOnlyList<SystemToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_tools);
    }

    public Task<IReadOnlyList<SystemToolDefinition>> ListToolsByKeysAsync(
        IReadOnlyCollection<string> toolKeys,
        CancellationToken cancellationToken = default)
    {
        if (toolKeys.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<SystemToolDefinition>>([]);
        }

        var requested = new HashSet<string>(toolKeys, StringComparer.OrdinalIgnoreCase);
        var selected = _tools.Where(tool => requested.Contains(tool.Key)).ToList();

        return Task.FromResult<IReadOnlyList<SystemToolDefinition>>(selected);
    }

    private static IReadOnlyList<SystemModuleDefinition> BuildModules()
    {
        var modules = new List<SystemModuleDefinition>
        {
            new(
                "module_dashboard",
                "dashboard",
                "admin.permissions.module.dashboard",
                1,
                true,
                [
                    Tool("tool_dashboard", "dashboard", "dashboard", "admin.dashboard", "/admin", 1)
                ]),
            new(
                "module_circuit",
                "circuit",
                "admin.permissions.module.circuit",
                2,
                true,
                [
                    Tool("tool_tournaments", "tournaments", "circuit", "admin.tournaments", "/admin/tournaments", 1),
                    Tool("tool_tournament_eligibility_profiles", "tournament-eligibility-profiles", "circuit", "admin.tournamentEligibilityProfiles", "/admin/catalogs/tournament-eligibility-profiles", 2),
                    Tool("tool_tournament_rules", "tournament-rules", "circuit", "admin.tournamentRuleSets", "/admin/catalogs/tournament-rules", 3),
                    Tool("tool_registrations", "registrations", "circuit", "admin.registrations", "/admin/registrations", 4),
                    Tool("tool_players", "players", "circuit", "admin.players", "/admin/players", 5),
                    Tool("tool_teams", "teams", "circuit", "admin.teams", "/admin/teams", 6),
                    Tool("tool_draw_planner", "draw-planner", "circuit", "admin.drawPlanner", "/admin/draw-planner", 7)
                ]),
            new(
                "module_catalog",
                "catalog",
                "admin.permissions.module.catalog",
                3,
                true,
                [
                    Tool("tool_complexes", "complexes", "catalog", "admin.complexes", "/admin/catalogs/complexes", 1),
                    Tool("tool_categories", "categories", "catalog", "admin.categories", "/admin/catalogs/categories", 2),
                    Tool("tool_genders", "genders", "catalog", "admin.genders", "/admin/catalogs/genders", 3),
                    Tool("tool_sports", "sports", "catalog", "admin.sports", "/admin/catalogs/sports", 4),
                    Tool("tool_tournament_statuses", "tournament-statuses", "catalog", "admin.tournamentStatuses", "/admin/catalogs/tournament-statuses", 5),
                    Tool("tool_tournament_modalities", "tournament-modalities", "catalog", "admin.tournamentModalities", "/admin/catalogs/tournament-modalities", 6),
                    Tool("tool_flyer_backgrounds", "flyer-backgrounds", "catalog", "admin.flyerBackgrounds", "/admin/flyer-backgrounds", 7)
                ]),
            new(
                "module_system",
                "system",
                "admin.permissions.module.system",
                4,
                true,
                [
                    Tool("tool_users", "users", "system", "admin.users", "/admin/system/users", 1),
                    Tool("tool_roles", "roles", "system", "admin.roles", "/admin/system/roles", 2, true),
                    Tool("tool_organizations", "organizations", "system", "admin.organizations", "/admin/system/organizations", 3),
                    Tool("tool_plans", "plans", "system", "admin.plans", "/admin/system/plans", 4, true),
                    Tool("tool_actions", "actions", "system", "admin.nav.actions", "/admin/system/actions", 5, true),
                    Tool("tool_audit", "audit", "system", "admin.audit", "/admin/system/audit", 6, true),
                    Tool("tool_app_logs", "app-logs", "system", "admin.appLogs", "/admin/system/logs", 7, true),
                    Tool("tool_security", "security", "system", "admin.security", "/admin/system/security", 8, true),
                    Tool("tool_settings", "settings", "system", "admin.settings", "/admin/system/settings", 9),
                    Tool("tool_email_templates", "email-templates", "system", "admin.emailTemplates", "/admin/system/email-templates", 10, true),
                    Tool("tool_billing", "billing", "system", "admin.billing", "/admin/billing", 11, true)
                ])
        };

        return modules;
    }

    private static SystemToolDefinition Tool(
        string id,
        string key,
        string moduleKey,
        string labelKey,
        string route,
        int sortOrder,
        bool isSystemAdminOnly = false)
    {
        return new SystemToolDefinition(
            id,
            key,
            moduleKey,
            labelKey,
            route,
            sortOrder,
            isSystemAdminOnly,
            true);
    }
}
