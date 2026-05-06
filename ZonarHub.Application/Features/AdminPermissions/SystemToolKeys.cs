namespace ZonarHub.Application.Features.AdminPermissions;

public static class SystemToolKeys
{
    public const string Users = "users";

    public static readonly IReadOnlySet<string> RestrictedSystemTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "roles",
        "plans",
        "actions",
        "audit",
        "app-logs",
        "security",
        "email-templates",
        "billing"
    };
}
