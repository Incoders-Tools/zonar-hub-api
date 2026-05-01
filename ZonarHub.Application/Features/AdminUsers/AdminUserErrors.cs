using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminUsers;

public static class AdminUserErrors
{
    public static readonly Error CallerNotAuthenticated = Error.Failure(
        "admin_users.caller_not_authenticated",
        "admin.users.errors.caller_not_authenticated");

    public static readonly Error CallerNotFound = Error.NotFound(
        "admin_users.caller_not_found",
        "admin.users.errors.caller_not_found");

    public static readonly Error Forbidden = Error.Failure(
        "admin_users.forbidden",
        "admin.users.errors.forbidden");

    public static readonly Error RoleIdInvalid = Error.Validation(
        "admin_users.role_id_invalid",
        "admin.users.errors.role_id_invalid");

    public static readonly Error RoleEscalationForbidden = Error.Failure(
        "admin_users.role_escalation_forbidden",
        "admin.users.errors.role_escalation_forbidden");

    public static readonly Error UserNotFound = Error.NotFound(
        "admin_users.user_not_found",
        "admin.users.errors.user_not_found");

    public static readonly Error CannotDeleteSelf = Error.Validation(
        "admin_users.cannot_delete_self",
        "admin.users.errors.cannot_delete_self");

    public static readonly Error OrganizationScopeInvalid = Error.Validation(
        "admin_users.organization_scope_invalid",
        "admin.users.errors.organization_scope_invalid");
}
