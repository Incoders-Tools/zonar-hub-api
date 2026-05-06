using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminPermissions;

public static class AdminPermissionErrors
{
    public static readonly Error CallerNotAuthenticated = Error.Failure(
        "admin_permissions.caller_not_authenticated",
        "admin.permissions.errors.caller_not_authenticated");

    public static readonly Error CallerNotFound = Error.NotFound(
        "admin_permissions.caller_not_found",
        "admin.permissions.errors.caller_not_found");

    public static readonly Error UserNotFound = Error.NotFound(
        "admin_permissions.user_not_found",
        "admin.permissions.errors.user_not_found");

    public static readonly Error Forbidden = Error.Failure(
        "admin_permissions.forbidden",
        "admin.permissions.errors.forbidden");
}
