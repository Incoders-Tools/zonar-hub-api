using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Organizations;

public static class OrganizationErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "organizations.not_found",
        "organizations.errors.not_found");

    public static readonly Error DisplayNameRequired = Error.Validation(
        "organizations.display_name_required",
        "organizations.errors.display_name_required");

    public static readonly Error TypeInvalid = Error.Validation(
        "organizations.type_invalid",
        "organizations.errors.type_invalid");

    public static readonly Error TenantIdRequired = Error.Validation(
        "organizations.tenant_id_required",
        "organizations.errors.tenant_id_required");

    public static readonly Error CrossTenantAccessDenied = Error.Validation(
        "organizations.cross_tenant_access_denied",
        "organizations.errors.cross_tenant_access_denied");
}
