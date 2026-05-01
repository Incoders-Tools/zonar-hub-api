using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.SystemSettings;

public static class SystemSettingErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "system_settings.not_found",
        "system_settings.errors.not_found");

    public static readonly Error KeyAlreadyExists = Error.Conflict(
        "system_settings.key_exists",
        "system_settings.errors.key_exists");

    public static readonly Error KeyRequired = Error.Validation(
        "system_settings.key_required",
        "system_settings.errors.key_required");

    public static readonly Error ValueRequired = Error.Validation(
        "system_settings.value_required",
        "system_settings.errors.value_required");

    public static readonly Error TenantIdRequired = Error.Validation(
        "system_settings.tenant_required",
        "system_settings.errors.tenant_required");

    public static readonly Error UserIdRequired = Error.Validation(
        "system_settings.user_required",
        "system_settings.errors.user_required");

    public static readonly Error ScopeOwnershipViolation = Error.Validation(
        "system_settings.scope_ownership_violation",
        "system_settings.errors.scope_ownership_violation");

    public static readonly Error CrossTenantAccessDenied = Error.Failure(
        "system_settings.cross_tenant_denied",
        "system_settings.errors.cross_tenant_denied");
}
