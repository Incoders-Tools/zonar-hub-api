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

    public static readonly Error ListScopeInvalid = Error.Validation(
        "admin_users.list_scope_invalid",
        "admin.users.errors.list_scope_invalid");

    public static readonly Error ListOrganizationRequired = Error.Validation(
        "admin_users.list_organization_required",
        "admin.users.errors.list_organization_required");

    public static readonly Error UserToolPermissionForbidden = Error.Failure(
        "admin_users.user_tool_permission_forbidden",
        "admin.users.errors.user_tool_permission_forbidden");

    public static readonly Error PermissionOrganizationScopeInvalid = Error.Validation(
        "admin_users.permission_organization_scope_invalid",
        "admin.users.errors.permission_organization_scope_invalid");

    public static readonly Error PermissionToolInvalid = Error.Validation(
        "admin_users.permission_tool_invalid",
        "admin.users.errors.permission_tool_invalid");

    public static readonly Error RestrictedToolRoleInvalid = Error.Validation(
        "admin_users.restricted_tool_role_invalid",
        "admin.users.errors.restricted_tool_role_invalid");

    public static readonly Error RestrictedToolAssignmentForbidden = Error.Failure(
        "admin_users.restricted_tool_assignment_forbidden",
        "admin.users.errors.restricted_tool_assignment_forbidden");
}
