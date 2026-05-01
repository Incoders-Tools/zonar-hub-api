using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminUsers;

internal static class AdminUserAuthorization
{
    public static bool IsSystemAdmin(User caller) => caller.Role == UserRole.SystemAdmin;

    public static bool CanManageUser(User caller, User target)
    {
        if (IsSystemAdmin(caller))
        {
            return true;
        }

        return caller.Role == UserRole.Admin &&
               caller.TenantId is { } tenantId &&
               target.TenantId == tenantId &&
               target.Role != UserRole.SystemAdmin;
    }

    public static bool CanAssignRole(User caller, UserRole role)
    {
        if (IsSystemAdmin(caller))
        {
            return true;
        }

        return role != UserRole.SystemAdmin;
    }
}
