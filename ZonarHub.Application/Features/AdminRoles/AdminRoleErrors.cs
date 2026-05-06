using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminRoles;

public static class AdminRoleErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "admin_roles.not_found",
        "admin.roles.errors.not_found");

    public static readonly Error NameRequired = Error.Validation(
        "admin_roles.name_required",
        "admin.roles.errors.name_required");

    public static readonly Error NameInvalid = Error.Validation(
        "admin_roles.name_invalid",
        "admin.roles.errors.name_invalid");

    public static readonly Error NameAlreadyExists = Error.Conflict(
        "admin_roles.name_exists",
        "admin.roles.errors.name_exists");

    public static readonly Error DescriptionRequired = Error.Validation(
        "admin_roles.description_required",
        "admin.roles.errors.description_required");

    public static readonly Error ImmutableSystemRole = Error.Failure(
        "admin_roles.immutable_system_role",
        "admin.roles.errors.immutable_system_role");
}
