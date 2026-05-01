using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminUsers;

internal static class AdminUserRoleMapper
{
    public static string ToRoleId(UserRole role) => role switch
    {
        UserRole.SystemAdmin => "role001",
        UserRole.Admin => "role002",
        UserRole.Viewer => "role003",
        _ => "role003",
    };

    public static string ToRoleName(UserRole role) => role switch
    {
        UserRole.SystemAdmin => "system_admin",
        UserRole.Admin => "admin",
        UserRole.Viewer => "viewer",
        UserRole.Player => "player",
        _ => "user",
    };

    public static bool TryFromRoleId(string roleId, out UserRole role)
    {
        switch ((roleId ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "role001":
                role = UserRole.SystemAdmin;
                return true;
            case "role002":
                role = UserRole.Admin;
                return true;
            case "role003":
                role = UserRole.Viewer;
                return true;
            default:
                role = UserRole.Viewer;
                return false;
        }
    }
}
